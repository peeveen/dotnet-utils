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

## `MultiplexingAsyncEnumerable`

A wrapper around an `IAsyncEnumerable` to allow for it to be consumed by multiple consumers, _as long
as you know how many consumers there are_.

Depending on the implementation of an `IAsyncEnumerable`, it may not support being enumerated multiple
times (e.g. if it is reading directly from a network stream, or a database query). So if you want to
provide the data to multiple consumers, your usual option is to first enumerate it to a concrete
collection such as a `List`. This could be costly in terms of memory.

This class uses a internal `List` buffer into which the enumerated elements are read, _but_:

- An item is only added to the buffer when it is requested by a consumer that has already consumed
  all previously-buffered items.
- Later consumers will received the buffered item(s).
- Items are removed from the buffer once all consumers have consumed them.
- You can specify a maximum buffer limit to ensure that one consumer does not race ahead, filling
  the internal buffer with massive amounts of data and allocating large amounts of memory.
