using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ContosoDemo
{
    class Program
    {
        private static readonly string Endpoint = ConfigurationManager.AppSettings["Endpoint"];
        private static readonly string PrimaryKey = ConfigurationManager.AppSettings["PrimaryKey"];

        private static readonly string DatabaseName = ConfigurationManager.AppSettings["DatabaseName"];

        private static readonly string Container_PartitionBySynthetic = ConfigurationManager.AppSettings["Container_PartitionBySynthetic"];
        private static readonly string Container_PartitionByHierarchy = ConfigurationManager.AppSettings["Container_PartitionByHierarchy"];
        private static readonly string Container_PartitionMerged = ConfigurationManager.AppSettings["Container_PartitionMerged"];

        private static readonly int AutoscaleMaxThroughput = int.Parse(ConfigurationManager.AppSettings["AutoscaleMaxThroughput"]);

        private CosmosClient cosmosClient;

        private Database database;
        private Container containerPartitionBySynthetic;
        private Container containerPartitionByHierarchy;
        //private Container containerPartitionMerged;

        private Program(CosmosClient client)//new program with the document cosmosClient
        {
            this.cosmosClient = client;
        }

        static async Task Main(string[] args)
        {
            try
            {
                using (var client = new CosmosClient(Endpoint, PrimaryKey))
                {
                    var program = new Program(client);
                    program.Setup();
                    await program.RunBenchmark();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Sample failed with exception:{0}", e);
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private void Setup()
        {
            database = cosmosClient.GetDatabase(DatabaseName);

            containerPartitionBySynthetic = database.GetContainer(Container_PartitionBySynthetic);
            containerPartitionByHierarchy = database.GetContainer(Container_PartitionByHierarchy);
            //containerPartitionMerged = database.GetContainer(Container_PartitionMerged);
        }

        public async Task RunBenchmark()
        {
            //var sqlQueryText1 = "SELECT TOP 100 * FROM c WHERE c.StoreId = 'qi6r89' and c.Amount > 20";
            var sqlQueryText1 = "SELECT * FROM c WHERE c.StoreId = 'qi6r89' and c.Amount > 20";

            Console.WriteLine("Running query: " + sqlQueryText1 + " across 3 containers each partitioned with \n 1. TenantId \n 2. A synthetic key with TenantId+UserId \n 3. A subpartition with TenantId and UserId \n");

            await RunQuery(sqlQueryText1, containerPartitionBySynthetic);
            await RunQuery(sqlQueryText1, containerPartitionByHierarchy);
            //await RunQuery(sqlQueryText1, containerSubpartitionByTenantId_UserId);

            Console.WriteLine("--------------------------------");

            //var sqlQueryText2 = "SELECT TOP 100 * FROM c WHERE c.TenantId = 'Turcotte and Sons' AND c.UserId = 'Audrey19' AND c.Amount >= 100";

            //await RunQuery(sqlQueryText2, containerPartitionBySynthetic);
            //await RunQuery(sqlQueryText2, containerPartitionByHierarchy);
            ////await RunQuery(sqlQueryText2, containerSubpartitionByTenantId_UserId);

            //var sqlQueryText3 = "SELECT TOP 100 * FROM c WHERE (c.TenantId = 'Fabrikam' AND c.UserId = 'Burdette.Grimes69') or (c.TenantId = 'Microsoft' AND c.UserId = 'Marjolaine_Mayer14')";

            //await RunQuery(sqlQueryText3, containerPartitionBySynthetic);
            //await RunQuery(sqlQueryText3, containerPartitionByHierarchy);
            ////await RunQuery(sqlQueryText3, containerSubpartitionByTenantId_UserId);

            //var sqlQueryText4 = "SELECT TOP 100 * FROM c WHERE (c.TenantId = 'Fabrikam' AND c.UserId = 'Burdette.Grimes69') AND c.Country = 'Qatar'";
            //await RunQuery(sqlQueryText4, containerPartitionBySynthetic);
            //await RunQuery(sqlQueryText4, containerPartitionByHierarchy);
            ////await RunQuery(sqlQueryText4, containerSubpartitionByTenantId_UserId);

            /*
             * Case where data in the container has a single `TenantId` which is very heavy. 
             * 
             * 1. Partition by tenantId
             * Drawbacks:
             * a. Inserting documents belonging to the same tenant beyond the 20GB limit fails
             * b. Even with 5 physical partitions. All data resides on a single partition. Poor Distribution.
             * 
             * 
             * 2. Partition by Synthetic key
             * Pros:
             * You overcome the above 2 drawbacks with approach.
             * 
             * Drawbacks:
             * a. Querying for a subset of data does a fan out across all partitions leading to high RU usage.
             * b. 
             */
            //var sqlQueryText5 = "SELECT TOP 100 * FROM c where (c.TenantId = 'Microsoft' AND c.UserId = 'Marty_Marks')";
            //await RunQuery(sqlQueryText5, containerPartitionByTenantId);
            //await RunQuery(sqlQueryText5, containerPartitionByTenantId_UserId);
            //await RunQuery(sqlQueryText5, containerSubpartitionByTenantId_UserId);
        }

        private async Task RunQuery(string sqlQueryText, Container containerContext)
        {
            QueryDefinition queryDefinition = new QueryDefinition(sqlQueryText);

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            var requestOptions = new QueryRequestOptions(); //use all default query options

            FeedIterator<dynamic> queryResultSetIterator = containerContext.GetItemQueryIterator<dynamic>(queryDefinition, requestOptions: requestOptions);

            List<dynamic> results = new List<dynamic>();
            double totalRequestCharge = 0;

            Dictionary<string, int> physicalPartitionsQueried = new Dictionary<string, int>();

            while (queryResultSetIterator.HasMoreResults)
            {
                FeedResponse<dynamic> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                totalRequestCharge += currentResultSet.RequestCharge;
                var diagnostics = currentResultSet.Diagnostics.ToString();

                // TODO: this isn't working
                //Console.WriteLine(diagnostics);
                //JObject jObject = JObject.Parse(diagnostics);
                //JToken context = jObject["Context"];
                //var pkRangeIdRaw = context.First["PKRangeId"] ?? context[2]["PKRangeId"];
                //var pkRangeId = pkRangeIdRaw.ToString();

                //if (!physicalPartitionsQueried.ContainsKey(pkRangeId))
                //    physicalPartitionsQueried[pkRangeId] = 0;

                //physicalPartitionsQueried[pkRangeId]++;

                foreach (var item in currentResultSet)
                {
                    results.Add(item);
                }

            }

            Console.WriteLine("Number of results returned: {0}", results.Count);
            stopWatch.Stop();
            TimeSpan ts = stopWatch.Elapsed;

            //Print results
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);

            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nContainer: {0}\n", containerContext.Id);

            Console.ResetColor();
            Console.WriteLine("\tQuery {0} consumed {1} RUs\n", sqlQueryText, totalRequestCharge);
            Console.WriteLine("\tTotal time: {0}\n", elapsedTime);


            foreach (KeyValuePair<string, int> entry in physicalPartitionsQueried)
            {
                Console.WriteLine("\tPhysical partition queried : {0}. Number of hits: {1}", entry.Key, entry.Value);
            }
        }
    }
}
