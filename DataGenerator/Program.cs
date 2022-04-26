using System;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Configuration;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;

namespace DataGenerator
{
    class Program
    {
        private static readonly string Endpoint = ConfigurationManager.AppSettings["Endpoint"];
        private static readonly string PrimaryKey = ConfigurationManager.AppSettings["PrimaryKey"];
        private static readonly int AutoscaleMaxThroughput = int.Parse(ConfigurationManager.AppSettings["AutoscaleMaxThroughput"]);
        private static readonly string DatabaseName = ConfigurationManager.AppSettings["DatabaseName"];

        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];
        private static readonly string PartitionKey = ConfigurationManager.AppSettings["PartitionKey"];

        private ConcurrentDictionary<int, double> requestUnitsConsumed = new ConcurrentDictionary<int, double>();
        private CosmosClient cosmosClient;

        private Database database;
        private Container container;

        private const int MinThreadPoolSize = 100;
        private const int NumRegions = 1; // Divide by # regions of your Azure Cosmos DB account for accurate RU/s calculation
        private const int DelayBetweenWritesMS = 100; // If `DelayBetweenWrites` setting true, MS to wait on client side between each insert

        private int pendingTaskCount;
        private long documentsInserted;
        private long throttlesCount;

        private static CosmosClientOptions cosmosClientOptions = new CosmosClientOptions
        {
            ApplicationName = "ContosoDemoDataGenerator",
            ApplicationRegion = Regions.EastUS2, // Set the write region of your Azure Cosmos DB account. This should be in the same region as your VM.
            ConnectionMode = ConnectionMode.Direct, // Use default of direct mode for best performance
            AllowBulkExecution = true,
            RequestTimeout = new TimeSpan(1, 0, 0),
            MaxTcpConnectionsPerEndpoint = 1000,
            MaxRetryAttemptsOnRateLimitedRequests = 10, // Retry policy - retry up to 5 times if requests are rate-limited (429)
            MaxRetryWaitTimeOnRateLimitedRequests = new TimeSpan(0, 1, 0) // Retry policy - maximum time the client should spend on retrying on 429s
        };

        private Program(CosmosClient client)//new program with the document cosmosClient
        {
            this.cosmosClient = client;
        }

        public static async Task Main(string[] args)
        {
            ThreadPool.SetMinThreads(MinThreadPoolSize, MinThreadPoolSize);

            Console.WriteLine("Data Generator:");
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine("Endpoint: {0}", Endpoint);
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine();

            Console.WriteLine("What kind of workload would you like to generate? \n\t 1. Evenly distributed \n\t 2. Skewed");
            var workload = GetOneTwoFromUser();

            Console.WriteLine("\nStarting...\n");

            try
            {

                using (var client = new CosmosClient(Endpoint, PrimaryKey, cosmosClientOptions))
                {
                    var program = new Program(client);
                    await program.RunAsync(workload);

                    Console.WriteLine("Data generation completed successfully.");
                }
            }
            catch (Exception e)
            {
                // If the Exception is a DocumentClientException, the "StatusCode" value might help identity 
                // the source of the problem. 
                Console.WriteLine("Samples failed with exception:{0}", e);
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private async Task RunAsync(int workload)
        {
            await Setup();
            await IngestData(workload);
        }

        private async Task Setup()
        {
            database = cosmosClient.GetDatabase(DatabaseName); //(await cosmosClient.CreateDatabaseIfNotExistsAsync(DatabaseName)).Database;
            var autoscaleThroughput = ThroughputProperties.CreateAutoscaleThroughput(AutoscaleMaxThroughput);
            
            //if (bool.Parse(ConfigurationManager.AppSettings["IsHierarchy"]))
            //{
            //    // Expecting format /<Prop1>/<Prop2>
            //    var paths = PartitionKey.Split('/');
            //    // This will split into { "", "<Prop1>", "<Prop2>" } so we just want the values in index 1 and 2
            //    List<string> hierarchyKeyPaths = new List<string> { $"/{paths[1]}", $"/{paths[2]}" };

            //    ContainerProperties containerProperties = new ContainerProperties(ContainerName, partitionKeyPaths: hierarchyKeyPaths);
            //    //await database.CreateContainerIfNotExistsAsync(containerProperties, autoscaleThroughput);
            //}
            //else
            //{
            //    ContainerProperties containerProperties = new ContainerProperties(ContainerName, partitionKeyPath: PartitionKey);
            //    //await database.CreateContainerIfNotExistsAsync(containerProperties, autoscaleThroughput);
            //}

            container = database.GetContainer(ContainerName);
        }

        private async Task IngestData(int workload)
        {
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

            int numberOfItemsToInsertPerTask = int.Parse(ConfigurationManager.AppSettings["NumberOfDocumentsToInsert"]) / taskCount; //determine number of documents to insert per task

            if (workload == 2)
            {
                for (var i = 0; i < taskCount; i++)
                {
                    tasks.Add(this.InsertStaticDocuments(i, numberOfItemsToInsertPerTask));
                }
            }
            else {
                var dataGenerator = new TransactionGenerator();
                for (var i = 0; i < taskCount; i++)
                {
                    tasks.Add(this.InsertCustomDocuments(dataGenerator, i, numberOfItemsToInsertPerTask));
                }
            }

            await Task.WhenAll(tasks);

            if (bool.Parse(ConfigurationManager.AppSettings["ShouldCleanupOnFinish"]))
            {
                Console.WriteLine("Deleting Database {0}", DatabaseName);
                await database.DeleteAsync();
            }
        }

        private async Task InsertCustomDocuments(TransactionGenerator tg, int taskId, int numberOfItemsToInsertPerTask)

        {
            var itemsToInsert = tg.GenerateRandomTransactions(numberOfItemsToInsertPerTask);

            requestUnitsConsumed[taskId] = 0;

            for (var i = 0; i < itemsToInsert.Count; i++)
            {
                if (bool.Parse(ConfigurationManager.AppSettings["DelayBetweenWrites"]))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(DelayBetweenWritesMS));  
                }
                try
                {
                    var newItem = itemsToInsert[i];
                    var itemResponse = await container.CreateItemAsync(newItem);

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
                        // TODO
                    }
                }
            }

