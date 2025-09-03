using FluentAssertions;
using Peeveen.Utils.Async;

namespace Peeveen.Utils.Test.Async;

public class MultiplexingAsyncEnumerableTests {
	private static async Task TestMultiplexingAsyncEnumerable(int testRuns, int consumerCount, int maxNumber, int maxBufferSize = 0) {
		for (var f = 0; f < testRuns; ++f) {
			var total = 0L;
			var numbersEnumerated = 0;
			var numbers = Enumerable.Range(0, maxNumber)
				.ToAsyncEnumerable()
				.ToMultiplexingAsyncEnumerable(consumerCount, maxBufferSize);
			var multiplexingEnumerable = numbers;
			var tasks = Enumerable.Range(0, consumerCount).Select(n =>
				Task.Run(async () => {
					var count = 0;
					var lastNumber = -1;
					await foreach (var number in numbers) {
						++count;
						number.Should().Be(lastNumber + 1);
						lastNumber = number;
						Interlocked.Increment(ref numbersEnumerated);
						Interlocked.Add(ref total, number);
					}
					count.Should().Be(maxNumber);
				})
			);
			await Task.WhenAll(tasks);
			numbersEnumerated.Should().Be(maxNumber * consumerCount);
			total.Should().Be((maxNumber - 1L) * maxNumber / 2 * consumerCount);
			// Hopefully we didn't use too much memory.
			// A bit of a cheat: the maximum buffer size used will always actually be one more than requested
			// due to the "next item" task that is added to the buffer (see GetNextItemAsync for a more
			// detailed explanation).
			// I doubt anyone will sue over that.
			multiplexingEnumerable.MaxBufferSizeUsed.Should().BeLessThanOrEqualTo((maxBufferSize > 1 ? maxBufferSize : maxNumber) + 1);
		}
	}

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithNoMaxBufferLimit() =>
	TestMultiplexingAsyncEnumerable(testRuns: 40, consumerCount: 2, maxNumber: 10000);

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithMaxBufferLimitLessThanConsumerCount() =>
	TestMultiplexingAsyncEnumerable(testRuns: 40, consumerCount: 12, maxNumber: 10000, maxBufferSize: 1);

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithMaxBufferLimitGreaterThanConsumerCount() =>
	TestMultiplexingAsyncEnumerable(testRuns: 40, consumerCount: 2, maxNumber: 10000, maxBufferSize: 10);

	[Fact]
	public Task TestMultiplexingAsyncEnumerableWithMaxBufferLimitEqualToConsumerCount() =>
	TestMultiplexingAsyncEnumerable(testRuns: 40, consumerCount: 12, maxNumber: 10000, maxBufferSize: 12);
}
