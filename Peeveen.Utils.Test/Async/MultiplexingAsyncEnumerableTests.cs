using FluentAssertions;
using Peeveen.Utils.Async;

namespace Peeveen.Utils.Test.Async;

public class MultiplexingAsyncEnumerableTests {
	private static async Task TestMultiplexingAsyncEnumerable(int consumerCount, int maxNumber, int maxBufferSize = 0) {
		var total = 0L;
		var numbersEnumerated = 0;
		var numbers = Enumerable.Range(0, maxNumber)
			.ToAsyncEnumerable()
			.ToMultiplexingAsyncEnumerable(consumerCount, maxBufferSize);
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
		multiplexingEnumerable.MaxBufferSizeUsed.Should().BeLessThanOrEqualTo(maxBufferSize > 1 ? maxBufferSize : (maxNumber - 1));
	}

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithoutMaxBufferLimit() =>
		TestMultiplexingAsyncEnumerable(consumerCount: 12, maxNumber: 100000);

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithMaxBufferLimit() =>
		TestMultiplexingAsyncEnumerable(consumerCount: 2, maxNumber: 100, maxBufferSize: 1);
}
