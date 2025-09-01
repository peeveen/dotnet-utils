using System.Dynamic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Peeveen.Utils.Dynamic;

namespace Peeveen.Utils.Test.Dynamic;

public class DynamicConverterTests {
	public static async Task<string> ReadTextFile(string filename) {
		return await File.ReadAllTextAsync(Path.Join("..", "..", "..", "json", filename));
	}

	public static async Task<T?> DeserializeJson<T>(string filename, Func<string, T?> deserializeFunc) {
		return deserializeFunc(await ReadTextFile(filename));
	}

	// Class to deserialize, including dynamic data.
	class TestClass {
		[JsonInclude]
		[JsonPropertyName("property1")]
		public string? Property1 { get; set; }
		[JsonInclude]
		[JsonPropertyName("dynamicData")]
		[JsonConverter(typeof(DynamicConverter))]
		public dynamic? DynamicData { get; set; }
		[JsonInclude]
		[JsonPropertyName("property2")]
		public string? Property2 { get; set; }
	}

	private static void AssertArraysMatch<T>(ICollection<T> array1, ICollection<T> array2) {
		var array1Length = array1.Count;
		var array2Length = array2.Count;
		array1Length.Should().Be(array2Length);
		for (int f = 0; f < array1Length; ++f) {
			var element1 = array1.ElementAt(f);
			var element2 = array2.ElementAt(f);
			element1.Should().Be(element2);
		}
	}

	private static void AssertObjectArraysMatch<T>(ICollection<T> array1, ICollection<T> array2) {
		var array1Length = array1.Count;
		var array2Length = array2.Count;
		array1Length.Should().Be(array2Length);
		for (int f = 0; f < array1Length; ++f) {
			var element1 = array1.ElementAt(f);
			var element2 = array2.ElementAt(f);
			if (element1 != null && element2 != null) {
				var props1 = element1.GetType().GetProperties();
				var dictionary = element2 as IDictionary<string, dynamic>;
				var props2 = dictionary?.Keys;
				var propCount1 = props1.Length;
				var propCount2 = props2?.Count;
				propCount1.Should().Be(propCount2);
				for (int g = 0; g < props1.Length; ++g) {
					var prop1 = props1[g];
					var prop2 = props2?.ElementAt(g);
					prop1.Name.Should().Be(prop2);
					var value1 = prop1.GetValue(element1);
					var value2 = dictionary?[prop2!];
					value1.Should().Be(value2);
				}
			}
		}
	}

	private void TestDeserializedObjectData(dynamic result) {
		(result as object).Should().NotBeNull();
		((bool)result.booleanTrueTest).Should().Be(true);
		((bool)result.booleanFalseTest).Should().Be(false);
		((int)result.integerTest).Should().Be(1234);
		((long)result.longTest).Should().Be(int.MaxValue + 1L);
		((float)result.floatTest).Should().Be(123.4f);
		(result.stringTest as string).Should().Be("abcd");
		(result.numericStringTest as string).Should().Be("100");
		(result.nullTest as object).Should().BeNull();
		((DateTime)result.date).Should().Be(new DateTime(2023, 04, 09));
		((DateTime)result.dateTime).Should().Be(new DateTime(2023, 04, 09, 01, 23, 45));
		((DateTimeOffset)result.dateTimeOffset).Should().Be(new DateTime(2023, 04, 09, 00, 23, 45));
		((TimeSpan)result.timespan).Should().Be(new TimeSpan(0, 0, 2, 23, 453, 983));
		var x = Equals(1, result.integerArrayTest[0]);
		AssertArraysMatch(new object[] { 1, 2, 3, 4 }, result.integerArrayTest);
		for (int f = 0; f < result.floatArrayTest.Length; ++f) {
			(result.floatArrayTest[f].GetType() as object).Should().BeOneOf(typeof(float), typeof(double));
			(1.1 * f - (double)result.floatArrayTest[f]).Should().BeLessThanOrEqualTo(0.000001);
		}
		AssertArraysMatch(new object[] { "a", "b", "c", "d" }, result.stringArrayTest);
		AssertArraysMatch(new object[] { true, false, true, false }, result.booleanArrayTest);
		AssertObjectArraysMatch(new[] { new { property = "thing1" }, new { property = "thing2" }, new { property = "thing3" }, new { property = "thing4" } }, result.objectArrayTest);
		AssertArraysMatch(new object[] { 1, 2, 3, 4 }, result.nestedArrayTest[0]);
		for (int f = 0; f < result.nestedArrayTest[1].Length; ++f) {
			(result.nestedArrayTest[1][f].GetType() as object).Should().BeOneOf(typeof(float), typeof(double));
			(1.1 * f - (double)result.nestedArrayTest[1][f]).Should().BeLessThanOrEqualTo(0.000001);
		}
		AssertArraysMatch(new object[] { "a", "b", "c", "d" }, result.nestedArrayTest[2]);
		AssertArraysMatch(new object[] { true, false, true, false }, result.nestedArrayTest[3]);
		AssertObjectArraysMatch(new[] { new { property = "thing1" }, new { property = "thing2" }, new { property = "thing3" }, new { property = "thing4" } }, result.nestedArrayTest[4]);
	}

