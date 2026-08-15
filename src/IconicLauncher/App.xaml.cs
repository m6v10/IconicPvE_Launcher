using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using IconicLauncher.Core.Services;
using IconicLauncher.Core.Utils;
using IconicLauncher.Services;
using IconicLauncher.ViewModels;
using Serilog;

namespace IconicLauncher;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private MainViewModel? _mainViewModel;
    private DiscordRichPresenceService? _rpc;
    private object? _ugcSession;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var adminMode = false;
        string? afterUpdatePath = null;
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (string.Equals(e.Args[i], "--admin", StringComparison.OrdinalIgnoreCase))
            {
                adminMode = true;
            }
            else if (string.Equals(e.Args[i], "--after-update", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                afterUpdatePath = e.Args[++i];
            }
        }
        _mutex = new Mutex(true, "IconicLauncher_SingleInstance", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LauncherConstants.AppDataFolderName);
        var logDir = Path.Combine(appDataDir, "logs");
        try
        {
            Directory.CreateDirectory(logDir);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LogLevelController.Switch)
                .WriteTo.File(Path.Combine(logDir, "launcher-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();
        }
        catch
        {
        }
        Log.Information("Iconic PvE Launcher starting, admin={Admin}", adminMode);
        if (afterUpdatePath != null)
        {
            try
            {
                SelfUpdateService.CleanupAfterUpdate(afterUpdatePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Cleanup after update failed");
            }
        }
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        try
        {
            var settingsService = new SettingsService();
            LogLevelController.SetDebug(settingsService.Settings.DebugLogging);
            if (settingsService.Settings.DebugLogging)
            {
                Log.Debug("Debug logging enabled from settings");
            }
            var configUrl = settingsService.Settings.ConfigUrlOverride ?? LauncherConstants.DefaultConfigUrl;
            var configService = new RemoteConfigService(configUrl, settingsService.AppDataDir, ReadEmbeddedFallback);
            var steamLocator = new SteamLibraryLocator();
            var workshopWebApi = new WorkshopWebApi();
            var verificationService = new ModVerificationService(workshopWebApi, new ModIntegrityChecker());
            var ugcSession = new SteamUgcSession();
            _ugcSession = ugcSession;
            var gameLauncher = new GameLauncher();
            var a2sService = new A2SQueryService();
            var serverModQueryService = new A2SRulesQueryService();
            var selfUpdateService = new SelfUpdateService(settingsService.AppDataDir);
            _rpc = new DiscordRichPresenceService();
            var adminConfigBuilder = new AdminConfigBuilder();
            var ftpPublishService = new FtpPublishService();
            var wowPatchService = new WowPatchService();
            var wowStatusService = new WowStatusService();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionText = version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
            _mainViewModel = new MainViewModel(
                settingsService,
                configService,
                steamLocator,
                verificationService,
                ugcSession,
                ugcSession,
                gameLauncher,
                a2sService,
                serverModQueryService,
                selfUpdateService,
                _rpc,
                adminConfigBuilder,
                ftpPublishService,
                wowPatchService,
                wowStatusService,
                adminMode,
                versionText);
            _mainViewModel.ExitRequested += () => Dispatcher.Invoke(Shutdown);
            var window = new MainWindow { DataContext = _mainViewModel };
            MainWindow = window;
            window.Show();
            _ = _mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Launcher startup failed");
            MessageBox.Show("The launcher could not start:\n" + ex.Message, "Iconic PvE Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static string? ReadEmbeddedFallback()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IconicLauncher.Assets.fallback-config.json");
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading the embedded fallback config failed");
            return null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show("An unexpected error occurred:\n" + e.Exception.Message, "Iconic PvE Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        Log.Error(e.ExceptionObject as Exception, "Unhandled domain exception, terminating={Terminating}", e.IsTerminating);
        Log.CloseAndFlush();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        try
        {
            _rpc?.Dispose();
        }
        catch
        {
        }
        try
        {
            (_ugcSession as IDisposable)?.Dispose();
        }
        catch
        {
        }
        Log.Information("Launcher exiting");
        Log.CloseAndFlush();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
            }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
