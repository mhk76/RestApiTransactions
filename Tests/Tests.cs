using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using RestApiTransaction;

namespace RestApiTransactionsTests;

public class TestClass
{
	[Fact]
	public async Task ReadWhileLongWrite()
	{
		var results = new long[3];
		var tasks = new TaskCompletionSource[3] { new(), new(), new() };

		// Long locking write, should finish second
		LaunchRequest("long-write/write", 0, results, tasks);

		Thread.Sleep(500);

		// Read blocked by the write, should finish last
		LaunchRequest("long-write/read", 1, results, tasks);

		// Action that is not involved in locks, should finish first
		LaunchRequest("neutral", 2, results, tasks);

		await tasks[0].Task;
		await tasks[1].Task;
		await tasks[2].Task;

		Assert.True(results[2].CompareTo(results[0]) == -1, "neutral before read");
		Assert.True(results[2].CompareTo(results[1]) == -1, "neutral before write");
		Assert.True(results[0].CompareTo(results[1]) == -1, "write before read");
	}

	[Fact]
	public async Task ReadWhileLongWrite_WithoutHeader()
	{
		var results = new long[3];
		var tasks = new TaskCompletionSource[3] { new(), new(), new() };

		// Without activation header should finish first
		LaunchRequest("long-write/write", 0, results, tasks, null, false);

		Thread.Sleep(2500);

		// Without activation header should finish second
		LaunchRequest("long-write/read", 1, results, tasks, null, false);

		Thread.Sleep(100);

		// Without activation header should finish last
		LaunchRequest("neutral", 2, results, tasks, null, false);

		await tasks[0].Task;
		await tasks[1].Task;
		await tasks[2].Task;

		Assert.True(results[0].CompareTo(results[1]) == -1, "write before read");
		Assert.True(results[1].CompareTo(results[2]) == -1, "read before neutral");
	}

	[Fact]
	public async Task WriteWhileLongRead()
	{
		var results = new long[4];
		var tasks = new TaskCompletionSource[4] { new(), new(), new(), new() };

		// Long locking read, should finish third
		LaunchRequest("long-read/long-read", 0, results, tasks);

		Thread.Sleep(500);

		// Write blocked by the read, should finish last
		LaunchRequest("long-read/write", 1, results, tasks);

		// Quick read, which should not be blocked by the long read , should finish first or second
		LaunchRequest("long-read/quick-read", 2, results, tasks);

		// Action that is not involved in locks, should finish first or second
		LaunchRequest("neutral", 3, results, tasks);

		await tasks[0].Task;
		await tasks[1].Task;
		await tasks[2].Task;
		await tasks[3].Task;

		Assert.True(results[3].CompareTo(results[0]) == -1, "neutral before long-read");
		Assert.True(results[2].CompareTo(results[0]) == -1, "quick-read before long-read");
		Assert.True(results[3].CompareTo(results[1]) == -1, "neutral before write");
		Assert.True(results[2].CompareTo(results[1]) == -1, "quick-read before write");
		Assert.True(results[0].CompareTo(results[1]) == -1, "long-read before write");
	}

	[Fact]
	public async Task Transaction()
	{
		// Start the transaction
		var factory = new WebApplicationFactory<Program>();
		var client = factory.CreateClient();

		client.DefaultRequestHeaders.Add(RestApiParameters.HeaderActivation, "On");
		client.DefaultRequestHeaders.Add(RestApiParameters.HeaderStartTransaction, "transaction");

		var response = await client.GetAsync("test/transaction/action");

		Assert.True(response?.StatusCode == HttpStatusCode.OK, "test/transaction/action should return OK");
		Assert.True(response.Headers.Contains(RestApiParameters.HeaderTransactionId));

		var transactionId = response.Headers.GetValues(RestApiParameters.HeaderTransactionId).First();

		var results = new long[3];
		var tasks = new TaskCompletionSource[4] { new(), new(), new(), new() };

		// Action outside of the transaction, should finish last
		LaunchRequest("transaction/action", 0, results, tasks);

		// Action inside the transaction, should finish second. Also ends the transaction
		LaunchRequest(
			"transaction/action",
			1,
			results,
			tasks,
			[
				new (RestApiParameters.HeaderTransactionId, transactionId),
				new (RestApiParameters.HeaderEndTransaction, transactionId)
			]
		);

		// Action that is not involved in locks, should finish first
		LaunchRequest("neutral", 2, results, tasks);

		await tasks[0].Task;
		await tasks[1].Task;
		await tasks[2].Task;

		Assert.True(results[2].CompareTo(results[0]) == -1, "neutral before outside action");
		Assert.True(results[2].CompareTo(results[1]) == -1, "neutral before inside action");
		Assert.True(results[1].CompareTo(results[0]) == -1, "inside action before outside action");
	}


	[Fact]
	public async Task TransactionTimeout()
	{
		// Start the transaction
		var factory = new WebApplicationFactory<Program>();
		var client = factory.CreateClient();

		client.DefaultRequestHeaders.Add(RestApiParameters.HeaderActivation, "On");
		client.DefaultRequestHeaders.Add(RestApiParameters.HeaderStartTransaction, "transaction");

		var response = await client.GetAsync("test/transaction/action");

		Assert.True(response?.StatusCode == HttpStatusCode.OK, "test/transaction/action should return OK");
		Assert.True(response.Headers.Contains(RestApiParameters.HeaderTransactionId));

		var transactionId = response.Headers.GetValues(RestApiParameters.HeaderTransactionId).First();

		var results = new long[1];
		var tasks = new TaskCompletionSource[1] { new() };

		// Action outside of the transaction, should finish when the transaction times out
		LaunchRequest("transaction/action", 0, results, tasks);

		await tasks[0].Task;
	}

	private static void LaunchRequest(
		string url,
		int index,
		long[] results,
		TaskCompletionSource[] tasks,
		KeyValuePair<string, string>[]? headers = null,
		bool headerActivation = true
	)
	{
		new Task(async () =>
		{
			var factory = new WebApplicationFactory<Program>();
			var client = factory.CreateClient();

			if (headerActivation)
			{
				client.DefaultRequestHeaders.Add(RestApiParameters.HeaderActivation, "On");
			}

			if (headers != null)
			{
				foreach (var header in headers)
				{
					client.DefaultRequestHeaders.Add(header.Key, header.Value);
				}
			}

			var response = await client.GetAsync($"test/{url}");

			Assert.True(response?.StatusCode == HttpStatusCode.OK, $"Request test/{url} should return OK");

			results[index] = DateTime.Now.Ticks;
			tasks[index].SetResult();
		}).Start();
	}
}
