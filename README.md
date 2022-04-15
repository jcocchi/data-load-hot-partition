## Overview
This demo generates a workload with a variable traffic pattern that writes data to Azure Cosmos DB container configured with [autoscale provisioned throughput](https://docs.microsoft.com/azure/cosmos-db/provision-throughput-autoscale). Use this sample to simulate different request volumes over time to show how Azure Cosmos DB is autoscaling the provisioned throughput.

This demo was presented in the Microsoft Build 2020 session [INT125](https://mybuild.microsoft.com/sessions/6776b8ab-7f6a-45f2-afd4-5100d394f3e7?source=sessions) **Building scalable and secure applications with Azure Cosmos DB**.


## Setup instructions

1. Follow these [instructions](https://docs.microsoft.com/azure/cosmos-db/how-to-manage-database-account) to create a new Azure Cosmos DB account for SQL API.
    - Do not enable multi-region writes or geo-redundancy. 
    - Choose a region that is the same as your VM region.

1. Clone the repo into a VM running in Azure. 
    - For best performance and low latency, the VM should be in the same region as one of your Azure Cosmos DB account regions. 
    - You can use a Windows [Data Science VM](https://azure.microsoft.com/services/virtual-machines/data-science-virtual-machines/) as it has all dependencies installed. 
Otherwise, you will need a VM with [.NET Core](https://dotnet.microsoft.com/download) (required) and [Visual Studio 2017 or 2019](https://visualstudio.microsoft.com/downloads/) (optional, if you'd like to inspect or edit code).

1. Open the ```App.config``` file and fill in the ```Endpoint``` and ```PrimaryKey``` variables with your Cosmos account information. If you want to log the data to AppInsights, fill in the ```AppInsightsKey``` variable.

1. To start the application:
    - From Visual Studio, select **Ctrl + F5** to start the application. 
    - From a command line, navigate to the root of the project (the library with ```AutoscaleDemo.csproj```) and type ```dotnet run```.


## How it works

When you run the sample, the program first creates an autoscale container that can scale between 5000 to 50,000 RU/s. The estimated cost in USD is $0.60 to $6.00 an hour, or $438 to $4380 per month, based on usage. Azure Cosmos DB bills you for the highest RU/s the system scaled to within an hour, or the minimum 0.1 * max RU/s if there is no usage. Because this demo is designed to run continuously at high throughput for several hours, it is likely the system will scale to the max RU/s in those hours. As a recommendation, **always clean up your demo resources when not in use.**

**TIP**: Want to run this demo with Azure Cosmos DB free tier? Set the autoscale max RU/s to the minimum of 4000 RU/s in `App.config`, which scales between 400 - 4000 RU/s. It'll cost up to $0.48 USD in the hours where the system scales up, and $0 if there is no usage. 

There are three main components to the program:

|Component  |Description  |Notes  |
|---------|---------|---------|
|LogOutputStats     | Runs every 1 second to output the current RU/s consumed and writes/s achieved in the current second.       |    ---     |
|InsertDocuments     |  Generates and writes data into the Azure Cosmos DB container. | Use the `InsertBasicDocuments` method to write the same `Event.json` document each time. <br><br>Use the `InsertCustomDocuments` method to generate data on the fly with [Bogus](https://github.com/bchavez/Bogus). Note, overall throughput may be lower due to the additional overhead of generating documents on the client-side.   |
|ChangeDelayBetweenOperations     | Changes the delay between inserts on client side, which changes the effective consumed RU/s.| Set the frequency of the change to 8 minutes to generate a history of the RU/s Azure Cosmos DB has scaled to over time, <br><br>To show a variable traffic pattern changing in a live demo, set it to 15 seconds.      |

Based on the number of documents to insert and the autoscale max RU/s of the container defined in `App.config`, the program will spin up a series of `InsertDocuments` tasks to insert batches of documents. Within the task, it waits a preset amount of delay in between writes. While this is running, `LogOutputStats` writes the current throughput achieved each second. `ChangeDelayBetweenOperations` runs every set interval to change the amount of delay between writes, to simulate different traffic patterns.

**TIP**: For best results, run the data generator continuously for a few hours before the demo, with the setting that changes the traffic pattern every 8 minutes. This will ensure there is a history of different RU/s values the system has scaled to over a longer period of time.

## Generate the graph in Azure Monitor
In **Azure Monitor**, navigate to **Metrics**. Select your Azure Cosmos DB account, database, and collection name. To re-create the graph, select the `Autoscale Max Throughput` and `Provisioned Throughput` metric. These metrics are emitted by the system every 5 minutes and represent the highest value within the 5 minute interval. 

![alt text](https://cosmosnotebooksdata.blob.core.windows.net/notebookdata/autoscale-azure_monitor_metrics.png "Azure Monitor timeseries chart that shows autoscale max RU/s of 50,000, and workload whose RU/s scale between 5000 - 50,000 RU/s in unpredictable way.")

## View RU/s consumption on a per-second basis
Enable [**Diagnostic Logs**](https://docs.microsoft.com/azure/cosmos-db/cosmosdb-monitor-resource-logs) in your Azure Cosmos DB account. Navigate to the **Logs** blade and run the query:

```kusto
AzureDiagnostics 
| where TimeGenerated >= ago(1hr) // replace with desired time interval
| where Category == "DataPlaneRequests"
| where databaseName_s  == "Demo" // replace with name of database
| where collectionName_s == "Benchmark" // replace with name of container
| summarize totalRUPerSec = sum(todouble(requestCharge_s)) by bin(TimeGenerated, 1sec) 
```

This will give you a view of how many RU/s were consumed each second. 

## Additional resources
- [Blog post](https://devblogs.microsoft.com/cosmosdb/autoscale-serverless-offers/)
- [Introduction to autoscale](https://aka.ms/cosmos-autoscale-intro)
- [How to use autoscale](https://aka.ms/cosmos-autoscale-how-to)
- [Choosing between autoscale and manual throughput](https://aka.ms/cosmos-throughput-comparison)
- [Autoscale FAQ](aka.ms/cosmos-autoscale-faq)
