namespace v2rayN.Desktop.Views;

public partial class DashboardView : ReactiveUserControl<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();

        DataContext = ViewModel = DashboardViewModel.Instance;

        btnPower.Click += (_, _) => ViewModel.ToggleSystemProxy();

        this.WhenActivated(disposables =>
        {
            ViewModel.WhenAnyValue(x => x.SystemProxyOn, x => x.TunOn, (proxy, tun) => (proxy, tun))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(state =>
                {
                    var on = state.proxy || state.tun;
                    btnPower.Classes.Set("on", state.proxy);
                    dotState.Classes.Set("on", on);
                    txtState.Text = on ? ResUI.MacDashPowerOn : ResUI.MacDashPowerOff;
                    ToolTip.SetTip(btnPower, state.proxy ? ResUI.MacDashPowerHintOn : ResUI.MacDashPowerHintOff);
                })
                .DisposeWith(disposables);

            ViewModel.WhenAnyValue(x => x.CoreRunning, x => x.CoreTypeDisplay, (running, coreType) => (running, coreType))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(state =>
                {
                    dotCore.Classes.Set("on", state.running);
                    txtCore.Text = state.running
                        ? $"{state.coreType} · {ResUI.MacDashCoreRunning}"
                        : ResUI.MacDashCoreStopped;
                })
                .DisposeWith(disposables);

            ViewModel.WhenAnyValue(x => x.LatencyMs)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(ms =>
                {
                    pillLatency.Classes.Set("good", ms is > 0 and <= 300);
                    pillLatency.Classes.Set("warn", ms > 300);
                    pillLatency.Classes.Set("bad", ms < 0);
                })
                .DisposeWith(disposables);

            ViewModel.WhenAnyValue(x => x.IsTesting)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(testing => txtTest.Text = testing ? ResUI.MacDashTesting : ResUI.MacDashTestLatency)
                .DisposeWith(disposables);

            ViewModel.WhenAnyValue(x => x.HistoryVersion)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => chart.SetData(ViewModel.UpHistory, ViewModel.DownHistory, DashboardViewModel.HistoryCapacity))
                .DisposeWith(disposables);
        });
    }
}
