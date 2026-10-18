using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using FlowMatters.Source.Veneer.ExchangeObjects;
using NUnit.Framework;

namespace FlowMatters.Source.Veneer.Tests
{
    [TestFixture]
    public class EvaluateItemTests
    {
        private static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)serializer.ReadObject(stream);
        }

        [Test] public void BindingsByToken_LastDuplicateWinsCaseInsensitively()
        {
            var item = new EvaluateItem
            {
                Bindings = new List<EvaluateBinding>
                {
                    new EvaluateBinding { Token = "$x", Value = 1 },
                    new EvaluateBinding { Token = "$X", Value = 2 },
                }
            };
            var d = item.BindingsByToken();
            Assert.That(d.Count, Is.EqualTo(1));
            Assert.That(d["$x"], Is.EqualTo(2));
            Assert.That(d["$X"], Is.EqualTo(2));
        }

        [Test] public void BindingsByToken_DropsEmptyTokens()
        {
            var item = new EvaluateItem
            {
                Bindings = new List<EvaluateBinding>
                {
                    new EvaluateBinding { Token = "", Value = 1 },
                    new EvaluateBinding { Token = null, Value = 2 },
                    new EvaluateBinding { Token = "$a", Value = 3 },
                }
            };
            var d = item.BindingsByToken();
            Assert.That(d.Count, Is.EqualTo(1));
            Assert.That(d["$a"], Is.EqualTo(3));
        }

        [Test] public void BindingsByToken_NullListIsEmpty()
        {
            var item = new EvaluateItem { Bindings = null };
            Assert.That(item.BindingsByToken(), Is.Empty);
        }

        [Test] public void RequestRoundTripsThroughDataContractJsonSerializer()
        {
            var request = new EvaluateRequest
            {
                Items = new List<EvaluateItem>
                {
                    new EvaluateItem
                    {
                        Id = "id",
                        Date = "2025-11-01",
                        Context = "$F.f_A",
                        Expression = "$x * 2",
                        Bindings = new List<EvaluateBinding>
                        {
                            new EvaluateBinding { Token = "$x", Value = 4.5 },
                            new EvaluateBinding { Token = "$Storages.StorageA.v_Level", Value = -3.25 },
                        }
                    }
                }
            };

            var json = Serialize(request);
            Assert.That(json, Does.Contain("\"bindings\":[{\"token\":\"$x\",\"value\":4.5}"));

            var roundTripped = Deserialize<EvaluateRequest>(json);
            Assert.That(roundTripped.Items[0].BindingsByToken()["$Storages.StorageA.v_Level"], Is.EqualTo(-3.25));
        }

        [Test] public void ResponseOmitsNullValueAndError()
        {
            var response = new EvaluateResponse
            {
                Results = new List<EvaluateResultItem>
                {
                    new EvaluateResultItem { Id = "a", Value = 7 },
                    new EvaluateResultItem { Id = "b", Error = "boom" },
                }
            };

            // DataContractJsonSerializer orders members alphabetically by wire name when no
            // explicit Order is set, so "error" sorts before "id" and "id" before "value".
            var json = Serialize(response);
            Assert.That(json, Does.Contain("{\"id\":\"a\",\"value\":7}"));
            Assert.That(json, Does.Contain("{\"error\":\"boom\",\"id\":\"b\"}"));
            Assert.That(json, Does.Not.Contain("\"error\":null"));
            Assert.That(json, Does.Not.Contain("\"value\":null"));
        }
    }
}