            Interlocked.Decrement(ref this.pendingTaskCount); // Consider task as completed when all documents have been inserted
        }

        private async Task InsertStaticDocuments(int taskId, int numberOfItemsToInsert)
        {
            requestUnitsConsumed[taskId] = 0;

            for (var i = 0; i < numberOfItemsToInsert; i++)
            {
                if (bool.Parse(ConfigurationManager.AppSettings["DelayBetweenWrites"]))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(DelayBetweenWritesMS)); // Wait some time on client side between each insert. 
                }

                ItemResponse<Transaction> itemResponse = null;
                try
                {
                    var id = Guid.NewGuid().ToString();
                    var timestamp = DateTime.Now;
                    var newItem = new Transaction()
                    {
                        id = id,
                        TransactionId = id,
                        StoreId = 1,
                        StoreIdTransactionIdKey = $"1;{id}",
                        NumItems = 5,
                        Currency = "USD",
                        UserId = "cosmosuser",
                        Country = "USA",
                        Address = "1234 Microsoft Way",
                        Date = timestamp.ToString("yyyy-MM-dd"),
                        Timestamp = timestamp
                    };

                    itemResponse = await container.CreateItemAsync(newItem);

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

        private async Task LogOutputStats() // Compute average RU/s consumed across all tasks every 1 second
        {
            long lastCount = 0;
            double requestUnits = 0;
            double lastRequestUnits = 0;
            double lastDocumentCount = 0;
            double lastThrottleCount = 0;
            double lastSeconds = 0;

            Stopwatch watch = new Stopwatch(); // Start counting - each task starts from 0
            watch.Start();
            await Task.Delay(TimeSpan.FromSeconds(1)); // Wait 1 second

            while (this.pendingTaskCount > 0)
            {

                double seconds = watch.Elapsed.TotalSeconds;

                await Task.Delay(TimeSpan.FromSeconds(1)); // Wait 1 second

                requestUnits = 0;
                foreach (int taskId in requestUnitsConsumed.Keys) // Sum up the total RU/s consumed across all tasks
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

        private static int GetOneTwoFromUser()
        {
            int n;
            while (!int.TryParse(Console.ReadLine(), out n) || !(n == 1 || n == 2))
            {
                Console.WriteLine("Please enter either 1 or 2.");
            };

            return n;
        }
    }
}
