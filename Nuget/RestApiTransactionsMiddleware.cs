using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace RestApiTransaction;

public class RestApiTransactionsMiddleware(RequestDelegate next)
{
	/// <summary>
	/// Process the request
	/// </summary>
	/// <param name="context">Current HttpContext</param>
	public async Task InvokeAsync(HttpContext context)
	{
		Debug.WriteLine($"Invoke: \"{context.Request.Path}\"");
		Guid? transactionId = null;

		if (RunMode == RunModes.Headers
			&& !context.Request.Headers.ContainsKey(RestApiParameters.HeaderActivation)
		)
		{
			Debug.WriteLine($"Activation header missing, excuting \"{context.Request.Path}\"");
			await next(context);
			Debug.WriteLine($"Finished: \"{context.Request.Path}\"");
			return;
		}

		if (context.Request.Headers.TryGetValue(RestApiParameters.HeaderStartTransaction, out StringValues transactionItems))
		{
			transactionId = await StartTransaction(transactionItems);

			context.Response.Headers.Append(RestApiParameters.HeaderTransactionId, transactionId.ToString());
		}
		else if (context.Request.Headers.TryGetValue(RestApiParameters.HeaderTransactionId, out StringValues transactionIds))
		{
			if (transactionIds.Count > 0 && Guid.TryParse(transactionIds.First(), out Guid id))
			{
				transactionId = id;
			}
		}

		var queueItem = new QueueItem()
		{
			Id = Guid.NewGuid(),
			IsTransaction = false,
			TransactionId = transactionId,
			Read = context.GetEndpoint()?.Metadata
				.Where((e) =>
					e is RestApiReadAttribute
					|| e is RestApiWriteAttribute
				)
				.SelectMany(a =>
					(a as IRestApiAttribute)?.Parameters ?? []
				)
				.Distinct()
				?? [],
			Write = context.GetEndpoint()?.Metadata
				.Where((e) =>
					e is RestApiWriteAttribute
				)
				.SelectMany(a =>
					(a as IRestApiAttribute)?.Parameters ?? []
				)
				.Distinct()
				?? [],
			Promise = new()
		};

		if (queueItem.Read.Any() || transactionId != null)
		{
			lock (_lock)
			{
				_queue.Add(queueItem);
			}

			new Task(ProcessQueue).Start();

			await queueItem.Promise.Task;

			Debug.WriteLine($"Execute: \"{context.Request.Path}\"");
			await next(context);

			if (queueItem.TransactionId != null
				&& context.Request.Headers.ContainsKey(RestApiParameters.HeaderEndTransaction)
			)
			{
				EndTransaction(queueItem.TransactionId.Value);
			}

			ReleaseQueue(queueItem);
		}
		else
		{
			Debug.WriteLine($"Execute: \"{context.Request.Path}\"");
			await next(context);
		}

		Debug.WriteLine($"Finished: \"{context.Request.Path}\"");
	}

	/// <summary>
	/// Locks the items of the next request in the queue
	/// </summary>
	private void ProcessQueue()
	{
		QueueItem queueItem;

		lock (_lock)
		{
			var response = FindQueueItem();

			if (response == null)
			{
				return;
			}

			queueItem = response.Value;

			if (queueItem.IsTransaction)
			{
				var cancellationTokenSource = new CancellationTokenSource();
				var transactionItem = new TransactionItem
				{
					Items = queueItem.Read,
					CancelTrigger = cancellationTokenSource,
					Timeout = new Task(
						() =>
						{
							Thread.Sleep(TransactionTimeoutMs);
							EndTransaction(queueItem.Id);
							new Task(ProcessQueue).Start();
						},
						cancellationTokenSource.Token
					)
				};

				_transactions.TryAdd(queueItem.Id, transactionItem);

				transactionItem.Timeout.Start();
			}

			foreach (var read in queueItem.Read)
			{
				Debug.WriteLine($"Locking read: {read}");
				var reading = _reading.GetOrAdd(read, new ConcurrentDictionary<Guid, bool>());

				reading.TryAdd(queueItem.Id, true);
			}

			foreach (var write in queueItem.Write)
			{
				Debug.WriteLine($"Locking write: {write}");
				var writing = _writing.GetOrAdd(write, new ConcurrentDictionary<Guid, bool>());

				writing.TryAdd(queueItem.Id, true);
			}

			var reads = string.Join(
					'|',
					_reading.Select(reading =>
						$"{reading.Key}={string.Join(',', reading.Value.Keys)}"
					)
				);

			var writes = string.Join(
					'|',
					_writing.Select(writing =>
						$"{writing.Key}={string.Join(',', writing.Value.Keys)}"
					)
				);

			Debug.WriteLine($"Read locks: {reads}");
			Debug.WriteLine($"Write locks: {writes}");
		}

		queueItem.Promise.SetResult();
	}

