using System.Text.Json;
using FluentAssertions;
using Peeveen.Utils.Dynamic;

namespace Peeveen.Utils.Test.Dynamic {
	public class DynamicUtilitiesTests {
		private static readonly dynamic DynamicTestObject = new {
			Name = "John",
			Age = 30,
			Address = new {
				City = "New York",
				ZipCode = "10001"
			},
			Array = new dynamic[] { 1, 2, new int[]{ 3,4 }, new {
				Thing = 44.6
			} },
			Bool = true,
			X = new {
				Y = "This is Y"
			},
			X_Y = "Conflict Test",
		};

		[Fact]
		public void TestDynamicExpressionEvaluation() {
			((string)DynamicTestObject.Name).Should().Be("John");

			string name = DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Name");
			int age = DynamicUtilities.EvaluateExpression<int>(DynamicTestObject, "Age");
			string city = DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address.City");
			string zipCode = DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address.ZipCode");
			string zipCodeByIndex = DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address[\"ZipCode\"]");
			int arrayItem = DynamicUtilities.EvaluateExpression<int>(DynamicTestObject, "Array[1]");
			double arrayThingByIndex = DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array[3][\"Thing\"]");
			double arrayThing = DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array[3].Thing");
			double arrayThingUsingDotSyntax = DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array.3.Thing");
			bool booleanValue = DynamicUtilities.EvaluateExpression<bool>(DynamicTestObject, "Bool");

			name.Should().Be(DynamicTestObject.Name);
			age.Should().Be(DynamicTestObject.Age);
			city.Should().Be(DynamicTestObject.Address.City);
			zipCode.Should().Be(DynamicTestObject.Address.ZipCode);
			zipCodeByIndex.Should().Be(DynamicTestObject.Address.ZipCode);
			arrayItem.Should().Be(DynamicTestObject.Array[1]);
			booleanValue.Should().Be(DynamicTestObject.Bool);
			arrayThing.Should().Be(DynamicTestObject.Array[3].Thing);
			arrayThingUsingDotSyntax.Should().Be(DynamicTestObject.Array[3].Thing);
			arrayThingByIndex.Should().Be(DynamicTestObject.Array[3].Thing);
		}

		private static void TestFlattening(
			dynamic objectToFlatten,
			ArrayFlatteningBehavior arrayBehavior,
			Dictionary<string, object> expectedResults
		) {
			var flat = DynamicUtilities.Flatten(objectToFlatten, arrayFlatteningBehavior: arrayBehavior);
			foreach (var kvp in expectedResults) {
				var dynamicDictionary = flat as IDictionary<string, object?>;
				dynamicDictionary.Should().NotBeNull();
				dynamicDictionary.ContainsKey(kvp.Key).Should().BeTrue($"Key {kvp.Key} should be present");
				dynamicDictionary[kvp.Key].Should().Be(kvp.Value, $"Value for key {kvp.Key} should match");
			}
		}

		[Fact]
		public void TestFlatteningWithIgnoredArrays() {
			TestFlattening(DynamicTestObject, ArrayFlatteningBehavior.Ignore, new Dictionary<string, object> {
				{ "Name", DynamicTestObject.Name },
				{ "Age", DynamicTestObject.Age },
				{ "Address_City", DynamicTestObject.Address.City },
				{ "Address_ZipCode", DynamicTestObject.Address.ZipCode },
				{ "Array", DynamicTestObject.Array },
				{ "Bool", DynamicTestObject.Bool },
				{ "X_Y", DynamicTestObject.X_Y }
			});
		}
		[Fact]
		public void TestFlatteningWithOmittedArrays() =>
			TestFlattening(DynamicTestObject, ArrayFlatteningBehavior.Omit, new Dictionary<string, object> {
				{ "Name", DynamicTestObject.Name },
				{ "Age", DynamicTestObject.Age },
				{ "Address_City", DynamicTestObject.Address.City },
				{ "Address_ZipCode", DynamicTestObject.Address.ZipCode },
				{ "Bool", DynamicTestObject.Bool },
				{ "X_Y", DynamicTestObject.X_Y }
			});
		[Fact]
		public void TestFlatteningWithJsonifiedArrays() =>
			TestFlattening(DynamicTestObject, ArrayFlatteningBehavior.Jsonify, new Dictionary<string, object> {
				{ "Name", DynamicTestObject.Name },
				{ "Age", DynamicTestObject.Age },
				{ "Address_City", DynamicTestObject.Address.City },
				{ "Address_ZipCode", DynamicTestObject.Address.ZipCode },
				{ "Array", JsonSerializer.Serialize(DynamicTestObject.Array) },
				{ "Bool", DynamicTestObject.Bool },
				{ "X_Y", DynamicTestObject.X_Y }
			});
		[Fact]
		public void TestFlatteningWithFlattenedArrays() =>
			TestFlattening(DynamicTestObject, ArrayFlatteningBehavior.Flatten, new Dictionary<string, object> {
				{ "Name", DynamicTestObject.Name },
				{ "Age", DynamicTestObject.Age },
				{ "Address_City", DynamicTestObject.Address.City },
				{ "Address_ZipCode", DynamicTestObject.Address.ZipCode },
				{ "Array_0", 1 },
				{ "Array_1", 2 },
				{ "Array_2_0", 3 },
				{ "Array_2_1", 4 },
				{ "Array_3_Thing", 44.6 },
				{ "Bool", DynamicTestObject.Bool },
				{ "X_Y", DynamicTestObject.X_Y }
			});

