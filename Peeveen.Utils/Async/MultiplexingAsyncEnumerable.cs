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
		private int _enumerations;

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
			var thisEnumeration = _enumerations++;
			if (_enumerations > _consumerCount)
				throw new InvalidOperationException($"This {nameof(MultiplexingAsyncEnumerable<T>)} supports only {_consumerCount} enumerations, but {nameof(GetAsyncEnumerator)} has now been called {_enumerations} times.");
			lock (_enumeratorLock) {
				if (_persistingEnumerator == null)
					_persistingEnumerator = new PersistingEnumerator<T>(_source.GetAsyncEnumerator(cancellationToken), _consumerCount, _maxBufferSize);
			}
			return new MultiplexingAsyncEnumerator<T>(_persistingEnumerator, thisEnumeration);
		}
	}

	internal class PersistingEnumerator<T> {
		// The wrapped enumerator.
		private readonly IAsyncEnumerator<T> _source;
		// The buffer containing items obtained from the wrapped enumerator.
		private readonly List<T> _buffer = new List<T>();
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
		// Semaphore for controlling "add" access to the buffer.
		// This is used to limit the number of items that can be added to the buffer.
		private readonly AsyncSemaphore _bufferSemaphore;
		// Lock for synchronizing access to the buffer.
		private readonly AsyncLock _bufferLock = new AsyncLock();

		internal PersistingEnumerator(IAsyncEnumerator<T> source, int consumerCount, int maxBufferSize) {
			if (consumerCount < 1)
				throw new ArgumentException("There must be at least one consumer.", nameof(consumerCount));
			// Some explanation required here.
			// The buffer semaphore is used to implement the maxBufferSize functionality.
			// We don't want to allow more than maxBufferSize items to be buffered at once.
			// So you would think that using maxBufferSize as the constructor value for the semaphore
			// would make sense.
			// However, this would cause deadlock if there were more consumers than the buffer size,
			// and all consumers tried to acquire the semaphore at the same time.
			// In the MoveNextAsync function (in PersistingEnumerator), once the semaphore has been
			// acquired, we perform a quick initial check to see if another consumer added to the buffer
			// while the active consumer was waiting for the semaphore (and the buffer lock). If this
			// has happened, the active consumer simply takes the added value from the buffer and
			// immediately releases the acquired semaphore.
			// So the additional (consumerCount - maxBufferSize) value is added to the semaphore count
			// to allow for the above scenario to play out (and if there are fewer consumers than the max
			// buffer size, we use zero).
			// Even though the semaphore allows access for more than maxBufferSize consumers, the
			// additional buffer lock and checking-logic (described above) ensures that no more than
			// maxBufferSize items are in the buffer at any time.
			var clamourBuffer = Math.Max(0, consumerCount - maxBufferSize);
			_bufferSemaphore = maxBufferSize > 1 ? new AsyncSemaphore(maxBufferSize + clamourBuffer) : null;

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

		public ValueTask DisposeAsync() => _source.DisposeAsync();

		public async ValueTask<bool> MoveNextAsync(int consumerNumber) {
			// If there is only one consumer, there is no need for any of our fancy-schmancy stuff.
			if (_singleConsumer)
				return await _source.MoveNextAsync();
			bool addToBuffer;
			int newConsumerIndex = ++_consumerIndices[consumerNumber];
			// Any buffer access (including examining length, etc) should be done
			// within this lock.
			// Note that _bufferStartIndex is a value that can change when the
			// buffer is being modified, so we should treat that with the same
			// reverence.
			using (await _bufferLock.LockAsync()) {
				// Figure out the actual buffer index that we want to access.
				var bufferIndex = newConsumerIndex - _bufferStartIndex;
				// Do we need to add more data to the buffer?
				// Or do we already have enough?
				addToBuffer = bufferIndex >= _buffer.Count;
				// If there is enough data in the buffer, then great! Job done.
				if (!addToBuffer)
					_currents[consumerNumber] = _buffer[bufferIndex];
			}
			var result = !addToBuffer;
			if (addToBuffer) {
				// Okay, we need to add to the buffer.
				// Grab the "add to buffer" semaphore.
				if (_bufferSemaphore != null)
					await _bufferSemaphore.WaitAsync();
				// Okay, we got it. Now, as with all buffer access, enter the lock.
				using (await _bufferLock.LockAsync()) {
					// By the time we have got the semaphore and the buffer lock, another
					// consumer might have made it into this section and added an item to
					// the buffer. So let's recalculate buffer index in case start index has changed.
					var recalculatedConsumerIndex = newConsumerIndex - _bufferStartIndex;
					// Do we now have enough data in the buffer?
					if (result = recalculatedConsumerIndex < _buffer.Count) {
						// Yes we do! Job done.
						_currents[consumerNumber] = _buffer[recalculatedConsumerIndex];
						// Be sure to release the semaphore we acquired on the way in.
						// It turned out that we didn't need to add to the buffer, so we
						// acquired it "in error".
						_bufferSemaphore?.Release();
					} else {
						// OK, we DEFINITELY need to add to the buffer.
						// Move the wrapped enumerator to the next index.
						if (result = _hasMoreData = _hasMoreData && await _source.MoveNextAsync()) {
							// There IS more data in the wrapped enumerator.
							// So grab it, and add it to our buffer.
							// Note that we DON'T release the semaphore here, as we ARE adding
							// to the buffer, so it is CORRECT that the semaphore count is reduced.
							var current = _source.Current;
							_currents[consumerNumber] = current;
							_buffer.Add(current);
							_maxBufferSizeUsed = Math.Max(_maxBufferSizeUsed, _buffer.Count);
							++_itemsEnumerated;
						} else
							// The wrapped enumerator is empty, so we can't add to the buffer.
							// Release the semaphore we acquired on the way in.
							_bufferSemaphore?.Release();
					}
				}
			}
			// After all that, clean up the buffer.
			using (await _bufferLock.LockAsync()) {
				// Check how far each consumer has gone.
				// If they're all past the start of the buffer, we can
				// remove items from the start.
				var minIndex = Math.Max(_consumerIndices.Min(), 0);
				var itemsToRemove = minIndex - _bufferStartIndex;
				_bufferStartIndex = minIndex;
				_buffer.RemoveRange(0, itemsToRemove);
				// For every item removed, we can release the semaphore a bit.
				_bufferSemaphore?.Release(itemsToRemove);
			}
			// The final result will be True if there is new data available in _currents,
			// or false if the data has been exhausted.
			return result;
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