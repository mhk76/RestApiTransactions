# RestApiTransactions

Middleware that adds transactions for testing REST API

## Installation

```bash
nuget install RestApiTransactions
```

## Activation

Add to your Program.cs
```C#
app.UseRestApiTransactions();
```

Add to your project´s appsettings.json
```json
"RestApiTransactions": {
	"Mode": "Headers",
	"TimeoutSeconds": 5
}
```
**Mode** can have values **Always** and **Headers**.
- Value **Always** means that transactions are always on.
- Value **Headers** means that transations are applied only when the request have **RestApiTransaction** header set.
Otherwise they are executed normally, like without the middleware.

If invalid or no value is set to **Mode**, then the middleware is not activated.

**TimeoutSeconds** define how long transactions can last before they are automatically cancelled. The request might
continue running after the timeout, but it does not block anymore other requests. By default the timeout is 5 seconds.

## Usage
In your controller you can add attributes to endpoints that require blocking
```C#
[HttpGet("{id}")]
[RestApiRead("clients-table", "companies-table")]
public string GetClient(long id) { ... }

[HttpPut("{id}")]
[RestApiWrite("clients-table")]
public string GetClient(long id, [FromBody] Client client) { ... }
```

**RestApiRead** blocks requests using **RestApiWrite** and transactions with the specified keywords.

**RestApiWrite** blocks all request using the specified keywords.
Attributes block other request only while they are executing. When the request finishes, the keywords are freed.


In order to block several requests, you need to you transaction headers. These are:
- **RestApiTransaction-Start**, which starts a transaction. Value is comma-separated list of keywords to block.
The response will contain header **RestApiTransaction-Id** which is required to execute other requests with specified
- keywords during the transaction.
- **RestApiTransaction-Id** informs to which transaction this request belongs.
- **RestApiTransaction-End** ends the current transaction.

Transactions block all the requests with specified keyword that are not using correct **RestApiTransaction-Id** header.
