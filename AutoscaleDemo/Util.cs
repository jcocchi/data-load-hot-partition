using Bogus;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoscaleDemo
{
    class Util
    {
        static internal List<CartOperationEvent> GenerateRandomCartOperationEvents(int numberOfDocumentsPerBatch)
        {
            var actions = new[] { "viewed", "addedToCart", "purchased" };

            // have only 10k posts
            var faker = new Faker();

            var operationEvent = new Faker<CartOperationEvent>()

                //Ensure all properties have rules. 
                .StrictMode(true)

                .RuleFor(o => o.id, f => Guid.NewGuid().ToString())

                //Generate event
                .RuleFor(o => o.CartID, f => Guid.NewGuid().ToString()) //TODO: Make it an integer value
                .RuleFor(o => o.Action, f => f.PickRandom(actions))

                .RuleFor(o => o.Item, f => f.Commerce.ProductName())
                .RuleFor(o => o.Price, f => f.Commerce.Price())
                .RuleFor(o => o.Username, f => f.Internet.UserName())
                .RuleFor(o => o.Country, f => f.Address.Country())
                //.RuleFor(o => o.timestamp, f => f.Date.Between(DateTime.Now, DateTime.Now.AddDays(-1))) //3 days ago
                .RuleFor(o => o.Address, f => f.Address.StreetAddress())
                .RuleFor(o => o.Timestamp, f => DateTime.Now) // just today's date
                .RuleFor(o => o.Date, (f, m) => $"{m.Timestamp.ToString("yyyy-MM-dd")}");

            var events = operationEvent.Generate(numberOfDocumentsPerBatch);

            return events;

        }

        static internal List<Transaction> GenerateRandomPaymentEvent(int numberOfDocumentsPerBatch)
        {
            var faker = new Faker();

            var storeIds = new int[50];
            var weights = new float[50];
            for (var i = 0; i < storeIds.Length; i++)
            {
                storeIds[i] = i;

                // Artifically increase the weight of the first 10 stores to create a hot partition
                if (i < 5)
                {
                    weights[i] = 0.04F;
                }
                else if (i < 10)
                {
                    weights[i] = 0.03F;
                }
                else
                {
                    weights[i] = 0.0126F;
                }
            }

            var paymentEvent = new Faker<Transaction>()
                .StrictMode(true)
                //Generate event
                .RuleFor(o => o.id, f => Guid.NewGuid().ToString())
                .RuleFor(o => o.StoreId, f => f.Random.WeightedRandom<int>(storeIds, weights))
                .RuleFor(o => o.SyntheticKey, (f, m) => $"{m.StoreId};{m.id}")
                .RuleFor(o => o.TotalAmount, f => f.Finance.Amount())
                .RuleFor(o => o.Currency, f => f.Finance.Currency().Code)
                .RuleFor(o => o.NumItems, f => f.Random.Int(1, 50))
                .RuleFor(o => o.Timestamp, f => DateTime.Now)
                .RuleFor(o => o.Date, (f, m) => $"{m.Timestamp.ToString("yyyy-MM-dd")}");

            var events = paymentEvent.Generate(numberOfDocumentsPerBatch);

            return events;

        }


        static internal List<string> GenerateRandomEmployeeIdHash(int numberOfDocumentsPerBatch)
        {
            var faker = new Faker("en")
            {
                Random = new Randomizer(1338)
            };

            var employeeIdHashes = Enumerable.Range(1, numberOfDocumentsPerBatch)
                                  .Select(_ => faker.Random.Guid().ToString())
                                  .ToList();

            return employeeIdHashes;
        }

        static internal List<SurveyResponse> GenerateRandomSurveyResponse(int numberOfDocumentsPerBatch)
        {
            var faker = new Faker("en")
            {
                Random = new Randomizer(1338)
            };
            var tenants = new[] { "Contoso", "Fabrikam", "Microsoft" };

            var numberOfEmployees = 100;
            var employeeIdHashes = GenerateRandomEmployeeIdHash(numberOfEmployees);

            var countries = new[] { "United States", "Canada", "Mexico" };


            var questionIds = new[] { "1", "2" };

            var questionIdTextMapping = new Dictionary<string, string>()
            {
                { "1", "How connected do you feel to your team?" },
                { "2", "How are you feeling?" }
                //{ "3", "QuestionText3" },
                //{ "4", "QuestionText4" },
                //{ "5", "QuestionText5" },
            };

            var questionIdResponseMapping = new Dictionary<string, Dictionary<double, string>>();

            questionIdResponseMapping.Add("1", new Dictionary<double, string>() { { 1.0, "No Signal" },
                                                                               { 2.0, "1 bar" },
                                                                               { 3.0, "In Range" },
                                                                               { 4.0, "Steady signal" },
                                                                               { 5.0, "Great coverage" } });

            questionIdResponseMapping.Add("2", new Dictionary<double, string>() { { 1.0, "Lost" },
                                                                               { 2.0, "Worse than usual" },
                                                                               { 3.0, "Pretty average" },
                                                                               { 4.0, "Better than usual" },
                                                                               { 5.0, "Awesome!" } });

            var weights = new float[] { 0.05f, 0.05f, 0.1f, 0.4f, 0.4f };
            var surveyResponse = new Faker<SurveyResponse>()
                .StrictMode(true)
                //Generate event
                .RuleFor(o => o.id, f => Guid.NewGuid().ToString())
                .RuleFor(o => o.EmployeeIdHash, f => f.PickRandom(employeeIdHashes))
                .RuleFor(o => o.QuestionId, f => f.PickRandom(questionIds))
                .RuleFor(o => o.QuestionText, (f, m) => questionIdTextMapping[m.QuestionId])
                .RuleFor(o => o.ResponseRating, f => f.Random.WeightedRandom(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, weights))
                .RuleFor(o => o.ResponseRatingText, (f, m) => questionIdResponseMapping[m.QuestionId][m.ResponseRating])
                .RuleFor(o => o.Status, f => "complete")
                .RuleFor(o => o.Country, f => f.PickRandom(countries))
                .RuleFor(o => o.Timestamp, f => f.Date.Between(new DateTime(2020, 04, 01), new DateTime(2021, 05, 24)))
                .RuleFor(o => o.Date, (f, m) => $"{m.Timestamp.ToString("yyyy-MM-dd")}")
                // Testing job title stuff
                .RuleFor(o => o.JobTitle, f => f.Name.JobTitle())
                .RuleFor(o => o.JobDescriptor, f => f.Name.JobDescriptor())
                .RuleFor(o => o.JobArea, f => f.Name.JobArea())
                .RuleFor(o => o.JobType, f => f.Name.JobType());

        var surveyResponses = surveyResponse.Generate(numberOfDocumentsPerBatch);
            return surveyResponses;
        }
    }


}

