using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Peeveen.Utils.Benchmark;

[MemoryDiagnoser]
public class Benchmark
{
	private string? _json;
	private readonly JsonSerializerOptions _options = new();

	[GlobalSetup]
	public void GlobalSetup()
	{
		// BenchmarkDotNet creates quite a deep folder in bin
		var jsonPath = Path.Combine("..", "..", "..", "..", "..", "..", "..", "json", "dynamicData.json");
		_json = File.ReadAllText(jsonPath) ?? throw new InvalidOperationException("Failed to read JSON data.");
		_options.Converters.Add(Converter.Instance);
	}

	[Benchmark]
	public void MeasureSpeed() => JsonSerializer.Deserialize<dynamic>(_json!, _options);
}