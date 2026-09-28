using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace v2rayN.Desktop.Common;

/// <summary>
/// Owns the macOS menu-bar item natively. Avalonia's TrayIcon never reports clicks on
/// macOS and always hands every click to the attached NSMenu, so left-click (show the
/// window) vs right-click (pop the menu) has to be implemented against AppKit directly.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacStatusBar
{
    private const double NSVariableStatusItemLength = -1.0;
    private const nint NSEventTypeRightMouseUp = 4;
    private const nint NSEventModifierFlagControl = 1 << 18;
    private const nint MouseUpMask = (1 << 2) | (1 << 4); // leftMouseUp | rightMouseUp
    private const nint NSControlStateValueOn = 1;
    private const nint NSApplicationActivationPolicyRegular = 0;
    private const double IconSizePt = 18.0;

    private const int TagShowWindow = 1;
    private const int TagToggleSystemProxy = 2;
    private const int TagToggleTun = 3;
    private const int TagUpdateSubscription = 4;
    private const int TagExit = 5;
    private const int TagServerBase = 1000;
    private const int MaxServerItems = 30;

    private static MacStatusBar? _instance;

    private nint _statusItem;
    private nint _button;
    private readonly nint _delegate;
    private readonly nint _imageOn;
    private readonly nint _imageOff;
    private readonly IDisposable _iconSubscription;

    public static void Start()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _instance ??= new MacStatusBar();
            return;
        }

        Dispatcher.UIThread.Post(() => _instance ??= new MacStatusBar());
    }

    public static void Refresh()
    {
        if (_instance is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            _instance.RefreshIcon();
            return;
        }

        Dispatcher.UIThread.Post(() => _instance?.RefreshIcon());
    }

    private MacStatusBar()
    {
        nint cls;
        unsafe
        {
            cls = ObjC.RegisterClass(
                "V2rayNTrayDelegate",
                ("trayAction:", "v@:@", Imp(&TrayAction)),
                ("menuAction:", "v@:@", Imp(&MenuAction)));
        }

        _delegate = ObjC.Retain(ObjC.Init(ObjC.Alloc(cls)));

        ClaimForegroundApp();

        // 36px artwork in an 18pt box == exact 2x on every Apple Silicon display.
        _imageOn = LoadTemplateImage("tray_on.png");
        _imageOff = LoadTemplateImage("tray_off.png");

        CreateStatusItem();

        _iconSubscription = StatusBarViewModel.Instance
            .WhenAnyValue(x => x.BlSystemProxySet)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => RefreshIcon());
    }

    private void CreateStatusItem()
    {
        if (_statusItem != 0)
        {
            return;
        }

        var statusBar = ObjC.Send(ObjC.GetClass("NSStatusBar"), "systemStatusBar");
        _statusItem = ObjC.Retain(ObjC.SendD1(statusBar, "statusItemWithLength:", NSVariableStatusItemLength));
        _button = ObjC.Send(_statusItem, "button");

        ObjC.SendV1(_button, "setTarget:", _delegate);
        ObjC.SendV1(_button, "setAction:", ObjC.Sel("trayAction:"));
        ObjC.SendV1(_button, "sendActionOn:", MouseUpMask);
        RefreshIcon();
    }

    /// <summary>
    /// A process exec'd straight from its bundle instead of through LaunchServices gets no
    /// foreground connection, so AppKit drops both the Dock icon and every NSStatusItem.
    /// Claiming the regular policy makes them show up no matter how the app was started.
    /// </summary>
    private static void ClaimForegroundApp()
    {
        var app = ObjC.Send(ObjC.GetClass("NSApplication"), "sharedApplication");
        ObjC.SendV1(app, "setActivationPolicy:", NSApplicationActivationPolicyRegular);
    }

    private static unsafe IntPtr Imp(delegate* unmanaged<nint, nint, nint, void> fn) => (IntPtr)fn;

    private static nint LoadTemplateImage(string asset)
    {
        var uri = new Uri($"avares://v2rayN/Assets/Mac/{asset}");
        using var stream = AssetLoader.Open(uri);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ObjC.NewImage(memory.ToArray(), IconSizePt, IconSizePt, template: true);
    }

    private void RefreshIcon()
    {
        if (_button == 0)
        {
            return;
        }

        var on = StatusBarViewModel.Instance.BlSystemProxySet;
        ObjC.SendV1(_button, "setImage:", on ? _imageOn : _imageOff);
    }

    #region Native callbacks

    [UnmanagedCallersOnly]
    private static void TrayAction(nint self, nint selector, nint sender)
    {
        try
        {
            _instance?.HandleTrayAction();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MacStatusBar.TrayAction", ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void MenuAction(nint self, nint selector, nint sender)
    {
        try
        {
            var tag = (int)ObjC.Send(sender, "tag");
            _instance?.HandleMenuAction(tag);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MacStatusBar.MenuAction", ex);
        }
    }

    #endregion Native callbacks

    private static bool IsRightClick()
    {
        var app = ObjC.Send(ObjC.GetClass("NSApplication"), "sharedApplication");
        var currentEvent = ObjC.Send(app, "currentEvent");
        if (currentEvent == 0)
        {
            return false;
        }

        var type = ObjC.Send(currentEvent, "type");
        var flags = ObjC.Send(currentEvent, "modifierFlags");
        return type == NSEventTypeRightMouseUp || (flags & NSEventModifierFlagControl) != 0;
    }

    private void HandleTrayAction()
    {
        if (IsRightClick())
        {
            ShowMenu();
        }
        else
        {
            StatusBarViewModel.Instance.ShowHideWindowRequested.Publish(true);
        }
    }

    private void ShowMenu()
    {
        var vm = StatusBarViewModel.Instance;
        var menu = ObjC.New("NSMenu");
        ObjC.SendBool(menu, "setAutoenablesItems:", false);

        AddItem(menu, ResUI.MacTrayShowWindow, TagShowWindow);
        AddSeparator(menu);
        AddItem(menu, $"{ResUI.MacTraySystemProxy}: {OnOff(vm.BlSystemProxySet)}", TagToggleSystemProxy, vm.BlSystemProxySet);
        AddItem(menu, $"{ResUI.MacTrayTunMode}: {OnOff(vm.EnableTun)}", TagToggleTun, vm.EnableTun);
        AddServersSubmenu(menu, vm);
        AddSeparator(menu);
        AddItem(menu, ResUI.MacTrayUpdateSubscription, TagUpdateSubscription);
        AddSeparator(menu);
        AddItem(menu, ResUI.MacTrayExit, TagExit);

        var app = ObjC.Send(ObjC.GetClass("NSApplication"), "sharedApplication");
        var currentEvent = ObjC.Send(app, "currentEvent");
        ObjC.SendV3(ObjC.GetClass("NSMenu"), "popUpContextMenu:withEvent:forView:", menu, currentEvent, _button);
        ObjC.SendV(menu, "autorelease");
    }

    private static string OnOff(bool on) => on ? ResUI.MacTrayOn : ResUI.MacTrayOff;

    private static void AddItem(nint menu, string title, int tag, bool isChecked = false)
    {
        var item = ObjC.New("NSMenuItem");
        ObjC.SendV1(item, "setTitle:", ObjC.NSString(title));
        ObjC.SendV1(item, "setTag:", tag);
        ObjC.SendV1(item, "setTarget:", _instance!._delegate);
        ObjC.SendV1(item, "setAction:", ObjC.Sel("menuAction:"));
        if (isChecked)
        {
            ObjC.SendV1(item, "setState:", NSControlStateValueOn);
        }

        ObjC.SendV1(menu, "addItem:", item);
        ObjC.SendV(item, "autorelease");
    }

    private static void AddSeparator(nint menu)
    {
        ObjC.SendV1(menu, "addItem:", ObjC.Send(ObjC.GetClass("NSMenuItem"), "separatorItem"));
    }

    private static void AddServersSubmenu(nint menu, StatusBarViewModel vm)
    {
        if (vm.BlServers == false || vm.Servers.Count == 0)
        {
            return;
        }

        var submenu = ObjC.New("NSMenu");
        var currentId = AppManager.Instance.Config.IndexId;
        var index = 0;
        foreach (var server in vm.Servers.Take(MaxServerItems))
        {
            AddItem(submenu, server.Text, TagServerBase + index, server.ID == currentId);
            index++;
        }

        var parent = ObjC.New("NSMenuItem");
        ObjC.SendV1(parent, "setTitle:", ObjC.NSString(ResUI.MacTrayServers));
        ObjC.SendV1(parent, "setSubmenu:", submenu);
        ObjC.SendV1(menu, "addItem:", parent);
        ObjC.SendV(parent, "autorelease");
        ObjC.SendV(submenu, "autorelease");
    }

    private void HandleMenuAction(int tag)
    {
        var vm = StatusBarViewModel.Instance;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                switch (tag)
                {
                    case TagShowWindow:
                        vm.ShowHideWindowRequested.Publish(true);
                        break;

                    case TagToggleSystemProxy:
                        AppEvents.SysProxyChangeRequested.Publish(
                            vm.BlSystemProxySet ? ESysProxyType.ForcedClear : ESysProxyType.ForcedChange);
                        break;

                    case TagToggleTun:
                        vm.EnableTun = !vm.EnableTun;
                        break;

                    case TagUpdateSubscription:
                        await vm.SubUpdateCmd.Execute(RxVoid.Default).ToTask();
                        break;

                    case TagExit:
                        await AppManager.Instance.AppExitAsync(false);
                        AppManager.Instance.Shutdown(true);
                        break;

                    default:
                        if (tag >= TagServerBase)
                        {
                            var server = vm.Servers.ElementAtOrDefault(tag - TagServerBase);
                            if (server != null)
                            {
                                vm.SetDefaultServerRequested.Publish(server.ID);
                            }
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog("MacStatusBar.HandleMenuAction", ex);
            }
        });
    }
}
