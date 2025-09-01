using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Peeveen.Utils.Benchmark;

[MemoryDiagnoser]
public static class Program {
	public static void Main() {
		_ = BenchmarkRunner.Run(typeof(Program).Assembly);
	}
}
