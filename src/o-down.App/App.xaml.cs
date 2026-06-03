using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using o_down.Core.Abstractions;
using o_down.Core.Pipeline;
using o_down.Data;
using o_down.Engines.Aria2;
using o_down.Engines.Media;
using o_down.Engines.Torrent;
using o_down.Infrastructure;
using o_down.Update;
using o_down.App.Services;
using o_down.App.ViewModels;
using o_down.App.Views;
using Serilog;

namespace o_down.App;

public partial class App : Application
{
    private IHost? _host;
    public static IServiceProvider Services => ((App)Current)._host!.Services;
    public static MainWindow? MainWindowRef { get; private set; }

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var sidecars = new SidecarManager();
        Directory.CreateDirectory(sidecars.LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(sidecars.LogDirectory, "odown-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var preGuard = new SingleInstanceGuard("Local\\o-down-singleton");
        if (!preGuard.TryAcquire())
        {
            Log.Information("Another instance is already running; sending focus signal");
            try
            {
                await SingleInstanceGuard.SendFocusMessageAsync("o-down-focus", string.Join(" ", Environment.GetCommandLineArgs()), TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to send focus signal");
            }
            preGuard.Dispose();
            Environment.Exit(0);
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(s => s
                .AddLogging(b => b.AddSerilog(dispose: true))
                .AddSingleton<ISidecarManager>(sidecars)
                .AddSingleton<IConsentStore>(sp => new FileConsentStore(sidecars.DataDirectory))
                .AddSingleton<IClipboardMonitor, WindowsClipboardMonitor>()
                .AddSingleton<ILinkGraber, NamedPipeLinkServer>()
                .AddSingleton<NamedPipeLinkServer>(sp => (NamedPipeLinkServer)sp.GetRequiredService<ILinkGraber>())
                .AddSingleton<IDownloadQueue, InMemoryDownloadQueue>()
                .AddSingleton<IScheduler, CronScheduler>()
                .AddSingleton<IDownloadRouter, DownloadRouter>()
                .AddSingleton<OdownDbContext>(sp => new OdownDbContext(new DbContextOptionsBuilder<OdownDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(sidecars.DataDirectory, "odown.db")}")
                    .Options))
                .AddSingleton<OdownDbInitializer>()
                .AddSingleton(sp => new Aria2HostProcess(
                    sidecars.Aria2Executable,
                    Path.Combine(sidecars.DataDirectory, "downloads"),
                    Path.Combine(sidecars.DataDirectory, "aria2"),
                    port: 6800,
                    secret: "odown"))
                .AddSingleton<Aria2DownloadEngine>()
                .AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<Aria2DownloadEngine>())
                .AddSingleton<ITorrentEngine>(sp => new TorrentDownloadEngine(
                    Path.Combine(sidecars.DataDirectory, "torrents.cache"),
                    sp.GetRequiredService<ILogger<o_down.Engines.Torrent.TorrentDownloadEngine>>()))
                .AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<ITorrentEngine>())
                .AddSingleton<IMediaExtractor>(sp => new YtDlpMediaExtractor(sidecars.YtDlpExecutable, sidecars.FfmpegExecutable))
                .AddSingleton<FfmpegTranscoder>(sp => new FfmpegTranscoder(sidecars.FfmpegExecutable, sp.GetRequiredService<ILogger<FfmpegTranscoder>>()))
                .AddSingleton<MediaDownloadEngine>(sp => new MediaDownloadEngine(
                    sp.GetRequiredService<IMediaExtractor>(),
                    sp.GetRequiredService<ILogger<MediaDownloadEngine>>()))
                .AddSingleton<IDownloadEngine>(sp => sp.GetRequiredService<MediaDownloadEngine>())
                .AddSingleton<AutoSortEngine>(sp =>
                {
                    using var scope = sp.CreateScope();
                    var db = sp.GetRequiredService<OdownDbContext>();
                    var rules = db.CategoryRules.ToList();
                    return new AutoSortEngine(rules);
                })
                .AddSingleton<LinkIngestService>()
                .AddSingleton<DownloadOrchestrator>()
                .AddSingleton<IUpdateFeed>(sp => new HttpUpdateFeed(new HttpClient()))
                .AddSingleton(sp => new UpdateService(
                    sp.GetRequiredService<IUpdateFeed>(),
                    AppContext.BaseDirectory,
                    new Version(0, 1, 0),
                    http: null,
                    sp.GetRequiredService<ILogger<o_down.Update.UpdateService>>()))
                .AddSingleton(sp => new UpdateCheckScheduler(
                    sp.GetRequiredService<UpdateService>(),
                    sp.GetRequiredService<IAppSettingsStore>(),
                    TimeSpan.FromHours(6),
                    sp.GetRequiredService<ILogger<UpdateCheckScheduler>>()))
                .AddSingleton<SingleInstanceGuard>(_ => new SingleInstanceGuard("Local\\o-down-singleton"))
                .AddSingleton<IAppSettingsStore>(sp => new JsonAppSettingsStore(
                    Path.Combine(sidecars.DataDirectory, "settings.json")))
                .AddSingleton<INavigationService, NavigationService>()
                .AddSingleton<TrayIconService>()
                .AddTransient<DownloadsViewModel>()
                .AddTransient<TorrentsViewModel>()
                .AddTransient<QueueViewModel>()
                .AddTransient<HistoryViewModel>()
                .AddTransient<SitesViewModel>()
                .AddTransient<SettingsViewModel>())
            .Build();

        await _host.StartAsync().ConfigureAwait(false);

        // Initialize sidecars and engines
        await sidecars.EnsureExtractedAsync().ConfigureAwait(false);
        var aria2 = _host.Services.GetRequiredService<Aria2DownloadEngine>();
        if (sidecars.AreAllPresent() || File.Exists(sidecars.Aria2Executable))
        {
            try { await aria2.InitializeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "aria2 init failed"); }
        }
        try
        {
            var torrent = _host.Services.GetRequiredService<ITorrentEngine>();
            await torrent.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warning(ex, "torrent init failed"); }

