using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
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

        [Test] public void BindingsByToken_SkipsNullElementKeepsOthers()
        {
            // A "bindings":[null] body deserialises to a list containing a null element.
            var item = new EvaluateItem
            {
                Bindings = new List<EvaluateBinding>
                {
                    null,
                    new EvaluateBinding { Token = "$a", Value = 3 },
                }
            };
            var d = item.BindingsByToken();
            Assert.That(d.Count, Is.EqualTo(1));
            Assert.That(d["$a"], Is.EqualTo(3));
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
            AddonAssert.Contains(json, "\"bindings\":[{\"token\":\"$x\",\"value\":4.5}");

            var roundTripped = Deserialize<EvaluateRequest>(json);
            Assert.That(roundTripped.Items[0].BindingsByToken()["$Storages.StorageA.v_Level"], Is.EqualTo(-3.25));
        }

        [Test] public void DeserializingAMissingBindingValueThrows()
        {
            var json = "{\"items\":[{\"id\":\"i1\",\"date\":\"2025-11-01\",\"expression\":\"$x\","
                      + "\"bindings\":[{\"token\":\"$x\"}]}]}";
            TestDelegate act = () => Deserialize<EvaluateRequest>(json);
            Assert.Throws<SerializationException>(act);
        }

        [Test] public void DeserializesHandWrittenJsonWithOutOfOrderFieldsAndNoBindings()
        {
            var json = "{\"items\":["
                      + "{\"id\":\"i1\",\"date\":\"2025-11-01\",\"expression\":\"$x*2\","
                      + "\"bindings\":[ {\"value\":4.5,\"token\":\"$x\"} ]},"
                      + "{\"id\":\"i2\",\"date\":\"2025-11-01\",\"expression\":\"1\"}"
                      + "]}";
            var request = Deserialize<EvaluateRequest>(json);
            Assert.That(request.Items[0].BindingsByToken()["$x"], Is.EqualTo(4.5));
            Assert.That(request.Items[1].Bindings, Is.Null);
            Assert.That(request.Items[1].BindingsByToken(), Is.Empty);
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
            AddonAssert.Contains(json, "{\"id\":\"a\",\"value\":7}");
            AddonAssert.Contains(json, "{\"error\":\"boom\",\"id\":\"b\"}");
            Assert.That(json.Contains("\"error\":null"), Is.False, json);
            Assert.That(json.Contains("\"value\":null"), Is.False, json);
        }

        [Test] public void ResponseEmitsAZeroValue()
        {
            // Pins the double?-zero-emission trap: a plain `double` with EmitDefaultValue=false
            // would drop a legitimate 0 just like it drops the default; `double?` does not.
            var response = new EvaluateResponse
            {
                Results = new List<EvaluateResultItem>
                {
                    new EvaluateResultItem { Id = "z", Value = 0 },
                }
            };
            var json = Serialize(response);
            AddonAssert.Contains(json, "{\"id\":\"z\",\"value\":0}");
        }
    

        [Test] public void BindingError_NonDollarToken()
        {
            var error = EvaluateItem.BindingError(new Dictionary<string, double> { { "$a", 1 }, { "b", 2 } });
            Assert.That(error, Is.EqualTo("binding token must start with '$': b"));
        }

        [Test] public void BindingError_NaN()
        {
            var error = EvaluateItem.BindingError(new Dictionary<string, double> { { "$a", double.NaN } });
            Assert.That(error, Is.EqualTo("binding $a: non-finite value"));
        }

        [Test] public void BindingError_PositiveInfinity()
        {
            var error = EvaluateItem.BindingError(new Dictionary<string, double> { { "$a", double.PositiveInfinity } });
            Assert.That(error, Is.EqualTo("binding $a: non-finite value"));
        }

        [Test] public void BindingError_ValidIsNull()
        {
            Assert.That(EvaluateItem.BindingError(new Dictionary<string, double> { { "$a", -0.0 }, { "$B", 1e300 } }), Is.Null);
            Assert.That(EvaluateItem.BindingError(new Dictionary<string, double>()), Is.Null);
            Assert.That(EvaluateItem.BindingError(null), Is.Null);
        }
    }
}
