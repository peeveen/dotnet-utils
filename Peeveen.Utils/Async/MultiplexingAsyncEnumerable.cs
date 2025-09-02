using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace Peeveen.Utils.Async {
	/// <summary>
	/// An IAsyncEnumerable implementation that multiplexes the source enumerable across multiple consumers
	/// by caching the data in a buffer when the first consumer requests it, and disposing of cached data
	/// once all consumers have consumed it.
	/// The number of consumers MUST be supplied during construction. The implementation knows how many
	/// consumers are active and can efficiently manage the flow of data to each consumer.
	/// If you attempt to enumerate this enumerable more times than the number of consumers, an exception
	/// will be thrown.
	/// </summary>
	/// <typeparam name="T">Type of data being enumerated.</typeparam>
	public class MultiplexingAsyncEnumerable<T> : IAsyncEnumerable<T> {
		private readonly int _consumerCount;
		private readonly int _maxBufferSize;
		private readonly IAsyncEnumerable<T> _source;
		internal PersistingEnumerator<T> _persistingEnumerator;
		private readonly object _enumeratorLock = new object();
		private int _consumerIndex = -1;

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="source">Source enumerable to wrap.</param>
		/// <param name="consumerCount">Number of consumers.</param>
		/// <param name="maxBufferSize">Maximum buffer size. If less than 1, buffer size will be limitless.</param>
		internal MultiplexingAsyncEnumerable(IAsyncEnumerable<T> source, int consumerCount, int maxBufferSize = 0) {
			_consumerCount = consumerCount;
			_source = source;
			_maxBufferSize = maxBufferSize;
		}

		/// <summary>
		/// How many items have been enumerated? Mainly used for diagnostics.
		/// </summary>
		public int ItemsEnumerated => _persistingEnumerator?._itemsEnumerated ?? 0;

		/// <summary>
		/// What is the maximum buffer size so far? Mainly used for diagnostics.
		/// </summary>
		public int MaxBufferSizeUsed => _persistingEnumerator?._maxBufferSizeUsed ?? 0;

		/// <inheritdoc/>
		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
			var consumerIndex = Interlocked.Increment(ref _consumerIndex);
			if (_consumerIndex >= _consumerCount)
				throw new InvalidOperationException($"This {nameof(MultiplexingAsyncEnumerable<T>)} supports only {_consumerCount} enumerations, but {nameof(GetAsyncEnumerator)} has now been called {_consumerIndex + 1} times.");
			lock (_enumeratorLock) {
				if (_persistingEnumerator == null)
					_persistingEnumerator = new PersistingEnumerator<T>(_source.GetAsyncEnumerator(cancellationToken), _consumerCount, _maxBufferSize);
				// Probably overkill to use Interlocked here, but ...
				Interlocked.Increment(ref _persistingEnumerator._usages);
			}
			return new MultiplexingAsyncEnumerator<T>(_persistingEnumerator, consumerIndex);
		}
	}

	internal class PersistingEnumerator<T> {
		// The wrapped enumerator.
		private readonly IAsyncEnumerator<T> _source;
		// The buffer containing items obtained from the wrapped enumerator.
		private readonly List<Task<(bool, T)>> _buffer = new List<Task<(bool, T)>>();
		private readonly AsyncSemaphore _bufferSemaphore;
		// We track the position of each consumer in this array.
		private readonly int[] _consumerIndices;
		// We track the current item for each consumer in this array.
		private readonly T[] _currents;
		// The buffer will have items removed from the "tail" once all consumers have
		// consumed them. So we need to keep track of the actual "start index" of the buffer.
		private int _bufferStartIndex;
		// This will be true while there is (possibly) more data in the wrapped IAsyncEnumerator.
		private bool _hasMoreData = true;
		// Counted for the maximum buffer size that was used.
		internal int _maxBufferSizeUsed;
		// Counter for items enumerated.
		internal int _itemsEnumerated;
		// This will be true if there is only one consumer.
		private readonly bool _singleConsumer;
		// Lock for synchronizing access to the buffer.
		private readonly AsyncLock _bufferLock = new AsyncLock();
		// Number of times that this enumerator has been used.
		// It is shared across multiple instances of MultiplexingAsyncEnumerator, so when
		// Dispose() is called, it should NOT dispose until there are no active usages.
		internal int _usages;

		internal PersistingEnumerator(IAsyncEnumerator<T> source, int consumerCount, int maxBufferSize) {
			if (consumerCount < 1)
				throw new ArgumentException("There must be at least one consumer.", nameof(consumerCount));
			_bufferSemaphore = maxBufferSize > 0 ? new AsyncSemaphore(maxBufferSize) : null;
			_singleConsumer = consumerCount == 1;
			_source = source;

			// Initialize the arrays.
			_consumerIndices = new int[consumerCount];
			_currents = new T[consumerCount];
			// All consumers start at an index of -1.
			// Their first call to MoveNextAsync will advance them to position 0 in the buffer.
			for (var i = 0; i < consumerCount; i++)
				_consumerIndices[i] = -1;
		}

		// If there is only one consumer, there is no need for any of our fancy-schmancy stuff.
		public T GetCurrent(int consumerNumber) => _singleConsumer ? _source.Current : _currents[consumerNumber];

		public async ValueTask DisposeAsync() {
			// Only dispose once the last enumerator says so.
			if (Interlocked.Decrement(ref _usages) == 0)
				await _source.DisposeAsync();
		}

		private async Task<(bool, T)> GetNextItemAsync() {
			if (_bufferSemaphore != null)
				await _bufferSemaphore.WaitAsync();
			// Okay, we got it. Now, as with all buffer access, enter the lock.
			// OK, we DEFINITELY need to add to the buffer.
			// Move the wrapped enumerator to the next index.
			if (_hasMoreData = _hasMoreData && await _source.MoveNextAsync()) {
				// There IS more data in the wrapped enumerator.
				// So grab it, and add it to our buffer.
				// Note that we DON'T release the semaphore here, as we ARE adding
				// to the buffer, so it is CORRECT that the semaphore count is reduced.
				Interlocked.Increment(ref _itemsEnumerated);
				return (true, _source.Current);
			}
			return (false, default);
		}

		public async ValueTask<bool> MoveNextAsync(int consumerNumber) {
			// If there is only one consumer, there is no need for any of our fancy-schmancy stuff.
			if (_singleConsumer)
				return await _source.MoveNextAsync();
			var consumerIndex = ++_consumerIndices[consumerNumber];
			// Any buffer access (including examining length, etc) should be done
			// within this lock.
			// Note that _bufferStartIndex is a value that can change when the
			// buffer is being modified, so we should treat that with the same
			// reverence.
			bool enoughData;
			Task<(bool, T)> resultTask;
			using (await _bufferLock.LockAsync()) {
				// Check how far each consumer has gone.
				// If they're all past the start of the buffer, we can
				// remove items from the start.
				var minIndex = Math.Max(_consumerIndices.Min(), 0);
				var itemsToRemove = minIndex - _bufferStartIndex;
				_bufferStartIndex = minIndex;
				_buffer.RemoveRange(0, itemsToRemove);
				_bufferSemaphore?.Release(itemsToRemove);

				// Figure out the actual buffer index that we want to access.
				var bufferIndex = consumerIndex - _bufferStartIndex;
				// Do we need to add more data to the buffer?
				// Or do we already have enough?
				// Don't add if another consumer has already started adding.
				enoughData = bufferIndex < _buffer.Count;
				if (!enoughData) {
					_buffer.Add(GetNextItemAsync());
					_maxBufferSizeUsed = Math.Max(_maxBufferSizeUsed, _buffer.Count);
				}
				resultTask = _buffer[bufferIndex];
			}
			var result = await resultTask;
			_currents[consumerNumber] = result.Item2;
			// The final result will be true if there is new data available in _currents,
			// or false if the data has been exhausted.
			return result.Item1;
		}
	}

	/// <summary>
	/// An IAsyncEnumerator implementation that multiplexes the source enumerator across multiple consumers.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	internal class MultiplexingAsyncEnumerator<T> : IAsyncEnumerator<T> {
		private readonly int _consumerIndex;
		private readonly PersistingEnumerator<T> _persistingEnumerator;

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="persistingEnumerator"></param>
		/// <param name="consumerIndex"></param>
		internal MultiplexingAsyncEnumerator(PersistingEnumerator<T> persistingEnumerator, int consumerIndex) {
			_consumerIndex = consumerIndex;
			_persistingEnumerator = persistingEnumerator;
		}

		public T Current => _persistingEnumerator.GetCurrent(_consumerIndex);
		public ValueTask DisposeAsync() => _persistingEnumerator.DisposeAsync();
		public ValueTask<bool> MoveNextAsync() => _persistingEnumerator.MoveNextAsync(_consumerIndex);
	}

	/// <summary>
	/// Extension methods for <see cref="MultiplexingAsyncEnumerable{T}"/>.
	/// </summary>
	public static class MultiplexingAsyncEnumerableExtensions {
		/// <summary>
		/// Converts an IAsyncEnumerable to a multiplexing enumerable.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="source">Source enumerable to wrap.</param>
		/// <param name="consumerCount">Number of consumers.</param>
		/// <param name="maxBufferSize">Maximum buffer size. If less than 1, buffer size will be limitless.</param>
		/// <returns></returns>
		public static MultiplexingAsyncEnumerable<T> ToMultiplexingAsyncEnumerable<T>(this IAsyncEnumerable<T> source, int consumerCount, int maxBufferSize = 0) =>
			new MultiplexingAsyncEnumerable<T>(source, consumerCount, maxBufferSize);
	}
}