	/// <summary>
	/// Find the next item in the queue that is not blocked
	/// </summary>
	/// <returns>A queue item or null</returns>
	private static QueueItem? FindQueueItem()
	{
		for (var i = 0; i < _queue.Count; i++)
		{
			var queueItem = _queue[i];

			bool blocked =
				queueItem.Read.Any(read =>
					_writing.Any(writing =>
						writing.Key == read
						&& !writing.Value.IsEmpty
						&& writing.Value.Any(v => v.Key != queueItem.TransactionId)
					)
				)
				|| queueItem.Write.Any(write =>
					_reading.Any(reading =>
						reading.Key == write
						&& !reading.Value.IsEmpty
						&& reading.Value.Any(v => v.Key != queueItem.TransactionId)
					)
				);

			Debug.WriteLine($"Checking: read={string.Join(',', queueItem.Read)}; write={string.Join(',', queueItem.Write)}; blocked={blocked}");

			var reads = string.Join(
					'|',
					_reading.Select(reading =>
						$"{reading.Key}={string.Join(',', reading.Value.Keys)}"
					)
				);

			var writes = string.Join(
					'|',
					_writing.Select(writing =>
						$"{writing.Key}={string.Join(',', writing.Value.Keys)}"
					)
				);

			Debug.WriteLine($"- reads: {reads}");
			Debug.WriteLine($"- writes: {writes}");

			if (!blocked)
			{
				_queue.RemoveAt(i);

				return queueItem;
			}
		}

		return null;
	}

	/// <summary>
	/// Releases items of a request in the queue
	/// </summary>
	/// <param name="queueItem">Queue item to release</param>
	private void ReleaseQueue(QueueItem queueItem)
	{
		lock (_lock)
		{
			foreach (var read in queueItem.Read)
			{
				if (_reading.TryGetValue(read, out var reading))
				{
					Debug.WriteLine($"Release read: {read}");
					reading.TryRemove(queueItem.Id, out _);
				}
			}

			foreach (var write in queueItem.Write)
			{
				if (_writing.TryGetValue(write, out var writing))
				{
					Debug.WriteLine($"Release write: {write}");
					writing.TryRemove(queueItem.Id, out _);
				}
			}
		}

		Thread.Sleep(500);

		new Task(ProcessQueue).Start();
	}

	/// <summary>
	/// Creates a new transaction and locks items related to it
	/// </summary>
	/// <param name="transactionItems">List of items to be locked by the transaction</param>
	/// <returns>TransactionId</returns>
	private async Task<Guid> StartTransaction(StringValues transactionItems)
	{
		var id = Guid.NewGuid();

		var transactionItemList = transactionItems
									.Where(ti => ti != null)
									.SelectMany(ti => ti!.Split(','))
									.Select(s => s.Trim())
									.Distinct();

		var queueItem = new QueueItem()
		{
			Id = id,
			IsTransaction = true,
			TransactionId = id,
			Read = transactionItemList,
			Write = transactionItemList,
			Promise = new()
		};

		lock (_lock)
		{
			_queue.Add(queueItem);
		}

		Debug.WriteLine($"Starting transaction: {queueItem.Id}");

		new Task(ProcessQueue).Start();

		await queueItem.Promise.Task;

		return id;
	}

	/// <summary>
	/// Stops transaction and releases items related to it
	/// </summary>
	/// <param name="transactionIds">List of transaction ids to end</param>
	private static void EndTransaction(Guid transactionId)
	{
		lock (_lock)
		{
			if (_transactions.TryGetValue(transactionId, out var transactionItem))
			{
				Debug.WriteLine($"Ending transaction {transactionId}");

				transactionItem.CancelTrigger.Cancel();

				foreach (var item in transactionItem.Items)
				{
					if (_reading.TryGetValue(item, out var reading))
					{
						reading.TryRemove(transactionId, out _);
					}

					if (_writing.TryGetValue(item, out var writing))
					{
						writing.TryRemove(transactionId, out _);
					}
				}

				_transactions.TryRemove(transactionId, out _);
			}
		}
	}

	public static RunModes RunMode { get; set; } = RunModes.On;
	public static int TransactionTimeoutMs { get; set; } = 5000;

	public enum RunModes
	{
		On,
		Headers
	};

	private static readonly object _lock = new();

	private readonly static ConcurrentDictionary<string, ConcurrentDictionary<Guid, bool>> _reading = [];
	private readonly static ConcurrentDictionary<string, ConcurrentDictionary<Guid, bool>> _writing = [];
	private readonly static ConcurrentDictionary<Guid, TransactionItem> _transactions = [];
	private readonly static List<QueueItem> _queue = [];

	private readonly record struct QueueItem
	{
		public readonly Guid Id { get; init; }
		public readonly bool IsTransaction { get; init; }
		public readonly Guid? TransactionId { get; init; }
		public required IEnumerable<string> Read { get; init; }
		public required IEnumerable<string> Write { get; init; }
		public required TaskCompletionSource Promise { get; init; }
	}

	private readonly record struct TransactionItem
	{
		public readonly IEnumerable<string> Items { get; init; }
		public readonly CancellationTokenSource CancelTrigger { get; init; }
		public readonly Task Timeout { get; init; }
	}
}
