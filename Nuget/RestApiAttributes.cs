namespace RestApiTransaction;

interface IRestApiAttribute
{
	public string[] Parameters { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public class RestApiReadAttribute(params string[] parameters) : Attribute, IRestApiAttribute
{
	public string[] Parameters { get; } = parameters;
}

[AttributeUsage(AttributeTargets.Method)]
public class RestApiWriteAttribute(params string[] parameters) : Attribute, IRestApiAttribute
{
	public string[] Parameters { get; } = parameters;
}
