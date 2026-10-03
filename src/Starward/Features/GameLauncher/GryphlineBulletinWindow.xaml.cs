using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 终末地的游戏内公告：直接嵌入游戏里打开的那个官方网页，与 <see cref="GameNoticeWindow"/> 对米哈游的做法相同。
/// <para/>
/// 网页在游戏里靠 webviewsdk 的桥接与游戏沟通（关闭、开外部链接、记已读）。网页按 User-Agent 判断平台，
/// 认不出来时会改用自己的 localStorage 记已读，但关闭与开链接就失效了。这里在网页脚本执行前
/// 放好 <c>window.WVSDK.platform = "Android"</c> 和一个 <c>window.__JSBridge__</c>，
/// 让网页走安卓那一路，把消息转给本窗口；已读仍照网页自己的格式记在 localStorage，并同步给启动页的红点。
/// <para/>
/// 官方网页打不开时调用 <see cref="Fallback"/>，改用自己画的 <see cref="VendorNoticeWindow"/>。
/// </summary>
public sealed partial class GryphlineBulletinWindow : WindowEx
{


    private readonly ILogger<GryphlineBulletinWindow> _logger = AppConfig.GetLogger<GryphlineBulletinWindow>();


    private readonly GameNoticeProviderRegistry _registry = AppConfig.GetService<GameNoticeProviderRegistry>();


    public GameKey CurrentGameKey { get; set; }


    public nint ParentWindowHandle { get; set; }


    /// <summary>
    /// 官方网页打不开时调用，由打开本窗口的一方决定怎么退
    /// </summary>
    public Action? Fallback { get; set; }


    /// <summary>
    /// 网页载入超时就当作打不开
    /// </summary>
    private readonly DispatcherQueueTimerHolder _loadTimeout = new();


    private bool _ready;


    private bool _fellBack;


    private GameNoticeBoard? _board;


    private HashSet<string> _readIds = [];



    /// <summary>
    /// 只有终末地有这个官方网页
    /// </summary>
    public static bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;



    public GryphlineBulletinWindow()
    {
        this.InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();
        InitializeWindow();
        Closed += (_, _) =>
        {
            _loadTimeout.Stop();
            // 退回另一个公告窗口时，红点留给那个窗口关闭时再刷新
            if (!_fellBack)
            {
                WeakReferenceMessenger.Default.Send(new GameNoticeWindowClosedMessage());
            }
        };
    }



