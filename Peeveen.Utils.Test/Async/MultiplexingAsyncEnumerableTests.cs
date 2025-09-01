using FluentAssertions;
using Peeveen.Utils.Async;

namespace Peeveen.Utils.Test.Async;

public class MultiplexingAsyncEnumerableTests {
	[Fact]
	public async Task TestMultiplexingAsyncEnumerable() {
		var consumerCount = 12;
		var maxNumber = 100000;
		var total = 0L;
		var numbersEnumerated = 0;
		var numbers = Enumerable.Range(0, maxNumber)
			.ToAsyncEnumerable()
			.ToMultiplexingAsyncEnumerable(consumerCount);
		var multiplexingEnumerable = numbers;
		var tasks = Enumerable.Range(0, consumerCount).Select(n =>
			Task.Run(async () => {
				await foreach (var number in numbers) {
					Interlocked.Increment(ref numbersEnumerated);
					Interlocked.Add(ref total, number);
				}
			})
		);
		await Task.WhenAll(tasks);
		numbersEnumerated.Should().Be(maxNumber * consumerCount);
		total.Should().Be((maxNumber - 1L) * maxNumber / 2 * consumerCount);
		// Hopefully we didn't use too much memory.
		multiplexingEnumerable.MaxBufferSizeUsed.Should().BeLessThan(maxNumber / 100);
	}
}
