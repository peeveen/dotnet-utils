using FluentAssertions;
using Peeveen.Utils.Dynamic;

namespace Peeveen.Utils.Test.Dynamic {
	public class DynamicUtilitiesTests {
		[Fact]
		public void TestDynamicExpressionEvaluation() {
			dynamic obj = new {
				Name = "John",
				Age = 30,
				Address = new {
					City = "New York",
					ZipCode = "10001"
				},
				Array = new dynamic[] { 1, 2, new{
					Thing = 44.6
				} },
				Bool = true
			};

			((string)obj.Name).Should().Be("John");

			var name = (string)DynamicUtilities.EvaluateExpression<string>(obj, "Name");
			var age = (int)DynamicUtilities.EvaluateExpression<int>(obj, "Age");
			var city = (string)DynamicUtilities.EvaluateExpression<string>(obj, "Address.City");
			var zipCode = (string)DynamicUtilities.EvaluateExpression<string>(obj, "Address.ZipCode");
			var zipCodeByIndex = (string)DynamicUtilities.EvaluateExpression<string>(obj, "Address[\"ZipCode\"]");
			var arrayItem = (int)DynamicUtilities.EvaluateExpression<int>(obj, "Array[1]");
			var arrayThingByIndex = (double)DynamicUtilities.EvaluateExpression<double>(obj, "Array[2][\"Thing\"]");
			var arrayThing = (double)DynamicUtilities.EvaluateExpression<double>(obj, "Array[2].Thing");
			var booleanValue = (bool)DynamicUtilities.EvaluateExpression<bool>(obj, "Bool");

			name.Should().Be("John");
			age.Should().Be(30);
			city.Should().Be("New York");
			zipCode.Should().Be("10001");
			zipCodeByIndex.Should().Be("10001");
			arrayItem.Should().Be(2);
			booleanValue.Should().BeTrue();
			arrayThing.Should().Be(44.6);
			arrayThingByIndex.Should().Be(44.6);
		}
	}
}