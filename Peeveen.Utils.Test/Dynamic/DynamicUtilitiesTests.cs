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

			var name = (string)DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Name");
			var age = (int)DynamicUtilities.EvaluateExpression<int>(DynamicTestObject, "Age");
			var city = (string)DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address.City");
			var zipCode = (string)DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address.ZipCode");
			var zipCodeByIndex = (string)DynamicUtilities.EvaluateExpression<string>(DynamicTestObject, "Address[\"ZipCode\"]");
			var arrayItem = (int)DynamicUtilities.EvaluateExpression<int>(DynamicTestObject, "Array[1]");
			var arrayThingByIndex = (double)DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array[3][\"Thing\"]");
			var arrayThing = (double)DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array[3].Thing");
			var arrayThingUsingDotSyntax = (double)DynamicUtilities.EvaluateExpression<double>(DynamicTestObject, "Array.3.Thing");
			var booleanValue = (bool)DynamicUtilities.EvaluateExpression<bool>(DynamicTestObject, "Bool");

			name.Should().Be("John");
			age.Should().Be(30);
			city.Should().Be("New York");
			zipCode.Should().Be("10001");
			zipCodeByIndex.Should().Be("10001");
			arrayItem.Should().Be(2);
			booleanValue.Should().BeTrue();
			arrayThing.Should().Be(44.6);
			arrayThingUsingDotSyntax.Should().Be(44.6);
			arrayThingByIndex.Should().Be(44.6);
		}

		private static void TestFlattening(
			dynamic objectToFlatten,
			ArrayFlatteningBehavior arrayBehavior,
			Dictionary<string, object> expectedResults
		) {
			var flat = DynamicUtilities.Flatten(objectToFlatten, arrayFlatteningBehavior: arrayBehavior);
			static void ValidateResult(dynamic result, Dictionary<string, object> expected) {
				foreach (var kvp in expected) {
					var dynamicDictionary = result as IDictionary<string, object?>;
					dynamicDictionary.Should().NotBeNull();
					dynamicDictionary.ContainsKey(kvp.Key).Should().BeTrue($"Key {kvp.Key} should be present");
					dynamicDictionary[kvp.Key].Should().Be(kvp.Value, $"Value for key {kvp.Key} should match");
				}
			}
			ValidateResult(flat, expectedResults);
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