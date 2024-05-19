using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace RestApiTransaction;

public static class RestApiTransactionsMiddlewareExtension
{
	public static IApplicationBuilder UseRestApiTransactions(this IApplicationBuilder builder)
	{
		var configuration = (IConfiguration?)builder.ApplicationServices.GetService(typeof(IConfiguration));
		var settings = configuration?.GetSection(RestApiParameters.SettingsSection);
		var mode = settings?.GetSection(RestApiParameters.SettingsMode).Value;

		if (mode == RestApiParameters.SettingsModeAlways || mode == RestApiParameters.SettingsModeHeaders)
		{
			RestApiTransactionsMiddleware.RunMode = mode switch
				{
					RestApiParameters.SettingsModeHeaders => RestApiTransactionsMiddleware.RunModes.Headers,
					_ => RestApiTransactionsMiddleware.RunModes.On
				};

			var timeoutValue = settings?.GetSection(RestApiParameters.SettingsTimeout).Value;

			if (int.TryParse(timeoutValue, out int timeout) && timeout > 0 && timeout <= 60)
			{
				RestApiTransactionsMiddleware.TransactionTimeoutMs = timeout * 1000;
			}

			return builder.UseMiddleware<RestApiTransactionsMiddleware>();
		}
		else
		{
			return builder;
		}
	}
}
