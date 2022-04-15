using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using System.Net;

namespace AutoscaleDemo
{
    /// <summary>   
    /// This sample generates a workload with a variable or unpredictable traffic pattern that is suited for Azure Cosmos DB autoscale provisioned throughput
    /// </summary>
    public sealed class Program
    {

        private static readonly string Endpoint = ConfigurationManager.AppSettings["Endpoint"];
        private static readonly string PrimaryKey = ConfigurationManager.AppSettings["PrimaryKey"];

        private static readonly string DatabaseName = ConfigurationManager.AppSettings["DatabaseName"];
        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];

        private static readonly string PartitionKeyPath = ConfigurationManager.AppSettings["ContainerPartitionKeyPath"];
        private static readonly string PartitionKeyPathHotPartition = ConfigurationManager.AppSettings["ContainerPartitionKeyPathForHotPartition"];

        private static readonly int AutoscaleMaxThroughput = int.Parse(ConfigurationManager.AppSettings["AutoscaleMaxThroughput"]);

        // 
        private static CosmosClientOptions cosmosClientOptions = new CosmosClientOptions
        {
            ApplicationName = "CosmosAutoscaleDemo",
            //ApplicationRegion = Regions.WestUS3, // Set the write region of your Azure Cosmos DB account. This should be in the same region as your VM.
            ConnectionMode = ConnectionMode.Direct, // Use default of direct mode for best performance
            RequestTimeout = new TimeSpan(1, 0, 0),
            MaxTcpConnectionsPerEndpoint = 1000,
            MaxRetryAttemptsOnRateLimitedRequests = 3, // Retry policy - retry up to 10 times if requests are rate-limited (429)%
            MaxRetryWaitTimeOnRateLimitedRequests = new TimeSpan(0, 1, 0), // Retry policy - maximum time the client should spend on retrying on 429s
            AllowBulkExecution = true
        };

        private const int MinThreadPoolSize = 100;
        private const int NumRegions = 1; //Divide by # regions of your Azure Cosmos DB account for accurate RU/s calculation

        private int pendingTaskCount;
        private long documentsInserted;
        private long throttlesCount;

        //private float delayBetweenInsertOperations = 800;
        private float delayBetweenInsertOperations = 0; // TODO: investigate

        /*        private float[] delayValuesInMs = new float[] { 300, 300, 300, 300, 300, 300 }; // Different delays between operations /*- *///experimentally tuned
        /**/        //private float[] delayValuesInMs = new float[] { 800, 800, 800, 800, 800, 800 }; // Different delays between */operations - experimentally tuned

        // TODO: investigate
        private float[] delayValuesInMs = new float[] { 0, 300, 600, 900, 1200, 2400 }; // Different delays between operations - experimentally tuned

        // TODO: investigate
        private int timeBetweenTrafficPatternChangeInSeconds_Demo = 15; // Change traffic pattern every 15 seconds during live demo
        private int timeBetweenTrafficPatternChangeInMinutes_DataGenerator = 45; // Change traffic pattern every 8 minutes to generate history of RU/s.

        private ConcurrentDictionary<int, double> requestUnitsConsumed = new ConcurrentDictionary<int, double>();
        private CosmosClient cosmosClient;

        private Database database;
        private Container container;

        /// <summary>
        /// Initializes a new instance of the <see cref="Program"/> class.
        /// </summary>
        /// <param name="client">The Cosmos DB cosmosClient instance.</param>
        private Program(CosmosClient client)//new program with the document cosmosClient
        {
            this.cosmosClient = client;
        }

