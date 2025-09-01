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
		private readonly int[] _consumerIndices;
		private readonly T[] _currents;
		private readonly IAsyncEnumerator<T> _source;
		private readonly List<T> _buffer = new List<T>();
		private int _bufferStartIndex;
		private bool _hasMoreData = true;
		private readonly AsyncLock _bufferLock = new AsyncLock();
		internal int _maxBufferSizeUsed;
		internal int _itemsEnumerated;
		private readonly bool _singleConsumer;
		private readonly AsyncSemaphore _bufferSemaphore;

		internal PersistingEnumerator(IAsyncEnumerator<T> source, int consumerCount, int maxBufferSize) {
			if (consumerCount < 1)
				throw new ArgumentException("There must be at least one consumer.", nameof(consumerCount));
			_bufferSemaphore = new AsyncSemaphore(maxBufferSize < 1 ? int.MaxValue : (maxBufferSize + (consumerCount - 1)));
			_singleConsumer = consumerCount == 1;
			_source = source;
			_consumerIndices = new int[consumerCount];
			_currents = new T[consumerCount];
			for (var i = 0; i < consumerCount; i++)
				_consumerIndices[i] = -1;
		}

		public T GetCurrent(int consumerNumber) => _singleConsumer ? _source.Current : _currents[consumerNumber];

		public ValueTask DisposeAsync() => _source.DisposeAsync();

		public async ValueTask<bool> MoveNextAsync(int consumerNumber) {
			if (_singleConsumer)
				return await _source.MoveNextAsync();
			Func<Task<bool>> afterTask;
			using (await _bufferLock.LockAsync()) {
				if (!_hasMoreData)
					return false;
				var newConsumerIndex = ++_consumerIndices[consumerNumber];
				var bufferIndex = newConsumerIndex - _bufferStartIndex;
				// If there is enough data in the buffer, then yes, we can move.
				if (bufferIndex < _buffer.Count) {
					_currents[consumerNumber] = _buffer[bufferIndex];
					afterTask = () => Task.FromResult(true);
				} else {
					// Otherwise we need to add to the buffer.
					afterTask = async () => {
						await _bufferSemaphore.WaitAsync();
						using (await _bufferLock.LockAsync()) {
							// Recalculate buffer index in case start index has changed.
							var newBufferIndex = newConsumerIndex - _bufferStartIndex;
							if (newBufferIndex < _buffer.Count) {
								_currents[consumerNumber] = _buffer[newBufferIndex];
								_bufferSemaphore.Release();
								return true;
							}
							var addResult = _hasMoreData = _hasMoreData && await _source.MoveNextAsync();
							if (addResult) {
								var current = _source.Current;
								_currents[consumerNumber] = current;
								_buffer.Add(current);
								_maxBufferSizeUsed = Math.Max(_maxBufferSizeUsed, _buffer.Count);
								++_itemsEnumerated;
							} else
								_bufferSemaphore.Release();
							return addResult;
						}
					};
				}
			}
			var result = await afterTask.Invoke();
			using (await _bufferLock.LockAsync()) {
				var minIndex = Math.Max(_consumerIndices.Min(), 0);
				var itemsToRemove = minIndex - _bufferStartIndex;
				_bufferStartIndex = minIndex;
				_buffer.RemoveRange(0, itemsToRemove);
				_bufferSemaphore.Release(itemsToRemove);
				var bufferSize = _buffer.Count;
			}
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