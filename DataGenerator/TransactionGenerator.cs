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

        public TransactionGenerator()
        {
            var faker = new Faker("en")
            {
                Random = new Randomizer(ITEMSET_RANDOM_SEED)
            };
        }

        internal List<Transaction> GenerateRandomTransactions(int numberOfDocumentsPerBatch)
        {
            var transactionFaker = new Faker<Transaction>()
                .StrictMode(true)
                //Generate event
                .RuleFor(t => t.id, f => Guid.NewGuid().ToString())
                .RuleFor(t => t.TransactionId, (f, m) => $"{m.id}") // same as id
                .RuleFor(t => t.StoreId, f => f.Random.Number(1, NUM_STORES))
                .RuleFor(t => t.StoreIdTransactionIdKey, (f, m) => $"{m.StoreId};{m.id}")
                .RuleFor(p => p.NumItems, f => f.Random.Int(1, 50))
                .RuleFor(t => t.Amount, f => f.Finance.Amount())
                .RuleFor(t => t.Currency, f => f.Finance.Currency().Code)
                .RuleFor(t => t.UserId, f => f.Internet.UserName())
                .RuleFor(t => t.Country, f => f.Address.Country())
                .RuleFor(t => t.Address, f => f.Address.StreetAddress())
                .RuleFor(t => t.Timestamp, f => DateTime.Now)
                .RuleFor(t => t.Date, (f, m) => $"{m.Timestamp.ToString("yyyy-MM-dd")}");

            var events = transactionFaker.Generate(numberOfDocumentsPerBatch, null);

            return events;
        }
    }
}
