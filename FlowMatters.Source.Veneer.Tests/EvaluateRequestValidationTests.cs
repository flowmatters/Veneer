using System;
using System.Collections.Generic;
using System.Linq;
using FlowMatters.Source.Veneer.ExchangeObjects;
using FlowMatters.Source.Veneer.Functions;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class EvaluateRequestValidationTests
    {
        private static EvaluateItem Item(string id, string date = "2025-11-01", string expression = "1") =>
            new EvaluateItem { Id = id, Date = date, Expression = expression };

        private static List<EvaluateRequestValidation.ParsedItem> Parse(EvaluateRequest request, out string problem) =>
            EvaluateRequestValidation.Parse(request, out problem);

        [Test] public void NullOrEmptyItemsYieldAnEmptyList()
        {
            string problem;
            foreach (var request in new[] { null, new EvaluateRequest(), new EvaluateRequest { Items = new List<EvaluateItem>() } })
            {
                var parsed = Parse(request, out problem);
                Assert.That(parsed, Is.Not.Null);
                Assert.That(parsed, Is.Empty);
                Assert.That(problem, Is.Null);
            }
        }

        [Test] public void TooManyItemsIsRejected()
        {
            var request = new EvaluateRequest
            {
                Items = Enumerable.Range(0, EvaluateRequest.MAX_ITEMS + 1).Select(i => Item("i" + i)).ToList()
            };
            string problem;
            Assert.That(Parse(request, out problem), Is.Null);
            Assert.That(problem, Is.EqualTo("too many items: 2001 > 2000"));
        }

        [Test] public void ExactlyTheCapIsAccepted()
        {
            var request = new EvaluateRequest
            {
                Items = Enumerable.Range(0, EvaluateRequest.MAX_ITEMS).Select(i => Item("i" + i)).ToList()
            };
            string problem;
            Assert.That(Parse(request, out problem).Count, Is.EqualTo(EvaluateRequest.MAX_ITEMS));
            Assert.That(problem, Is.Null);
        }

        [Test] public void ANullItemIsRejected()
        {
            var request = new EvaluateRequest { Items = new List<EvaluateItem> { Item("a"), null } };
            string problem;
            Assert.That(Parse(request, out problem), Is.Null);
            Assert.That(problem, Does.Contain("null"));
        }

        [Test] public void AMissingIdIsRejected()
        {
            foreach (var id in new[] { null, "" })
            {
                var request = new EvaluateRequest { Items = new List<EvaluateItem> { Item(id) } };
                string problem;
                Assert.That(Parse(request, out problem), Is.Null);
                Assert.That(problem, Is.EqualTo("every item needs an id and an expression"));
            }
        }

        [Test] public void AMissingExpressionIsRejected()
        {
            foreach (var expression in new[] { null, "" })
            {
                var request = new EvaluateRequest { Items = new List<EvaluateItem> { Item("a", expression: expression) } };
                string problem;
                Assert.That(Parse(request, out problem), Is.Null);
                Assert.That(problem, Is.EqualTo("every item needs an id and an expression"));
            }
        }

        [Test] public void ABadDateIsRejectedNamingTheItem()
        {
            foreach (var date in new[] { "01/11/2025", "2025-11-01T00:00:00", "2025-02-30", "", null })
            {
                var request = new EvaluateRequest { Items = new List<EvaluateItem> { Item("ok"), Item("bad-date", date) } };
                string problem;
                Assert.That(Parse(request, out problem), Is.Null, "date " + (date ?? "null"));
                Assert.That(problem, Does.StartWith("item bad-date: date must be yyyy-MM-dd ("), "date " + (date ?? "null"));
            }
        }

        [Test] public void AValidRequestParsesInOrderWithItsDates()
        {
            var first = Item("a", "2025-11-01", "$x * 2");
            var second = Item("b", "1999-12-31", "2");
            var request = new EvaluateRequest { Items = new List<EvaluateItem> { first, second } };
            string problem;

            var parsed = Parse(request, out problem);

            Assert.That(problem, Is.Null);
            Assert.That(parsed.Count, Is.EqualTo(2));
            Assert.That(parsed[0].Item, Is.SameAs(first));
            Assert.That(parsed[0].Date, Is.EqualTo(new DateTime(2025, 11, 1)));
            Assert.That(parsed[1].Item, Is.SameAs(second));
            Assert.That(parsed[1].Date, Is.EqualTo(new DateTime(1999, 12, 31)));
        }
    }
}