		[Fact]
		public void TestMergingObjects() {
			dynamic obj1 = new {
				Name = "Alice",
				Details = new {
					Age = 25,
					City = "Los Angeles"
				}
			};
			dynamic obj2 = new {
				Name = "Bob", // This should overwrite
				Details = new {
					City = "San Francisco", // This should overwrite
					ZipCode = "94105"
				},
				Occupation = "Engineer"
			};
			var merged = DynamicUtilities.Merge(obj1, obj2);
			((string)merged.Name).Should().Be(obj2.Name);
			((int)merged.Details.Age).Should().Be(obj1.Details.Age);
			((string)merged.Details.City).Should().Be(obj2.Details.City);
			((string)merged.Details.ZipCode).Should().Be(obj2.Details.ZipCode);
			((string)merged.Occupation).Should().Be(obj2.Occupation);

			var mergedAgain = DynamicUtilities.Merge(merged, DynamicTestObject);
			((object)mergedAgain).Should().NotBeNull();
			((string)mergedAgain.Name).Should().Be(DynamicTestObject.Name);
			((int)mergedAgain.Age).Should().Be(DynamicTestObject.Age);
			((string)mergedAgain.Address.City).Should().Be(DynamicTestObject.Address.City);
			((string)mergedAgain.Address.ZipCode).Should().Be(DynamicTestObject.Address.ZipCode);
			((int)mergedAgain.Details.Age).Should().Be(obj1.Details.Age);
			((string)mergedAgain.Details.City).Should().Be(obj2.Details.City);
			((string)mergedAgain.Details.ZipCode).Should().Be(obj2.Details.ZipCode);
			((string)mergedAgain.Occupation).Should().Be(obj2.Occupation);
			((System.Collections.IEnumerable)mergedAgain.Array).Should().Be(DynamicTestObject.Array);
			((bool)mergedAgain.Bool).Should().Be(DynamicTestObject.Bool);
			((string)mergedAgain.X_Y).Should().Be(DynamicTestObject.X_Y);
		}

		[Fact]
		public void TestMergingPrimitives() {
			var merged1 = DynamicUtilities.Merge(24.7, DynamicTestObject);
			((object)merged1).Should().NotBeNull();
			((string)merged1.Name).Should().Be(DynamicTestObject.Name);
			((int)merged1.Age).Should().Be(DynamicTestObject.Age);
			((string)merged1.Address.City).Should().Be(DynamicTestObject.Address.City);
			((string)merged1.Address.ZipCode).Should().Be(DynamicTestObject.Address.ZipCode);
			((bool)merged1.Bool).Should().Be(DynamicTestObject.Bool);
			((System.Collections.IEnumerable)merged1.Array).Should().Be(DynamicTestObject.Array);
			((string)merged1.X_Y).Should().Be(DynamicTestObject.X_Y);

			var helloString = "Hello";
			var merged2 = DynamicUtilities.Merge(DynamicTestObject, helloString);
			((object)merged2).Should().Be(helloString);

			var merged3 = DynamicUtilities.Merge(DynamicTestObject, null);
			((object?)merged3).Should().Be(DynamicTestObject);

			var merged4 = DynamicUtilities.Merge(null, DynamicTestObject);
			((object?)merged4).Should().Be(DynamicTestObject);

			var merged5 = DynamicUtilities.Merge(null, null);
			((object?)merged5).Should().BeNull();

			var merged6 = DynamicUtilities.Merge("a", "b");
			((object?)merged6).Should().Be("b");

			var merged7 = DynamicUtilities.Merge(6678.3, false);
			((object?)merged7).Should().Be(false);
		}
	}
}