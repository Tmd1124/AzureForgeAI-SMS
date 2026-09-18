using Microsoft.Extensions.Logging;
using SmsMessenger.Core.Services;
using SmsMessenger.Core.ViewModels;
using SmsMessenger.Platforms.Android;
using SmsMessenger.Services;

namespace SmsMessenger;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		builder.Services.AddSingleton<IDefaultAppRoleService, DefaultAppRoleService>();
		builder.Services.AddSingleton<IPermissionService, PermissionService>();
		builder.Services.AddSingleton<IContactService, ContactService>();
		builder.Services.AddScoped<INavigationService, NavigationService>();
		builder.Services.AddTransient<SplashViewModel>();
		builder.Services.AddTransient<OnboardingViewModel>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