	public class TestObject(IReadOnlyCollection<string> strings, dynamic dynamicData) {
		public IReadOnlyCollection<string> Strings { get; set; } = strings;
		public dynamic DynamicData { get; set; } = dynamicData;
	}

	[Fact]
	public void TestSerialization() {
		var dynamicData = new { UserName = "PC BIL", Tenant = "BIL Enterprises", Group = "Management", Level = 10 };
		var testObject = new TestObject(["hello", "goodbye"], dynamicData);
		var serializerOptions = new JsonSerializerOptions();
		serializerOptions.Converters.Add(DynamicConverter.Instance);
		var serializedJson = JsonSerializer.Serialize(testObject, serializerOptions);
		var deserializedDescriptor = JsonSerializer.Deserialize<TestObject>(serializedJson, serializerOptions);
		deserializedDescriptor.Should().NotBeNull();
	}

	[Fact]
	public async Task TestDeserialization() {
		var result = await DeserializeJson("test.json", json => JsonSerializer.Deserialize<TestClass>(json));
		result.Should().NotBeNull();
		result.Property1.Should().Be("something");
		result.Property2.Should().Be("somethingElse");
		TestDeserializedObjectData(result.DynamicData);
		TestDeserializedObjectData(result.DynamicData?.objectTest);
		TestDeserializedObjectData(result.DynamicData?.objectTest?.nestedObjectTest);
	}

	[Fact]
	public void TestArraySerialization() {
		var dynamicData = new { UserName = "PC BIL", Tenant = "BIL Enterprises", Group = "Management", Level = 10 };
		var testObject = new dynamic[] { new TestObject(["hello", "goodbye"], dynamicData), dynamicData };
		var serializerOptions = new JsonSerializerOptions();
		serializerOptions.Converters.Add(DynamicConverter.Instance);
		serializerOptions.Converters.Add(DynamicCollectionConverter.Instance);
		var serializedJson = JsonSerializer.Serialize(testObject, serializerOptions);
		var deserializedArray = JsonSerializer.Deserialize<dynamic[]>(serializedJson, serializerOptions);
		deserializedArray.Should().NotBeNull();
		deserializedArray.Length.Should().Be(2);
		(deserializedArray[0].Strings[0] as string).Should().Be("hello");
		(deserializedArray[1].Tenant as string).Should().Be("BIL Enterprises");
	}

	[Fact]
	public async Task TestAsyncEnumerable() {
		var httpResponse = new HttpResponseMessage {
			Content = new StringContent(await ReadTextFile("dynamicArray.json"))
		};
		var serializerOptions = new JsonSerializerOptions();
		serializerOptions.Converters.Add(DynamicConverter.Instance);
		serializerOptions.Converters.Add(DynamicCollectionConverter.Instance);
		var asyncEnumerable = httpResponse.Content.ReadFromJsonAsAsyncEnumerable<dynamic>(serializerOptions);
		// The async enumerable deserialization seems to operate on "chunks" of JSON data at a time,
		// rather than one JSON item at a time. The number of items that it deserializes each time (before
		// going back to the converter to deserialize more) seems dependent on the amount of data/bytes in
		// each item.
		await foreach (var item in asyncEnumerable)
			(item as object).Should().BeOfType<ExpandoObject>();
	}
}
