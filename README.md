# Peeveen.Utils

Some handy classes that I like to use.

## `DynamicConverter` / `DynamicCollectionConverter`

Implementations of `System.Text.Json.Serialization.JsonConverter<T>` for the `dynamic` and `dynamic[]` types.

```csharp
class MyClass {
	...

	[JsonInclude]
	[System.Text.Json.Serialization.JsonConverter(typeof(Peeveen.Utils.Dynamic.DynamicConverter))]
	public dynamic MyDynamicData { get; set; }

	[JsonInclude]
	[System.Text.Json.Serialization.JsonConverter(typeof(Peeveen.Utils.Dynamic.DynamicCollectionConverter))]
	public dynamic[] MyDynamicDataArray { get; set; }

	...
}

var result = JsonSerializer.Deserialize<MyClass>(json);
var val = result.MyDynamicData.some._dynamic.property.somewhere;
```

Alternatively, the more direct approach ...

```csharp
var serializerOptions = new JsonSerializerOptions();
serializerOptions.Converters.Add(Peeveen.Utils.Dynamic.DynamicConverter.Instance); // or new() ...
serializerOptions.Converters.Add(Peeveen.Utils.Dynamic.DynamicCollectionConverter.Instance); // or new() ...
var deserializedDynamicData = JsonSerializer.Deserialize<dynamic>(rawJsonString, serializerOptions);
var dynamicProperty = deserializedDynamicData.some._dynamic.property.somewhere;
```

### TODO

- Parameterized constructor to define behavior surrounding date detection and smaller numeric data types.

## `DynamicUtilities`

A static class with a few methods for working with `dynamic` data.

- `EvaluateExpression()` allows for a string expression like `x.y.z[2].blah` to be evaluated against
  a `dynamic` object.
- `GetPropertyInfo()` will return an array of objects describing the top-level properties of a `dynamic`
  object.
- `Flatten()` will flatten all nested objects of a `dynamic` into top-level properties (optionally
  including arrays/lists), with property names made from a combination of parent property names using
  a custom separator.

## `MultiplexingAsyncEnumerable`

A wrapper around an `IAsyncEnumerable` to allow for it to be consumed by multiple consumers, _as long
as you know how many consumers there are_.

Depending on the implementation of any given `IAsyncEnumerable`, it may not support being enumerated
multiple times (e.g. if it is reading directly from a network stream, or a database query). So if you
want to provide the data to multiple consumers, your usual option is to first enumerate it to a
concrete collection such as a `List`. This could be costly in terms of memory if there is a very
large amount of data being enumerated.

This class uses a internal `List` buffer into which the enumerated elements are read, _but_:

- An item is only added to the buffer when it is requested by a consumer that has already consumed
  all previously-buffered items.
- Later consumers will received the buffered item(s).
- Items are removed from the buffer as soon as all consumers have consumed them.
- You can specify a maximum buffer limit to ensure that one consumer does not race ahead, filling
  the internal buffer with massive amounts of data and allocating large amounts of memory.
