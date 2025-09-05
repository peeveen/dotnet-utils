using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
		private int _bufferCleanupTriggerSize;

		/// <summary>
		/// Constructor.
		/// </summary>
		/// <param name="source">Source enumerable to wrap.</param>
		/// <param name="consumerCount">Number of consumers.</param>
		/// <param name="maxBufferSize">Maximum buffer size. If less than 1, buffer size will be limitless.</param>
		/// <param name="bufferCleanupTriggerSize">Buffer will be cleaned up if it contains at least this number of items.</param>
		internal MultiplexingAsyncEnumerable(IAsyncEnumerable<T> source, int consumerCount, int maxBufferSize = 0, int bufferCleanupTriggerSize = 1) {
			if (bufferCleanupTriggerSize < 1)
				throw new ArgumentException("Value must be greater than zero.", nameof(bufferCleanupTriggerSize));
			if (bufferCleanupTriggerSize > maxBufferSize && maxBufferSize > 0)
				throw new ArgumentException("Value must be less than or equal to maxBufferSize.", nameof(bufferCleanupTriggerSize));
			if (consumerCount < 1)
				throw new ArgumentException("There must be at least one consumer.", nameof(consumerCount));
			_consumerCount = consumerCount;
			_source = source;
			_maxBufferSize = maxBufferSize;
			_bufferCleanupTriggerSize = bufferCleanupTriggerSize;
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
					_persistingEnumerator = new PersistingEnumerator<T>(_source.GetAsyncEnumerator(cancellationToken), _consumerCount, _maxBufferSize, _bufferCleanupTriggerSize);
				++_persistingEnumerator._usages;
			}
			return new MultiplexingAsyncEnumerator<T>(_persistingEnumerator, consumerIndex);
		}
	}

	internal class PersistingEnumerator<T> {
		// The wrapped enumerator.
		private readonly IAsyncEnumerator<T> _source;
		// The buffer containing items obtained from the wrapped enumerator.
		private readonly List<Task<(bool, T)>> _buffer = new List<Task<(bool, T)>>();
		// Semaphore that implements the "max buffer size" functionality.
		private readonly SemaphoreSlim _bufferSemaphore;
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
		private readonly object _bufferLock = new object();
		// Number of times that this enumerator has been used.
		// It is shared across multiple instances of MultiplexingAsyncEnumerator, so when
		// Dispose() is called, it should NOT dispose until there are no active usages.
		internal int _usages;
		// The buffer size cleanup limit trigger.
		private readonly int _bufferCleanupTriggerSize;

		internal PersistingEnumerator(IAsyncEnumerator<T> source, int consumerCount, int maxBufferSize, int bufferCleanupTriggerSize) {
			_bufferCleanupTriggerSize = bufferCleanupTriggerSize;
			_bufferSemaphore = maxBufferSize > 0 ? new SemaphoreSlim(maxBufferSize, maxBufferSize) : null;
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
			// The semaphore prevents us adding more than "maxBufferSize" items to the
			// buffer.
			// Since the buffer actually contains TASKS that return items, there may
			// actually be one more than that (e.g. if the maxBufferSize is 10, and
			// there are already 10 tasks in the buffer, the 11th task that we add
			// will block at this WaitAsync until the semaphore is available).
			if (_bufferSemaphore != null)
				await _bufferSemaphore.WaitAsync();
			// See if the wrapped enumerator has more data.
			// We set a member variable indicating whether there is more data available
			// so that we don't make further unnecessary calls to MoveNextAsync.
			// Since this variable is shared between consumers, access would normally
			// need to be protected with a lock, but this is a simple boolean that
			// will, at some point, flip from false to true, but, importantly, never
			// back again. So I *think* it should be okay.
			if (_hasMoreData = _hasMoreData && await _source.MoveNextAsync()) {
				// There IS more data in the wrapped enumerator.
				// So grab it, and return it.
				// Note that we DON'T release the semaphore here, as we ARE adding
				// to the buffer, so it is CORRECT that the semaphore count is reduced.
				// (It will be increased again once items are removed from the tail
				// of the buffer).
				Interlocked.Increment(ref _itemsEnumerated);
				return (true, _source.Current);
			}
			// There was no more data. Best release the semaphore that we acquired.
			_bufferSemaphore?.Release();
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
			Task<(bool moreDataAvailable, T item)> resultTask;
			lock (_bufferLock) {
				// First, tidy up the buffer, deallocating items that every
				// consumer has consumed.
				// We only do this once the buffer reaches a minimum size.
				var bufferSize = _buffer.Count;
				if (bufferSize >= _bufferCleanupTriggerSize) {
					// Check how far each consumer has gone.
					// If they're all past the start of the buffer, we can
					// remove items from the start.
					var minIndex = Math.Max(_consumerIndices.Min(), 0);
					var itemsToRemove = minIndex - _bufferStartIndex;
					_bufferStartIndex = minIndex;
					_buffer.RemoveRange(0, itemsToRemove);
					if (itemsToRemove > 0) {
						_bufferSemaphore?.Release(itemsToRemove);
						bufferSize -= itemsToRemove;
					}
				}

				// Figure out the actual buffer index that we want to access.
				var bufferIndex = consumerIndex - _bufferStartIndex;
				// Do we need to add more data to the buffer?
				// Or do we already have enough?
				if (bufferIndex >= bufferSize) {
					// Not enough data. Add a task that will retrieve the next
					// item from the wrapped enumerator (if available).
					_buffer.Add(GetNextItemAsync());
					_maxBufferSizeUsed = Math.Max(_maxBufferSizeUsed, bufferSize + 1);
				}
				// There should now be enough. At any time, the bufferIndex is
				// guaranteed to be, at most, one greater than the current
				// buffer count.
				// So by adding another (above), we should now be able to
				// safely access the list.
				resultTask = _buffer[bufferIndex];
			}
			// Outside the lock now, we can await the task.
			// (If we await INSIDE the lock, the semaphore wait could lock,
			// and we'd be in DEADLOCK).
			var (moreDataAvailable, item) = await resultTask;
			// Each task returns a tuple: (bool, T)
			// Boolean value indicates whether there was more data available in
			// the wrapper enumerator.
			// Second value is the data item, so we can set it in the _currents array.
			// (If the bool is false, the T will be "default" ... it will do
			// no harm to set it in the _currents array, as the caller should
			// not care about the value if there was no more data).
			_currents[consumerNumber] = item;
			// Return the "has more data" value, as the interface demands.
			return moreDataAvailable;
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
		/// <param name="bufferCleanupTriggerSize">Items in the internal buffer (containing cached data) will be
		/// removed once all consumers have consumed them. The value of this parameter controls how often this
		/// happens. The cleanup will only be performed (during MoveNextAsync) if the internal buffer contains
		/// AT LEAST the number of items specified by this parameter. This value of this parameter MUST be
		/// less than or equal to maxBufferSize.</param>
		/// <returns></returns>
		public static MultiplexingAsyncEnumerable<T> ToMultiplexingAsyncEnumerable<T>(
			this IAsyncEnumerable<T> source,
			int consumerCount,
			int maxBufferSize = 0,
			int bufferCleanupTriggerSize = 1
		) =>
			new MultiplexingAsyncEnumerable<T>(source, consumerCount, maxBufferSize, bufferCleanupTriggerSize);
	}
}