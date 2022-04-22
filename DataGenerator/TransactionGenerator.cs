using System;
using System.Linq;
using System.Collections.Generic;
using Bogus;

namespace DataGenerator
{
    public class TransactionGenerator
    {
        // Arbitrary static random seed so we get same items created each time
        private const int ITEMSET_RANDOM_SEED = 42;
        private const int NUM_STORES = 500;

        private readonly int[] _storeIds = new int[500];
        // Probability each store will show up in the data generated
        private readonly float[] _weights = new float[500];

        public TransactionGenerator()
        {
            var faker = new Faker("en")
            {
                Random = new Randomizer(ITEMSET_RANDOM_SEED)
            };

            for(int i = 0; i < NUM_STORES; i++)
            {
                _storeIds[i] = i + 1;
                //// Artifically increase the weight of the first 50 stores to create hot partitions
                //if (i == 1)
                //{
                //    _weights[i] = 0.25F;
                //}
                //// Artifically increase the weight of the next 50 stores by a smaller amount to create warm partitions
                //else if (i < 100)
                //{
                //    _weights[i] = 0.00015F;
                //}
                // Artifically increase the weight of the first 50 stores to create hot partitions
                if (i < 50)
                {
                    _weights[i] = 0.04F;
                }
                // Artifically increase the weight of the next 50 stores by a smaller amount to create warm partitions
                else if (i < 100)
                {
                    _weights[i] = 0.03F;
                }
                else
                {
                    _weights[i] = 0.0126F;
                }
            }

            //_storeIds = Enumerable.Range(1, NUM_STORES)
            //    .Select((int i) => i)
            //    .ToArray();
            //_weights = Enumerable.Range(1, NUM_STORES)
            //    .Select((int i) => {
            //        // Artifically increase the weight of the first 50 stores to create hot partitions
            //        if (i < 50)
            //        {
            //            return 0.04F;
            //        }
            //        // Artifically increase the weight of the next 50 stores by a smaller amount to create warm partitions
            //        else if (i < 100)
            //        {
            //            return 0.03F;
            //        }
            //        else
            //        {
            //            return 0.0126F;
            //        }
            //    })
            //    .ToArray();
        }

        internal List<Transaction> GenerateRandomTransactions(int numberOfDocumentsPerBatch)
        {
            var transactionFaker = new Faker<Transaction>()
                .StrictMode(true)
                //Generate event
                .RuleFor(t => t.id, f => Guid.NewGuid().ToString())
                .RuleFor(t => t.TransactionId, (f, m) => $"{m.id}") // same as id
                .RuleFor(t => t.StoreId, 1) //f => f.Random.WeightedRandom<int>(this._storeIds, this._weights))
                .RuleFor(t => t.StoreIdTransactionIdKey, (f, m) => $"{m.StoreId};{m.id}")
                .RuleFor(p => p.NumItems, f => f.Random.Int(1, 50))
                .RuleFor(t => t.Amount, f => f.Finance.Amount())
                .RuleFor(t => t.Currency, f => f.Finance.Currency().Code)
                .RuleFor(t => t.UserId, f => f.Internet.UserName())
                .RuleFor(t => t.Country, f => f.Address.Country())
                .RuleFor(t => t.Address, f => f.Address.StreetAddress())
                .RuleFor(t => t.Timestamp, f => DateTime.Now) // just today's date
                .RuleFor(t => t.Date, (f, m) => $"{m.Timestamp.ToString("yyyy-MM-dd")}");

            var events = transactionFaker.Generate(numberOfDocumentsPerBatch, null);

            return events;
        }
    }
}
