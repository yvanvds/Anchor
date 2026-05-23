using FocusAgent.App.Auth;
using FocusAgent.App.Focus;
using FocusAgent.App.Realtime;
using FocusAgent.App.Sessions;
using FocusAgent.App.Tray;
using FocusAgent.Core.Auth;
using FocusAgent.Core.Dtos;
using FocusAgent.Core.Focus;
using FocusAgent.Core.Logging;
using FocusAgent.Core.Realtime;
using FocusAgent.Core.Sessions;
using FocusAgent.Core.Settings;
using FocusAgent.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using WinRT.Interop;

namespace FocusAgent.App;

public partial class App : Application
{
    private IHost? _host;
    private MainWindow? _mainWindow;
    private TrayIconHost? _tray;
    private SessionCoordinator? _coordinator;
    private FocusSessionController? _focus;
    private ISessionHubConnection? _hub;
    // Held only by the --show-test-toast path so the logger outlives the
    // async show/decide chain rather than getting disposed at OnLaunched return.
    private ILoggerFactory? _testLoggerFactory;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Program.ShowTestToast)
        {
            RunToastSelfTest();
            return;
        }

        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            _host = BuildHost(dispatcher, () => _mainWindow is null ? IntPtr.Zero : WindowNative.GetWindowHandle(_mainWindow));
            _host.Start();

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("FocusAgent starting (unpackaged WinUI 3)");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
            TaskScheduler.UnobservedTaskException += (_, e) =>
                logger.LogError(e.Exception, "Unobserved task exception");

            _mainWindow = new MainWindow();
            _tray = new TrayIconHost(
                onOpen: () => _mainWindow.Activate(),
                onQuit: () => Exit(),
                dispatcher: dispatcher);
            _tray.Show();

            _hub = _host.Services.GetRequiredService<ISessionHubConnection>();
            _coordinator = _host.Services.GetRequiredService<SessionCoordinator>();
            _focus = _host.Services.GetRequiredService<FocusSessionController>();

            _hub.StateChanged += (_, state) => _tray.UpdateStatus(state, LastDisplayName);
            _ = StartHubAsync(_host.Services, logger);
        }
        catch (Exception ex)
        {
            WriteStartupFailure(ex);
            throw;
        }
    }

    private string? LastDisplayName { get; set; }

    private async Task StartHubAsync(IServiceProvider services, ILogger<App> logger)
    {
        try
        {
            var tokens = services.GetRequiredService<IAuthTokenProvider>();
            var auth = await tokens.AcquireTokenAsync().ConfigureAwait(true);
            LastDisplayName = auth.DisplayName;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acquire access token");
            _tray?.UpdateStatus(AgentConnectionState.Disconnected, LastDisplayName);
            return;
        }

        // SignalR's WithAutomaticReconnect only fires AFTER a connection has
        // been established at least once. Without a first-connect backoff loop
        // a dev who launches the agent before the backend gets a one-shot
        // failure that leaves the tray stuck Disconnected forever (#43). Use
        // the same exponential cap as the post-establishment retry policy.
        var attempt = 0;
        while (true)
        {
            _tray?.UpdateStatus(
                attempt == 0 ? AgentConnectionState.Connecting : AgentConnectionState.Reconnecting,
                LastDisplayName);
            try
            {
                await _hub!.StartAsync().ConfigureAwait(true);
                _tray?.UpdateStatus(AgentConnectionState.Connected, LastDisplayName);
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                var seconds = Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));
                logger.LogWarning(ex,
                    "Initial hub connect failed (attempt {Attempt}); retrying in {Seconds:0}s",
                    attempt, seconds);
                _tray?.UpdateStatus(AgentConnectionState.Disconnected, LastDisplayName);
                await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Dev-only path: skip host/WAM/hub bootstrap and just render the join
    /// toast against a synthetic payload, then exit after the countdown so
    /// scripts/dev/verify-toast.ps1 can screenshot it. See Program.cs.
    /// </summary>
    private void RunToastSelfTest()
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var logDir = AgentLogPaths.LocalAppDataLogDirectory();
        Directory.CreateDirectory(logDir);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(logDir, "focusagent-toasttest-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 3,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        _testLoggerFactory = LoggerFactory.Create(b => b.AddSerilog(serilog, dispose: true));
        var log = _testLoggerFactory.CreateLogger<App>();
        log.LogInformation("--show-test-toast: starting toast self-test");

        var ui = new WinUiSessionUiHost(dispatcher, _testLoggerFactory.CreateLogger<WinUiSessionUiHost>());
        var payload = new SessionStartedPayload(
            SessionId: Guid.Parse("00000000-0000-0000-0000-000000000041"),
            ClassId: Guid.NewGuid(),
            Mode: "Strict",
            StartedAt: DateTimeOffset.UtcNow,
            JoinCode: "TOAST41");
        var confirmation = new JoinConfirmation(payload, "Self-Test Teacher", TimeSpan.FromSeconds(5), TimeProvider.System);

        // Kick off the show on the UI thread — ShowJoinConfirmationAsync
        // internally enqueues window creation. The returned Task completes when
        // the 5s countdown elapses; we then exit after a screenshot buffer.
        var showTask = ui.ShowJoinConfirmationAsync(confirmation);
        _ = showTask.ContinueWith(t =>
        {
            log.LogInformation(
                "--show-test-toast: confirmation completed status={Status} decision={Decision}",
                t.Status,
                t.Status == TaskStatus.RanToCompletion ? (object)t.Result : t.Exception?.Message ?? "<none>");
            return Task.Delay(TimeSpan.FromMilliseconds(1500))
                .ContinueWith(_ => dispatcher.TryEnqueue(Exit));
        });
    }

    private static void WriteStartupFailure(Exception ex)
    {
        try
        {
            var dir = AgentLogPaths.LocalAppDataLogDirectory();
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "startup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // last-resort logger; intentionally swallow
        }
    }

    private static IHost BuildHost(DispatcherQueue dispatcher, Func<IntPtr> windowHandleProvider)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

        builder.Services.AddOptions<BackendSettings>()
            .Bind(builder.Configuration.GetSection(BackendSettings.SectionName));
        builder.Services.AddOptions<AuthSettings>()
            .Bind(builder.Configuration.GetSection(AuthSettings.SectionName));
        builder.Services.AddOptions<RealtimeSettings>()
            .Bind(builder.Configuration.GetSection(RealtimeSettings.SectionName));
        builder.Services.AddOptions<SessionSettings>()
            .Bind(builder.Configuration.GetSection(SessionSettings.SectionName));
        builder.Services.AddOptions<DevSettings>()
            .Bind(builder.Configuration.GetSection(DevSettings.SectionName));

        builder.Services.AddSingleton(dispatcher);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<Func<IntPtr>>(_ => windowHandleProvider);
        // Capture the UI thread's SynchronizationContext so the foreground
        // watcher can marshal native callbacks onto it.
        builder.Services.AddSingleton(SynchronizationContext.Current
            ?? new DispatcherQueueSynchronizationContext(dispatcher));

        builder.Services.AddSingleton<IAuthTokenProvider, WamTokenProvider>();
        builder.Services.AddSingleton<ISessionHubConnection, SignalRSessionHubConnection>();
        builder.Services.AddSingleton<ISessionUiHost, WinUiSessionUiHost>();
        builder.Services.AddSingleton<SessionCoordinator>();

        builder.Services.AddSingleton<IAppIdentifier, AppIdentifier>();
        builder.Services.AddSingleton<IForegroundWatcher, ForegroundWatcher>();
        builder.Services.AddSingleton<IFocusEnforcer, FocusEnforcer>();
        builder.Services.AddSingleton<IFocusEventReporter, SignalRFocusEventReporter>();
        builder.Services.AddSingleton<FocusSessionController>();

        var logDir = AgentLogPaths.LocalAppDataLogDirectory();
        Directory.CreateDirectory(logDir);

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(logDir, "focusagent-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(serilog, dispose: true);
        builder.Logging.AddDebug();

        return builder.Build();
    }
}
