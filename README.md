# Overview

This demo simulates a workload where transactions are created and queried by Contoso. Contoso has 500 stores worldwide with 10% of stores having very high traffic, 10% of stores having high traffic, and the remaining 80% of stores having moderate traffic.

This application is built to highlight the value of:

- Hierarchial partition keys
- Partition merge
- Burst capacity
- Partition throughput redistribution

## Setup instructions

1. Follow these [instructions](https://docs.microsoft.com/azure/cosmos-db/how-to-manage-database-account) to create a new Azure Cosmos DB account for SQL API.
    - Do not enable multi-region writes or geo-redundancy.
    - Choose a region that is the same as your VM region.

1. Clone the repo into a VM running in Azure.
    - For best performance and low latency, the VM should be in the same region as one of your Azure Cosmos DB account regions.
    - You can use a Windows [Data Science VM](https://azure.microsoft.com/services/virtual-machines/data-science-virtual-machines/) as it has all dependencies installed.
Otherwise, you will need a VM with [.NET Core](https://dotnet.microsoft.com/download) (required) and [Visual Studio 2017 or 2019](https://visualstudio.microsoft.com/downloads/) (optional, if you'd like to inspect or edit code).

1. Open the ```DataGenerator\App.config``` file and fill in the ```Endpoint``` and ```PrimaryKey``` variables with your Cosmos account information along with any other configuration values you would like to change.

1. To start the application:
    - From Visual Studio, select **Ctrl + F5** to start the application.
    - From a command line, navigate to the root of the project (the library with ```DataGenerator.csproj```) and type ```dotnet run```.