        // Initialize DB
        await _host.Services.GetRequiredService<OdownDbInitializer>().InitializeAsync().ConfigureAwait(false);

        // Wire clipboard + link graber
        var graber = (NamedPipeLinkServer)_host.Services.GetRequiredService<ILinkGraber>();
        var ingestSvc = _host.Services.GetRequiredService<LinkIngestService>();
        graber.SetResponder(async (link, ct) =>
        {
            try
            {
                var item = await ingestSvc.IngestAsync(link.Url, link.Source, link.Referrer, link.Cookies, link.FilenameHint, ct).ConfigureAwait(false);
                return new o_down.Core.Protocol.NativeMessageCodec.NativeResponse { Ok = true, DownloadId = item.Id };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Pipe ingest failed for {Url}", link.Url);
                return new o_down.Core.Protocol.NativeMessageCodec.NativeResponse { Ok = false, Error = ex.Message };
            }
        });
        graber.Start();
        graber.LinkCaptured += OnLinkCaptured;
        var clipboard = _host.Services.GetRequiredService<IClipboardMonitor>();
        var consent = _host.Services.GetRequiredService<IConsentStore>();
        clipboard.TextCaptured += async (_, text) => await HandleCapturedLinkAsync(text, "clipboard").ConfigureAwait(false);
        if (consent.IsEnabled(FileConsentStore.ClipboardFeature))
        {
            try { await clipboard.StartAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warning(ex, "Clipboard monitor failed to start"); }
        }
        else
        {
            Log.Information("Clipboard monitoring disabled (no consent). Enable it in Settings.");
        }

        // Tray icon
        try { _host.Services.GetRequiredService<TrayIconService>().Show(); }
        catch (Exception ex) { Log.Warning(ex, "Tray icon failed to show"); }

        // Orchestrator loop
        _ = _host.Services.GetRequiredService<DownloadOrchestrator>().RunAsync();

        // Background update check
        try
        {
            var scheduler = _host.Services.GetRequiredService<UpdateCheckScheduler>();
            scheduler.UpdateAvailable += (_, result) =>
                Log.Information("Update available: {Version} (current {Current})", result.Manifest?.Version, result.CurrentVersion);
            scheduler.Start();
        }
        catch (Exception ex) { Log.Warning(ex, "Update scheduler failed to start"); }

        MainWindowRef = new MainWindow();
        MainWindowRef.Activate();
    }

    private void OnLinkCaptured(object? sender, Core.Abstractions.CapturedLink link)
    {
        _ = HandleCapturedLinkAsync(link.Url, link.Source, link.Referrer, link.Cookies, link.FilenameHint);
    }

    private async Task HandleCapturedLinkAsync(string url, string source, string? referrer = null, string? cookies = null, string? filenameHint = null)
    {
        try
        {
            var ingest = _host!.Services.GetRequiredService<LinkIngestService>();
            await ingest.IngestAsync(url, source, referrer, cookies, filenameHint).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Warning(ex, "Ingest failed for {Url}", url); }
    }
}
