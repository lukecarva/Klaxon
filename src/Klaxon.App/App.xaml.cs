using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Klaxon.App.Services;
using Klaxon.App.ViewModels;
using Klaxon.Core.Abstractions;
using Klaxon.Core.Localization;
using Klaxon.Core.Persistence;
using Klaxon.Core.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using MainWindowView = Klaxon.App.Views.MainWindow;

namespace Klaxon.App;

/// <summary>Application entry point: DI, logging, single instance and startup.</summary>
public partial class App : Application
{
    // Per-user scope for the single-instance mutex.
    private const string SingleInstanceMutexName = @"Local\Klaxon.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private IHost? _host;

    /// <summary>True during an intentional shutdown, so the window really closes.</summary>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>Wires up services and shows the tray icon and main window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // A second instance just exits.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        AppPaths.EnsureCreated();
        ConfigureLogging();
        HookGlobalExceptionHandlers();
        ResolveLanguage();

        Log.Information("Klaxon iniciando ({Idioma}). Dados em {Root}", Loc.Code, AppPaths.Root);

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                // Replace the console lifetime; this is a tray app.
                services.AddSingleton<IHostLifetime, WpfHostLifetime>();

                services.AddSingleton<ISystemClock, SystemClock>();
                services.AddSingleton<IIdleDetector, Win32IdleDetector>();
                services.AddSingleton<IAlarmStore>(sp => new JsonAlarmStore(
                    AppPaths.AlarmsFile,
                    sp.GetRequiredService<ILogger<JsonAlarmStore>>()));
                services.AddSingleton<ISchedulerStateStore>(sp => new JsonSchedulerStateStore(
                    AppPaths.SchedulerStateFile,
                    sp.GetRequiredService<ILogger<JsonSchedulerStateStore>>()));

                services.AddSingleton<IAlarmScheduler, AlarmScheduler>();
                services.AddSingleton<IAlertPresenter, WpfAlertPresenter>();

                services.AddSingleton<AlarmsService>();
                services.AddSingleton<AlarmDialogs>();
                services.AddSingleton<StartupRegistrar>();
                services.AddSingleton<TrayIconService>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindowView>();

                services.AddHostedService<SchedulerHost>();
            })
            .Build();

        // Show the tray before starting the host, which writes its status.
        _host.Services.GetRequiredService<TrayIconService>().Show();

        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindowView>();
        MainWindow = window;

        // --minimized: start hidden in the tray.
        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            window.Show();
        }
    }

    /// <summary>Tears down the host and releases the single-instance mutex.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        Log.Information("Encerrando (código {Code}).", e.ApplicationExitCode);

        if (_host is not null)
        {
            _host.Services.GetRequiredService<TrayIconService>().Dispose();
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>Configures file logging (Serilog, rolling, shared, UTF-8 BOM).</summary>
    private static void ConfigureLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                AppPaths.LogFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>Picks the language at startup: saved setting, else Windows, else English.</summary>
    private static void ResolveLanguage()
    {
        var settings = SettingsStore.Load();

        var language = settings.Language is { Length: > 0 } code
            ? Loc.Parse(code)
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt"
                ? AppLanguage.Portuguese
                : AppLanguage.English;

        Loc.Set(language);

        CultureInfo.CurrentCulture = Loc.Culture;
        CultureInfo.CurrentUICulture = Loc.Culture;
        CultureInfo.DefaultThreadCurrentCulture = Loc.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Loc.Culture;
    }

    /// <summary>Logs any unhandled exception so the process never dies silently.</summary>
    private void HookGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Exceção não tratada no AppDomain. Encerrando: {Terminating}", args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Exceção não observada em Task.");
            args.SetObserved();
        };
    }

    /// <summary>Logs a UI exception, keeps the app running, and warns the user.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exceção não tratada na UI.");

        // Keep the process alive: a UI failure must not take down the scheduler.
        e.Handled = true;

        MessageBox.Show(
            Loc.Format("Error_UIBody", e.Exception.Message, AppPaths.LogsFolder),
            Loc.Get("App_Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