        /// <summary>
        /// Main method for the sample.
        /// </summary>
        /// <param name="args">command line arguments.</param>
        public static async Task Main(string[] args)
        {
            ThreadPool.SetMinThreads(MinThreadPoolSize, MinThreadPoolSize);

            Console.WriteLine("Summary:");
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine("Endpoint: {0}", Endpoint);
            //Console.WriteLine("Container : {0}.{1} with autoscale max RU/s of {2} RU/s", DatabaseName, ContainerName, ConfigurationManager.AppSettings["AutoscaleMaxThroughput"]);
            Console.WriteLine("Container : {0}.{1}", DatabaseName, ContainerName);


            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine();

            Console.WriteLine("Demo starting...\n");

            try
            {

                using (var client = new CosmosClient(Endpoint, PrimaryKey, cosmosClientOptions))
                {
                    var program = new Program(client);
                    await program.RunAsync();

                    Console.WriteLine("Cosmos DB Benchmark completed successfully.");
                }
            }

#if !DEBUG
            catch (Exception e)
            {
                // If the Exception is a DocumentClientException, the "StatusCode" value might help identity 
                // the source of the problem. 
                Console.WriteLine("Samples failed with exception:{0}", e);
            }
#endif

            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadLine();
            }
        }

        /// <summary>
        /// Inserts data into Azure Cosmos DB at variable rate
        /// </summary>
        /// <returns>a Task object.</returns>
        private async Task RunAsync()
        {
            SetupCosmosResources();

            int taskCount;
            int degreeOfParallelism = int.Parse(ConfigurationManager.AppSettings["DegreeOfParallelism"]);

            if (degreeOfParallelism == -1)
            {
                taskCount = Math.Max((int)AutoscaleMaxThroughput / 1000, 250);
            }
            else
            {
                taskCount = degreeOfParallelism;
            }

            Console.WriteLine("Starting workload inserts...");

            pendingTaskCount = taskCount;
            var tasks = new List<Task>();
            // Prints out average RU/s consumed and writes/s every second. Runs until all inserts are completed. 
            tasks.Add(this.LogOutputStats());

            // Changes the delay between inserts on client side, which changes the effective consumed RU/s.
            tasks.Add(this.ChangeDelayBetweenOperations());

            int numberOfItemsToInsertPerTask = int.Parse(ConfigurationManager.AppSettings["NumberOfDocumentsToInsert"]) / taskCount; //determine number of documents to insert per task

            string sampleDocument = File.ReadAllText(ConfigurationManager.AppSettings["DocumentTemplateFile"]);

            for (var i = 0; i < taskCount; i++)
            {
                // Choose ONE of the below for inserting data
                // Option 1: Use this for basic demo scenarios
                //tasks.Add(this.InsertBasicDocuments(i, sampleDocument, numberOfItemsToInsertPerTask));

                //Option 2: Use InsertCustomDocuments to use data generated by Bogus. Note, overall throughput may be lower due to the additional overhead of generating documents on the client side  
                tasks.Add(this.InsertCustomDocuments(i, numberOfItemsToInsertPerTask));
            }

            await Task.WhenAll(tasks);

            if (bool.Parse(ConfigurationManager.AppSettings["ShouldCleanupOnFinish"]))
            {
                Console.WriteLine("Deleting Database {0}", DatabaseName);
                await database.DeleteAsync();
            }
        }

        private void SetupCosmosResources()
        {

            //var autoscaleThroughput = ThroughputProperties.CreateAutoscaleThroughput(AutoscaleMaxThroughput);
            //database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName, autoscaleThroughput);


            //ContainerProperties containerProperties = new ContainerProperties(ContainerName, PartitionKeyPath);


            //ContainerProperties containerProperties = new ContainerProperties(ContainerName, PartitionKeyPathHotPartition);


            
            container = cosmosClient.GetDatabase(DatabaseName).GetContainer(ContainerName);


            /*if (bool.Parse(ConfigurationManager.AppSettings["ShouldCleanupOnStart"]))
            {
                // Delete the existing database
                await cosmosClient.GetDatabase(DatabaseName).DeleteAsync();

                Console.WriteLine("Deleted database {0}...\n", DatabaseName);

                // Create new database
                await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName);
                Console.WriteLine("Re-created new database {0}..\n", DatabaseName);

                // Create new container with autoscale
                container = await CreateNewContainerWithAutoscale(DatabaseName, ContainerName, AutoscaleMaxThroughput);

                var throughputProperties = await container.ReadThroughputAsync(requestOptions: null); // Read the throughput of the container
                var currentAutoscaleMaxThroughput = throughputProperties.Resource.AutoscaleMaxThroughput;
                Console.WriteLine("Created container {0} with autoscale max RU/s of {1} RU/s...", ContainerName, currentAutoscaleMaxThroughput);

            }*/

        }

        /// <summary>
        /// Helper method to insert data into Azure Cosmos DB at variable rate, using pre-defined Event.json format
        /// </summary>
        /// <returns>a Task object.</returns>
        private async Task InsertBasicDocuments(int taskId, string sampleDocument, int numberOfItemsToInsert)
        {
            requestUnitsConsumed[taskId] = 0;

            // Sample document:
            Dictionary<string, object> newItem = JsonConvert.DeserializeObject<Dictionary<string, object>>(sampleDocument);

            for (var i = 0; i < numberOfItemsToInsert; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayBetweenInsertOperations)); // Wait some time on client side between each insert. 

                ItemResponse<Dictionary<string, object>> itemResponse = null;
                try
                {
                    //var partitionKey = Guid.NewGuid().ToString();
                    //newItem["id"] = partitionKey;

                    newItem["id"] = Guid.NewGuid().ToString();
                    newItem["SyntheticKey"] = $"{newItem["StoreId"].ToString()};{newItem["id"]}";
                    var partitionKey = (newItem["SyntheticKey"].ToString());

                    itemResponse = await container.CreateItemAsync(newItem, new PartitionKey(partitionKey));

                    requestUnitsConsumed[taskId] += itemResponse.RequestCharge / NumRegions; // Keep track of how many RU/s have been consumed for this task

                    Interlocked.Increment(ref this.documentsInserted); // Increment # doc inserted
                }
                catch (CosmosException e)
                {
                    if (e.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        requestUnitsConsumed[taskId] += e.RequestCharge / NumRegions;
                        Interlocked.Increment(ref this.throttlesCount);
                        //Interlocked.Increment(ref this.documentsInserted);
                    }
                    else
                    {
                        //catchall
                    }

                }
                catch (Exception ex)
                {
                    int x = 0;
                }
            }

            Interlocked.Decrement(ref this.pendingTaskCount); // Consider task as completed when all documents have been inserted
        }

        /// <summary>
        /// Helper method to insert data into Azure Cosmos DB at variable rate, using custom data generated using Bogus format
        /// </summary>
        /// <returns>a Task object.</returns>
        private async Task InsertCustomDocuments(int taskId, int numberOfItemsToInsertPerTask)

        {
            var itemsToInsert = Util.GenerateRandomPaymentEvent(numberOfItemsToInsertPerTask);

            requestUnitsConsumed[taskId] = 0;

            for (var i = 0; i < itemsToInsert.Count; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayBetweenInsertOperations)); // Wait some time on client side between each insert. 

                try
                {
                    var newItem = itemsToInsert[i];
                    var itemResponse = await container.CreateItemAsync(newItem);

                    requestUnitsConsumed[taskId] += itemResponse.RequestCharge / NumRegions; // Keep track of how many RU/s have been consumed for this task

                    Interlocked.Increment(ref this.documentsInserted); // Increment # doc inserted
                }
                catch (Exception e)
                {

                    Interlocked.Increment(ref this.documentsInserted);
                }
            }

            Interlocked.Decrement(ref this.pendingTaskCount); // Consider task as completed when all documents have been inserted
        }

        private async Task ChangeDelayBetweenOperations()
        {
            while (this.pendingTaskCount > 0)
            {
                // Choose ONE of the below, based on the scenario
                // Every <> amount of time, we change the client-side delay to change the workload traffic pattern

                // Option 1: Run this to generate history of RU/s on Azure Cosmos DB. Recommended to run for a few hours before demo to generate a nice graph.
                await Task.Delay(TimeSpan.FromMinutes(timeBetweenTrafficPatternChangeInMinutes_DataGenerator));

                // // Option 2: Show traffic pattern changing frequently in a live demo
                //await Task.Delay(TimeSpan.FromSeconds(timeBetweenTrafficPatternChangeInSeconds_Demo));

                Random rand = new Random();
                int index = rand.Next(delayValuesInMs.Length);
                float newDelay = delayValuesInMs[index];

                Interlocked.Exchange(ref delayBetweenInsertOperations, newDelay);

                Console.BackgroundColor = ConsoleColor.Green;
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine("Traffic pattern has changed! Time between inserts is now {0}ms", delayBetweenInsertOperations);
                Console.ResetColor();
            }

        }

        /// <summary>
        /// Helper method to log the throughput (RU/s and writes/s) of the operations
        /// </summary>
        /// <returns>a Task object.</returns>

        private async Task LogOutputStats() // Compute average RU/s consumed across all tasks every 1 second
        {
            long lastCount = 0;
            double requestUnits = 0;
            double lastRequestUnits = 0;
            double lastDocumentCount = 0;
            double lastThrottleCount = 0;
            double lastSeconds = 0;

            Stopwatch watch = new Stopwatch(); //start counting - each task starts from 0
            watch.Start();
            await Task.Delay(TimeSpan.FromSeconds(1)); //wait 1 second

            while (this.pendingTaskCount > 0)
            {

                double seconds = watch.Elapsed.TotalSeconds;

                await Task.Delay(TimeSpan.FromSeconds(1)); //wait 1 second

                requestUnits = 0;
                foreach (int taskId in requestUnitsConsumed.Keys) // Sum up the total RU/s consumed across all task
                {
                    requestUnits += requestUnitsConsumed[taskId];
                }

                long currentCount = this.documentsInserted;
                long currentThrottleCount = this.throttlesCount;

                if (currentCount > 0)
                {
                    Console.WriteLine("Inserted {0} docs @ {1} writes/s with average throughput {2} RU/s, throttles count {3}",
                        currentCount - lastDocumentCount,
                        Math.Round((currentCount - lastDocumentCount) / (seconds - lastSeconds)),
                        Math.Round((requestUnits - lastRequestUnits) / (seconds - lastSeconds)),
                        currentThrottleCount - lastThrottleCount);
                }
                
                lastCount = documentsInserted;
                lastRequestUnits = requestUnits;
                lastDocumentCount = currentCount;
                lastThrottleCount = currentThrottleCount;
                lastSeconds = seconds;
            }

            // When all tasks have finished, print a summary
            double totalSeconds = watch.Elapsed.TotalSeconds;
            double ruPerSecond = requestUnits / totalSeconds;

            Console.WriteLine();
            Console.WriteLine("Summary:");
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine("Inserted {0} total docs @ {1} writes/s with average throughput {2} RU/s)",
                lastCount,
                Math.Round(this.documentsInserted / watch.Elapsed.TotalSeconds),
                Math.Round(ruPerSecond)
               );
            Console.WriteLine("--------------------------------------------------------------------- ");
        }

        /// <summary>
        /// Create a new autoscale container
        /// </summary>
        /// <returns>The created container.</returns>
        private async Task<Container> CreateNewContainerWithAutoscale(string databaseName, string containerName, int autoscaleMaxThroughput)
        {
            ContainerProperties containerProperties = new ContainerProperties(containerName, PartitionKeyPath);
            var autoscaleThroughput = ThroughputProperties.CreateAutoscaleThroughput(autoscaleMaxThroughput);

            Container container = await cosmosClient.GetDatabase(databaseName).CreateContainerIfNotExistsAsync(containerProperties, autoscaleThroughput);

            // Show user cost of running this per month - container scales between 10% max RU/s and max RU/s
            double containerMinCostPerMonth = 730 * (0.012 * 0.1 * AutoscaleMaxThroughput) / 100;
            double containerMaxCostPerMonth = 730 * (0.012 * AutoscaleMaxThroughput) / 100;

            double containerMinCostPerHour = (0.012 * 0.1 * AutoscaleMaxThroughput) / 100;
            double containerMaxCostPerHour = (0.012 * AutoscaleMaxThroughput) / 100;

            Console.WriteLine("Found container {0} with autoscale between {1} to {2} RU/s.\nHourly cost will be between ${3} to ${4} based on usage. Monthly cost will be between ${5} to ${6} based on usage\n", container.Id, 0.1 * AutoscaleMaxThroughput, AutoscaleMaxThroughput, containerMinCostPerHour, containerMaxCostPerHour, containerMinCostPerMonth, containerMaxCostPerMonth);

            //Console.WriteLine("Press enter to continue ...");
            //Console.ReadLine();

            return container;
        }

        // Sample to show how to create a container with standard (manual) throughput
        private async Task<Container> CreateNewContainerWithProvisioned(string databaseName, string containerName, int throughput)
        {
            ContainerProperties containerProperties = new ContainerProperties(containerName, PartitionKeyPath);

            Container container = await cosmosClient.GetDatabase(databaseName).CreateContainerIfNotExistsAsync(containerProperties, ThroughputProperties.CreateManualThroughput(throughput));

            return container;
        }

    }
}
