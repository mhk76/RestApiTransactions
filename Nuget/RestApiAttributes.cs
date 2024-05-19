namespace RestApiTransaction;

interface IRestApiAttribute
{
	public string[] Parameters { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public class RestApiReadAttribute : Attribute, IRestApiAttribute
{
	public string[] Parameters { get; }

	public RestApiReadAttribute(params string[] parameters)
	{
		Parameters = parameters;
	}
}

[AttributeUsage(AttributeTargets.Method)]
public class RestApiWriteAttribute : Attribute, IRestApiAttribute
{
	public string[] Parameters { get; }

	public RestApiWriteAttribute(params string[] parameters)
	{
		Parameters = parameters;
	}
}
