using System;

namespace AutoscaleDemo
{
    class CartOperationEvent
    {
        public string id { get; set; }

        public string CartID { get; set; }

        public string Action { get; set; }

        public string Item { get; set; }

        public string Price { get; set; }

        public string Username { get; set; }

        public string Country { get; set; }

        public string Address { get; set; }

        public string Date { get; set; }

        public DateTime Timestamp { get; set; }
    }

    class CartOperationEventBasic
    {
        public string id { get; set; }

        public string CartID { get; set; }
    }

    class Transaction
    {
        public string id { get; set; }

        public int StoreId { get; set; }

        public string SyntheticKey { get; set; }

        public Decimal TotalAmount { get; set; }

        public string Currency { get; set; }

        public int NumItems { get; set; }

        public string Date { get; set; }

        public DateTime Timestamp { get; set; }
    }

    class SurveyResponse
    {
        public string id { get; set; } // the survey response Id

        public string EmployeeIdHash { get; set; }

        public string QuestionId { get; set; }

        public string QuestionText { get; set; }

        public double ResponseRating { get; set; }

        public string ResponseRatingText { get; set; }

        public string Status { get; set; }

        public string Country { get; set; }

        public string Date { get; set; }

        public string JobTitle { get; set; }

        public string JobDescriptor { get; set; }

        public string JobArea { get; set; }

        public string JobType { get; set; }

        //public string Department { get; set; }

        public DateTime Timestamp { get; set; }


    }

    internal class EmployeeIdHashHelper
    {
        public string EmployeeIdHash { get; set; }
    }
}
