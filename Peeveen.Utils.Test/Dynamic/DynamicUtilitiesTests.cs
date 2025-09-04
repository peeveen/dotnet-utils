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
	}
}