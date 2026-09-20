using Microsoft.Extensions.Logging;
using SmsMessenger.Core.Data;
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
		builder.Services.AddSingleton<IThreadService, ThreadService>();
		builder.Services.AddSingleton<ISmsService, SmsService>();
		builder.Services.AddScoped<INavigationService, NavigationService>();
		builder.Services.AddTransient<SplashViewModel>();
		builder.Services.AddTransient<OnboardingViewModel>();
		builder.Services.AddTransient<ConversationsViewModel>();
		builder.Services.AddTransient<ComposeViewModel>();
		builder.Services.AddTransient<ContactPickerViewModel>();
		builder.Services.AddSingleton<INotificationService, NotificationService>();

		var trashRepository = new TrashRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
		trashRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<ITrashRepository>(trashRepository);
		builder.Services.AddSingleton<IUndoStack, UndoStack>();
		builder.Services.AddTransient<TrashViewModel>();

		var blockedNumberRepository = new BlockedNumberRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
		blockedNumberRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IBlockedNumberRepository>(blockedNumberRepository);
		builder.Services.AddSingleton<IContactBlockService, ContactBlockService>();
		builder.Services.AddTransient<BlockedViewModel>();

		var favoriteRepository = new FavoriteRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
		favoriteRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IFavoriteRepository>(favoriteRepository);

		var archiveRepository = new ArchiveRepository(Path.Combine(FileSystem.AppDataDirectory, "SmsMessenger.db"));
		archiveRepository.InitializeAsync().GetAwaiter().GetResult();
		builder.Services.AddSingleton<IArchiveRepository>(archiveRepository);
		builder.Services.AddTransient<ArchivedViewModel>();

		builder.Services.AddSingleton<IMarkAsReadService, MarkAsReadService>();
		builder.Services.AddTransient<MenuViewModel>();

		builder.Services.AddSingleton<IThemeService, ThemeService>();
		builder.Services.AddTransient<ThemeViewModel>();

		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddSingleton<PendingNavigationStore>();

		builder.Services.AddSingleton<IProfileService, ProfileService>();
		builder.Services.AddTransient<ProfileViewModel>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
