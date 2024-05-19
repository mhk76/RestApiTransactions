using RestApiTransaction;
using Microsoft.AspNetCore.Mvc;

namespace Controllers;

[ApiController]
[Route("[controller]")]
public class TestController : ControllerBase
{
	[HttpGet("long-write/read")]
	[RestApiRead("long-write")]
	public string ReadLongWrite()
	{
		Thread.Sleep(100);
		return "long-write/read";
	}

	[HttpGet("long-write/write")]
	[RestApiWrite("long-write")]
	public string WriteLongWrite()
	{
		Thread.Sleep(2000);
		return "long-write/write";
	}

	[HttpGet("long-read/long-read")]
	[RestApiRead("long-read")]
	public string ReadLongLongRead()
	{
		Thread.Sleep(2000);
		return "long-read/long-read";
	}

	[HttpGet("long-read/quick-read")]
	[RestApiRead("long-read")]
	public string ReadQuickLongRead()
	{
		Thread.Sleep(100);
		return "long-read/quick-read";
	}

	[HttpGet("long-read/write")]
	[RestApiWrite("long-read")]
	public string WriteLongRead()
	{
		Thread.Sleep(100);
		return "long-read/write";
	}

	[HttpGet("transaction/action")]
	[RestApiRead("transaction")]
	public string ActionTransaction()
	{
		Thread.Sleep(1000);
		return "transaction/action";
	}

	[HttpGet("neutral")]
	public string Neutral()
	{
		Thread.Sleep(100);
		return "neutral";
	}
}
