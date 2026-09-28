using System.Runtime.Versioning;
using Avalonia.Controls.Notifications;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

[SupportedOSPlatform("macos")]
public partial class MacMainWindow : WindowBase<MainWindowViewModel>, IMainWindowView
{
    private static Config _config;
    private readonly WindowNotificationManager? _manager;
    private readonly Flyout _themeFlyout = new();
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private bool _blCloseByUser = false;

    public MacMainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        menuAbout.Header = Utils.GetVersion();
        pageDashboard.Content = new DashboardView();
        SwitchPage(0);
        btnStatus.Click += (_, _) => navList.SelectedIndex = 0;

        KeyDown += MainWindow_KeyDown;
        navList.SelectionChanged += (_, _) => SwitchPage(navList.SelectedIndex);

        btnSubscription.Click += (_, _) => ViewModel?.SubSettingCmd.Execute(RxVoid.Default).Subscribe();
        btnRouting.Click += (_, _) => ViewModel?.RoutingSettingCmd.Execute(RxVoid.Default).Subscribe();
        btnSettings.Click += (_, _) => ViewModel?.OptionSettingCmd.Execute(RxVoid.Default).Subscribe();
        btnTheme.Click += (_, _) =>
        {
            _themeFlyout.Content ??= new ThemeSettingView();
            _themeFlyout.ShowAt(btnTheme);
        };

        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        menuClose.Click += MenuClose_Click;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.pageServers.Content).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.pageProxies.Content).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.pageConnections.Content).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.pageLogs.Content).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.navProxies.IsVisible).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.navConnections.IsVisible).DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.StatusBarViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                await AvaUtils.SetClipboardData(this, interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.StatusBarViewModel.PasswordInputInteraction.RegisterHandler(async interaction =>
            {
                var result = await PasswordInputAsync();
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.StatusBarViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                MacStatusBar.Refresh();
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            var dashboard = DashboardViewModel.Instance;
            dashboard.WhenAnyValue(x => x.SystemProxyOn, x => x.TunOn, x => x.ServerName, (proxy, tun, server) => (on: proxy || tun, server))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(state =>
                {
                    dotStatus.Classes.Set("on", state.on);
                    txtStatus.Text = state.on ? ResUI.MacDashPowerOn : ResUI.MacSidebarIdle;
                    txtStatusServer.Text = state.server;
                })
                .DisposeWith(disposables);
            dashboard.WhenAnyValue(x => x.UpSpeedDisplay, x => x.DownSpeedDisplay, x => x.SpeedEnabled)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(state =>
                {
                    txtStatusUp.Text = state.Item1;
                    txtStatusDown.Text = state.Item2;
                    spStatusSpeed.IsVisible = state.Item3;
                })
                .DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(async content => await DelegateSnackMsg(content))
                .DisposeWith(disposables);

            AppEvents.AppExitRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => StorageUI())
                .DisposeWith(disposables);

            AppEvents.ShutdownRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(Shutdown)
                .DisposeWith(disposables);
        });

        Title = $"{Utils.GetVersion()}";
    }

    #region Event

    private async Task DelegateSnackMsg(string content)
    {
        _manager?.Show(new Notification(null, content, NotificationType.Information));
        await Task.CompletedTask;
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            if (e.Key == Key.V)
            {
                await AddServerViaClipboardAsync();
            }
        }
        else if (e.Key == Key.F5)
        {
            ViewModel?.Reload();
        }
    }

    private async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task<string?> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            DashboardViewModel.Instance.TunOn = false;
            return password;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return password;
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }

        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    private void SwitchPage(int index)
    {
        pageDashboard.IsVisible = index == 0;
        pageServers.IsVisible = index == 1;
        pageProxies.IsVisible = index == 2;
        pageConnections.IsVisible = index == 3;
        pageLogsHost.IsVisible = index == 4;
        spServerActions.IsVisible = index == 1;

        txtPageTitle.Text = index switch
        {
            1 => ResUI.MacNavServers,
            2 => ResUI.MacNavProxies,
            3 => ResUI.MacNavConnections,
            4 => ResUI.MacNavLogs,
            _ => ResUI.MacNavDashboard,
        };
    }

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Focus();
        }
        else
        {
            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }

            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }

    #endregion UI
}
