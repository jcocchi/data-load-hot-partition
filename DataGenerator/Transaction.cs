using System;

namespace DataGenerator
{
    class Transaction
    {
        public string id { get; set; }

        public string TransactionId { get; set; }

        public int StoreId { get; set; }

        public string StoreIdTransactionIdKey { get; set; }

        public int NumItems { get; set; }

        public decimal Amount { get; set; }

        public string Currency { get; set; }

        public string UserId { get; set; }

        public string Country { get; set; }

        public string Address { get; set; }

        public string Date { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
