namespace ServiceLib.ViewModels;

public partial class DashboardViewModel : MyReactiveObject
{
    private static readonly Lazy<DashboardViewModel> _instance = new(() => new());

    public static DashboardViewModel Instance => _instance.Value;

    /// <summary>Samples kept for the traffic chart (one per statistics tick, ~1s).</summary>
    public const int HistoryCapacity = 60;

    private readonly List<double> _upHistory = [];
    private readonly List<double> _downHistory = [];

    public StatusBarViewModel StatusBar => StatusBarViewModel.Instance;

    public IReadOnlyList<double> UpHistory => _upHistory;

    public IReadOnlyList<double> DownHistory => _downHistory;

    [Reactive]
    public partial bool SystemProxyOn { get; set; }

    [Reactive]
    public partial bool TunOn { get; set; }

    [Reactive]
    public partial bool CoreRunning { get; set; }

    [Reactive]
    public partial string CurrentServer { get; set; }

    [Reactive]
    public partial string ServerName { get; set; }

    [Reactive]
    public partial string ServerType { get; set; }

    [Reactive]
    public partial string ServerEndpoint { get; set; }

    [Reactive]
    public partial int LatencyMs { get; set; }

    [Reactive]
    public partial bool IsTesting { get; set; }

    [Reactive]
    public partial string LatencyDisplay { get; set; }

    [Reactive]
    public partial string UpSpeedDisplay { get; set; }

    [Reactive]
    public partial string DownSpeedDisplay { get; set; }

    [Reactive]
    public partial bool SpeedEnabled { get; set; }

    /// <summary>Bumped after every traffic sample so views can redraw the chart.</summary>
    [Reactive]
    public partial int HistoryVersion { get; set; }

    [Reactive]
    public partial string LocalPortDisplay { get; set; }

