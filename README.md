# Overview

This project simulates a workload where transactions are created and written into Contoso's database. Contoso has 500 stores worldwide and wants to simulate two different traffic patterns- one where transactions are evenly distributed across all stores, and another where all transactions are for a single store.

## Setup instructions

1. Follow these [instructions](https://docs.microsoft.com/azure/cosmos-db/how-to-manage-database-account) to create a new Azure Cosmos DB account for SQL API.
    - Do not enable multi-region writes or geo-redundancy.
    - Choose a region that is the same as your VM region.

1. Clone the repo into a VM running in Azure.
    - For best performance and low latency, the VM should be in the same region as one of your Azure Cosmos DB account regions.
    - You can use a Windows [Data Science VM](https://azure.microsoft.com/services/virtual-machines/data-science-virtual-machines/) as it has all dependencies installed.
Otherwise, you will need a VM with [.NET Core](https://dotnet.microsoft.com/download) (required) and [Visual Studio 2017 or 2019](https://visualstudio.microsoft.com/downloads/) (optional, if you'd like to inspect or edit code).

1. Open the ```DataGenerator\App.Config.Sample``` file, rename it to `App.Config`, and fill in the ```Endpoint``` and ```PrimaryKey``` variables with your Cosmos account information.

1. Think about your partitioning strategy. This project uses [hierarchial partition keys](https://devblogs.microsoft.com/cosmosdb/hierarchical-partition-keys-private-preview/) by default. It is partitioned on `/StoreId/TransactionId` which are two separate top-level properties in the transaction document that represent a logical hierarchy in the data. To partition on a single property instead, change the value of `IsHierarchy` to `false` and enter only one path in the `PartitionKey` field. Change any other configuration values as you see fit.

> Note: The default configuration will create a database named `ContosoDemo` with a container named `Transactions` that has an autoscale max limit of 4000 RUs in your Cosmos DB account. Remember to delete this resource when you are done if you want to avoid charges.

1. The application region is set to `EastUS2` by default. Change the `ApplicationRegion` in `Program.cs` on line 44 if you want to use a different region.

1. To start the application:
    - From Visual Studio, select **Ctrl + F5** to start the application.
    - From a command line, navigate to the root of the project (the library with ```DataGenerator.csproj```) and type ```dotnet run```.