    private void InitializeWindow()
    {
        try
        {
            Title = "Starward";
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.TitleBar.SetDragRectangles([new RectInt32(0, 0, 0, 0)]);
            AdaptTitleBarButtonColorToActuallTheme();
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE, User32.GetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_STYLE) & ~(nint)User32.WindowStyles.WS_DLGFRAME);
        }
        catch { }
    }



    /// <summary>
    /// 盖在启动器窗口上，与 <see cref="GameNoticeWindow.Activate"/> 一致
    /// </summary>
    public new void Activate()
    {
        try
        {
            AppWindow? appWindow = AppWindow.GetFromWindowId(new WindowId((ulong)ParentWindowHandle));
            if (appWindow is null)
            {
                Close();
                return;
            }
            var pos = appWindow.Position;
            var size = appWindow.Size;
            int edge = (int)(8 * UIScale);
            AppWindow.MoveAndResize(new RectInt32(pos.X + edge, pos.Y, size.Width - 2 * edge, size.Height - edge));
            User32.SetWindowLong(WindowHandle, User32.WindowLongFlags.GWL_HWNDPARENT, ParentWindowHandle);
        }
        catch { }
        base.Activate();
    }



    protected override nint InputSiteSubclassProc(HWND hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwRefData)
    {
        if (uMsg == (uint)User32.WindowMessage.WM_KEYDOWN && (VirtualKey)wParam is VirtualKey.Escape)
        {
            Close();
            return 0;
        }
        return base.InputSiteSubclassProc(hWnd, uMsg, wParam, lParam, uIdSubclass, dwRefData);
    }



    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (ParentWindowHandle is 0)
        {
            Close();
            return;
        }
        _loadTimeout.Start(DispatcherQueue, TimeSpan.FromSeconds(15), () => FallBack("timeout"));
        _readIds = GameNoticeProviderRegistry.GetReadIds(CurrentGameKey);
        _ = LoadBoardForReadSyncAsync();
        await InitializeWebViewAsync();
    }



    /// <summary>
    /// 已读要同步给启动页的红点，记的时候按公告板修剪，所以先把公告板取来。取不到不影响网页。
    /// </summary>
    private async Task LoadBoardForReadSyncAsync()
    {
        try
        {
            _board = await _registry.GetBoardAsync(CurrentGameKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load Endfield bulletin board for read sync");
        }
    }



    private async Task InitializeWebViewAsync()
    {
        try
        {
            await WebView_Bulletin.EnsureCoreWebView2Async();
            CoreWebView2 core = WebView_Bulletin.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.ProcessFailed += (_, _) => FallBack("process failed");
            core.NavigationStarting += CoreWebView2_NavigationStarting;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
            string language = GryphlineLauncherClient.GetLanguageCode();
            string server = GryphlineLauncherClient.GetBulletinServer(DateTimeOffset.Now.Offset);
            core.Navigate(GryphlineLauncherClient.GetBulletinPageUrl(language, server));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize Endfield bulletin page");
            FallBack("initialize failed");
        }
    }



    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // 公告页是单页应用，换分页不会离开这一页；要离开的一律交给浏览器
        if (!args.Uri.StartsWith(GryphlineLauncherClient.BULLETIN_PAGE, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            _ = OpenInBrowserAsync(args.Uri);
        }
    }


    private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            FallBack($"navigation failed: {args.WebErrorStatus}");
            return;
        }
        // 网页正常时会自己送 ready；万一没送，载完两秒后也显示出来
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(2000);
            ShowPage();
        });
    }


    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        _ = OpenInBrowserAsync(args.Uri);
    }



    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(args.WebMessageAsJson);
            switch (node?["action"]?.ToString())
            {
                case "ready":
                    ShowPage();
                    break;
                case "close":
                    Close();
                    break;
                case "url":
                    _ = OpenInBrowserAsync(node["url"]?.ToString());
                    break;
                case "read":
                    MarkAsRead(node["ids"]?.AsArray());
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Endfield bulletin web message");
        }
    }



    private void ShowPage()
    {
        if (_ready || _fellBack)
        {
            return;
        }
        _ready = true;
        _loadTimeout.Stop();
        WebView_Bulletin.Visibility = Visibility.Visible;
    }



    /// <summary>
    /// 网页记下的已读同步给启动页的红点，公告 ID 与 <see cref="GryphlineNoticeMapper"/> 用的 cid 相同
    /// </summary>
    private void MarkAsRead(JsonArray? ids)
    {
        if (ids is null || _board is null)
        {
            return;
        }
        bool changed = false;
        foreach (JsonNode? id in ids)
        {
            if (id?.ToString() is string cid && !string.IsNullOrWhiteSpace(cid))
            {
                changed |= _readIds.Add(cid);
            }
        }
        if (changed)
        {
            GameNoticeProviderRegistry.SaveReadIds(CurrentGameKey, _readIds, _board);
        }
    }



    private void FallBack(string reason)
    {
        if (_ready || _fellBack)
        {
            return;
        }
        _fellBack = true;
        _loadTimeout.Stop();
        _logger.LogWarning("Endfield bulletin page unavailable ({reason}), fall back to the built-in board.", reason);
        Action? fallback = Fallback;
        Close();
        fallback?.Invoke();
    }



    private async Task OpenInBrowserAsync(string? url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            {
                await Launcher.LaunchUriAsync(uri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open bulletin link {url}", url);
        }
    }



    private new void Close()
    {
        AppWindow?.Hide();
        base.Close();
    }



    /// <summary>
    /// 在网页脚本之前执行。网页只在 <c>window.WVSDK</c> 不存在时才自己判断平台，
    /// 所以先放一个平台为安卓的，网页就会把消息交给 <c>window.__JSBridge__.postMessage</c>。
    /// 需要回传值的消息带着 <c>window.WVSDK.callback.{id}</c>，按游戏的做法把 JSON 字符串传给它。
    /// 已读的格式照抄网页认不出平台时自己记的那一份（键 <c>endfield-bulletin-read-history</c>）。
    /// </summary>
    private const string BridgeScript = """
        (() => {
            if (window.top !== window) return;
            const KEY = 'endfield-bulletin-read-history';
            const post = (message) => window.chrome.webview.postMessage(message);
            const now = () => Math.floor(Date.now() / 1000);
            const load = () => { try { return JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { return {}; } };
            const save = (history) => { try { localStorage.setItem(KEY, JSON.stringify(history)); } catch (e) { } };
            const reply = (callback, value) => {
                if (!callback) return;
                const id = callback.split('.').pop();
                setTimeout(() => {
                    const fn = window.WVSDK && window.WVSDK.callback && window.WVSDK.callback[id];
                    if (typeof fn === 'function') fn(JSON.stringify(value));
                }, 0);
            };
            window.WVSDK = { ENV: {}, callback: {}, API: {}, platform: 'Android' };
            window.__JSBridge__ = {
                postMessage(json) {
                    let message;
                    try { message = JSON.parse(json); } catch (e) { return; }
                    const data = message.data || {};
                    switch (message.type) {
                        case 'ready':
                            post({ action: 'ready' });
                            break;
                        case 'close':
                            post({ action: 'close' });
                            break;
                        case 'openExternalBrowser':
                        case 'openMiniWebView':
                            post({ action: 'url', url: data.url });
                            reply(message.callback, {});
                            break;
                        case 'syncInfo': {
                            const history = load();
                            const read = [];
                            for (const item of data.onlineList || []) {
                                const seen = history[item.cid];
                                if (!seen) continue;
                                seen.lastSeen = now();
                                if (!(item.version > seen.version)) read.push(item.cid);
                            }
                            save(history);
                            reply(message.callback, { list: read });
                            post({ action: 'read', ids: read });
                            break;
                        }
                        case 'readBulletin': {
                            const history = load();
                            history[data.cid] = { lastSeen: now(), version: data.version };
                            save(history);
                            post({ action: 'read', ids: [String(data.cid)] });
                            reply(message.callback, {});
                            break;
                        }
                        default:
                            reply(message.callback, {});
                            break;
                    }
                }
            };
        })();
        """;


}



/// <summary>
/// 一次性的 DispatcherQueue 计时器，到时在界面线程上执行
/// </summary>
internal sealed class DispatcherQueueTimerHolder
{

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;


    public void Start(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action action)
    {
        Stop();
        _timer = queue.CreateTimer();
        _timer.Interval = interval;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => action();
        _timer.Start();
    }


    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

}
