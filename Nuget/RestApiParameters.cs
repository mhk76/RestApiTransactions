namespace RestApiTransaction;

public class RestApiParameters
{
	public const string HeaderActivation = "RestApiTransaction";
	public const string HeaderStartTransaction = "RestApiTransaction-Start";
	public const string HeaderEndTransaction = "RestApiTransaction-End";
	public const string HeaderTransactionId = "RestApiTransaction-Id";

	public const string SettingsSection = "RestApiTransactions";
	public const string SettingsMode = "Mode";
	public const string SettingsModeAlways = "Always";
	public const string SettingsModeHeaders = "Headers";
	public const string SettingsTimeout = "TimeoutSeconds";
}