    [Reactive]
    public partial string CoreTypeDisplay { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> TestLatencyCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> EnableSpeedCmd { get; }

    public DashboardViewModel()
    {
        _config = AppManager.Instance.Config;

        CurrentServer = string.Empty;
        ServerName = string.Empty;
        ServerType = string.Empty;
        ServerEndpoint = string.Empty;
        LatencyDisplay = "-";
        UpSpeedDisplay = FormatRate(0);
        DownSpeedDisplay = FormatRate(0);
        CoreTypeDisplay = Global.None;
        SpeedEnabled = _config.GuiItem.DisplayRealTimeSpeed || _config.GuiItem.EnableStatistics;

        var statusBar = StatusBarViewModel.Instance;

        SystemProxyOn = statusBar.BlSystemProxySet;
        TunOn = statusBar.EnableTun;
        CurrentServer = statusBar.RunningServerDisplay;
        RefreshLocalPort();
        RefreshLatency();

        this.WhenAnyValue(x => x.TunOn)
            .Skip(1)
            .Subscribe(on => statusBar.EnableTun = on);

        statusBar.WhenAnyValue(x => x.BlSystemProxySet)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(on => SystemProxyOn = on);

        statusBar.WhenAnyValue(x => x.EnableTun)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(on => TunOn = on);

        statusBar.WhenAnyValue(x => x.RunningServerDisplay)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async text =>
            {
                CurrentServer = text;
                await RefreshServerAsync();
                RefreshLocalPort();
                RefreshLatency();
            });

        AppEvents.CoreStateChanged
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(running =>
            {
                CoreRunning = running;
                CoreTypeDisplay = running ? AppManager.Instance.RunningCoreType.ToString().Replace("_", "-") : Global.None;
                if (!running)
                {
                    ClearHistory();
                }
            });

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(update => AddSample(update.ProxyUp, update.ProxyDown));

        TestLatencyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            IsTesting = true;
            try
            {
                var result = await statusBar.TestServerAvailability();
                if (result is { Time: <= 0 })
                {
                    // Keep the table in sync: a failed test should not leave an old, good-looking delay behind.
                    ProfileExManager.Instance.SetTestDelay(_config.IndexId, -1);
                }
            }
            finally
            {
                IsTesting = false;
                RefreshLatency();
            }
        });

        ReloadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            statusBar.ReloadRequested.Publish();
            await Task.CompletedTask;
        });

        EnableSpeedCmd = ReactiveCommand.CreateFromTask(EnableSpeedAsync);
    }

    /// <summary>
    /// Only direct user intent may request a proxy change; mirroring BlSystemProxySet back
    /// into SystemProxyOn must stay silent or the two ends ping-pong forever.
    /// </summary>
    public void ToggleSystemProxy()
    {
        AppEvents.SysProxyChangeRequested.Publish(SystemProxyOn ? ESysProxyType.ForcedClear : ESysProxyType.ForcedChange);
    }

    /// <summary>
    /// Turns on the lightweight speed feed without an app restart. sing-box always exposes the
    /// clash API, so only Xray needs a core reload to pick up its stats inbound.
    /// </summary>
    private async Task EnableSpeedAsync()
    {
        if (SpeedEnabled)
        {
            return;
        }

        _config.GuiItem.DisplayRealTimeSpeed = true;
        await ConfigHandler.SaveConfig(_config);
        await StatisticsManager.Instance.Init(_config, update =>
        {
            if (AppManager.Instance.ShowInTaskbar)
            {
                AppEvents.DispatcherStatisticsRequested.Publish(update);
            }
            return Task.CompletedTask;
        });
        SpeedEnabled = true;

        if (!AppManager.Instance.IsRunningCore(ECoreType.sing_box))
        {
            StatusBar.ReloadRequested.Publish();
        }
    }

    private void AddSample(long upKb, long downKb)
    {
        UpSpeedDisplay = FormatRate(upKb);
        DownSpeedDisplay = FormatRate(downKb);

        Push(_upHistory, upKb);
        Push(_downHistory, downKb);
        HistoryVersion++;
    }

    private void ClearHistory()
    {
        _upHistory.Clear();
        _downHistory.Clear();
        UpSpeedDisplay = FormatRate(0);
        DownSpeedDisplay = FormatRate(0);
        HistoryVersion++;
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(Math.Max(0, value));
        if (list.Count > HistoryCapacity)
        {
            list.RemoveAt(0);
        }
    }

    /// <summary>Statistics services report KB per second.</summary>
    private static string FormatRate(long kb)
    {
        if (kb <= 0)
        {
            return "0 KB/s";
        }
        return kb < 1024 ? $"{kb} KB/s" : $"{Utils.HumanFy(kb)}/s";
    }

    private async Task RefreshServerAsync()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            ServerName = ResUI.MacDashNoServer;
            ServerType = string.Empty;
            ServerEndpoint = string.Empty;
            return;
        }

        ServerName = item.Remarks.IsNullOrEmpty() ? item.Address : item.Remarks;
        ServerType = item.ConfigType.ToString();
        ServerEndpoint = item.IsComplex() || item.Address.IsNullOrEmpty() ? string.Empty : $"{MaskAddress(item.Address)}:{item.Port}";
    }

    /// <summary>Same masking as <see cref="ProfileItem.GetSummary"/>, so screenshots don't leak hosts.</summary>
    private static string MaskAddress(string address)
    {
        var parts = address.Contains(':') ? address.Split(':') : address.Split('.');
        return parts.Length > 2 ? $"{parts.First()}***{parts.Last()}" : address;
    }

    private void RefreshLocalPort()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        LocalPortDisplay = _config.Inbound.First().SecondLocalPortEnabled
            ? $"127.0.0.1:{port}, {AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}"
            : $"127.0.0.1:{port}";
    }

    private void RefreshLatency()
    {
        var delay = ProfileExManager.Instance.GetTestDelay(_config.IndexId);
        LatencyMs = delay;
        LatencyDisplay = delay switch
        {
            > 0 => $"{delay} ms",
            < 0 => ResUI.MacDashTimeout,
            _ => ResUI.MacDashNotTested,
        };
    }
}
