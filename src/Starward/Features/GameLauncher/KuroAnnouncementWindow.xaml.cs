using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Kuro;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 鸣潮的游戏内公告：直接嵌入游戏里打开的那个官方网页（3.8 起公告板改成游戏 SDK 内嵌的网页），
/// 与 <see cref="GryphlineBulletinWindow"/> 对终末地的做法相同。
/// <para/>
/// 网页在 PC 上靠全局函数与游戏沟通：网页调用 <c>window.close_webview()</c>、<c>window.new_openPage({url})</c>、
/// <c>window.save_redPoint(未读清单)</c>，游戏调用网页挂上的 <c>window.redPoint(未读清单 JSON)</c>。
/// 这里在网页脚本之前放好这几个函数，把消息转给本窗口。
/// <para/>
/// 已读：网页用 <see cref="KuroLauncherClient.ANNOUNCEMENT_BRIDGE_SERVER_ID"/> 打开时才走上面那条未读清单的路，
/// 但那个服务器标识在 CDN 上没有数据，所以网页去取数据时由本窗口拦下来，改给 <see cref="KuroLauncherClient.GetGameNoticeFeedJsonAsync"/> 那份。
/// <para/>
/// 另外拦下的：游戏 SDK 在本机提供的字体（改从游戏目录读文鼎方新书 H7），以及网页的统计与错误回报（直接回 204，不送出去）。
/// <para/>
/// 官方网页打不开时调用 <see cref="Fallback"/>，改用自己画的 <see cref="KuroNoticeWindow"/>。
/// </summary>
public sealed partial class KuroAnnouncementWindow : WindowEx
{


    /// <summary>
    /// 游戏 SDK 在本机提供字体的地址，网页写死的
    /// </summary>
    private const string FontUrl = "https://127.0.0.1:8971/font/gameFont.ttf";


    /// <summary>
    /// 网页的统计（ThinkingData 与库洛自己的）和错误回报（Sentry），一律不送出去
    /// </summary>
    private static readonly string[] BlockedHosts =
    [
        "mp-gb-sdklog.kurogames.net",
        "mp-cn-sdklog.kurogames.com",
        "qcloud-sg-datareceiver.kurogame.xyz",
        "ali-sh-datareceiver.kurogame.xyz",
        "web-sentry.kurogame.net",
        "web-sentry.kurogame.com",
    ];


    private readonly ILogger<KuroAnnouncementWindow> _logger = AppConfig.GetLogger<KuroAnnouncementWindow>();


    private readonly GameNoticeProviderRegistry _registry = AppConfig.GetService<GameNoticeProviderRegistry>();


    private readonly KuroLauncherClient _client = AppConfig.GetService<KuroLauncherClient>();


    public GameKey CurrentGameKey { get; set; }


    public nint ParentWindowHandle { get; set; }


    /// <summary>
    /// 官方网页打不开时调用，由打开本窗口的一方决定怎么退
    /// </summary>
    public Action? Fallback { get; set; }


    private readonly DispatcherQueueTimerHolder _loadTimeout = new();


    private readonly CancellationTokenSource _closeCts = new();


    private GameNoticeBoard? _board;


    private HashSet<string> _readIds = [];


    private bool _ready;


    private bool _fellBack;



    /// <summary>
    /// 只有国际服有这个网页的数据
    /// </summary>
    public static bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;



    public KuroAnnouncementWindow()
    {
        this.InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();
        InitializeWindow();
        Closed += (_, _) =>
        {
            _loadTimeout.Stop();
            _closeCts.Cancel();
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
        await InitializeWebViewAsync();
    }



    private async Task InitializeWebViewAsync()
    {
        try
        {
            // 网页要的未读清单在网页脚本之前就得备好，所以先取公告板；取不到就别开网页了
            Task<GameNoticeBoard?> boardTask = _registry.GetBoardAsync(CurrentGameKey, _closeCts.Token);
            await WebView_Notice.EnsureCoreWebView2Async();
            _board = await boardTask;
            if (_board is null)
            {
                FallBack("no board");
                return;
            }
            CoreWebView2 core = WebView_Notice.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.ProcessFailed += (_, _) => FallBack("process failed");
            core.AddWebResourceRequestedFilter(FontUrl, CoreWebView2WebResourceContext.All);
            core.AddWebResourceRequestedFilter($"{KuroLauncherClient.AnnouncementFeedPrefix}*", CoreWebView2WebResourceContext.All);
            foreach (string host in BlockedHosts)
            {
                core.AddWebResourceRequestedFilter($"https://{host}/*", CoreWebView2WebResourceContext.All);
            }
            core.WebResourceRequested += CoreWebView2_WebResourceRequested;
            core.NavigationStarting += CoreWebView2_NavigationStarting;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript.Replace("__UNREAD__", GetUnreadJson()));
            core.Navigate(KuroLauncherClient.GetAnnouncementPageUrl(KuroLauncherClient.GetLanguageCode()));
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize WuWa announcement page");
            FallBack("initialize failed");
        }
    }



    /// <summary>
    /// 交给网页的未读清单：该亮红点、还没看过的公告与日历
    /// </summary>
    private string GetUnreadJson()
    {
        var unread = _board?.ReadableEntries.Where(x => x.NeedRedDot && !_readIds.Contains(x.Id)).Select(x => x.Id).ToList() ?? [];
        return JsonSerializer.Serialize(unread);
    }



    private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        string url = args.Request.Uri;
        Windows.Foundation.Deferral? deferral = null;
        try
        {
            var uri = new Uri(url);
            if (BlockedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                args.Response = sender.Environment.CreateWebResourceResponse(null, 204, "No Content", CorsHeaders);
            }
            else if (args.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                args.Response = sender.Environment.CreateWebResourceResponse(null, 204, "No Content", CorsHeaders);
            }
            else if (url.StartsWith(FontUrl, StringComparison.OrdinalIgnoreCase))
            {
                args.Response = GetFontResponse(sender.Environment);
            }
            else if (url.StartsWith(KuroLauncherClient.AnnouncementFeedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                deferral = args.GetDeferral();
                // 路径最后一段是 {语言}.json，后面可能带时间戳参数
                string language = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                string json = await _client.GetGameNoticeFeedJsonAsync(language, _closeCts.Token);
                args.Response = sender.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(json)).AsRandomAccessStream(), 200, "OK",
                    $"Content-Type: application/json; charset=utf-8\r\n{CorsHeaders}");
            }
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Serve WuWa announcement request {url}", url);
            args.Response = sender.Environment.CreateWebResourceResponse(null, 502, "Bad Gateway", CorsHeaders);
            // 公告数据取不到时网页只会显示读取失败，不如退回自己画的那块（它还有旧清单可以兜底）
            if (url.StartsWith(KuroLauncherClient.AnnouncementFeedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                FallBack("feed failed");
            }
        }
        finally
        {
            deferral?.Complete();
        }
    }


    private const string CorsHeaders = "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: *\r\nCache-Control: no-cache";


    /// <summary>
    /// 游戏 SDK 自带的文鼎方新书 H7，从本机的游戏目录读。没装游戏时 404，网页自己会退回系统字体。
    /// 读进内存再交出去，不能把文件流交给 WebView2：要等垃圾回收才会关，期间官方启动器更新时换不掉这个文件。
    /// </summary>
    private CoreWebView2WebResourceResponse GetFontResponse(CoreWebView2Environment environment)
    {
        string? installPath = GameLauncherService.GetGameInstallPath(CurrentGameKey);
        string? file = string.IsNullOrWhiteSpace(installPath) ? null : Path.Combine(installPath, KuroGameMapping.NoticeFontRelativePath);
        if (file is null || !File.Exists(file))
        {
            return environment.CreateWebResourceResponse(null, 404, "Not Found", CorsHeaders);
        }
        byte[] bytes;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        return environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), 200, "OK", $"Content-Type: font/ttf\r\n{CorsHeaders}");
    }



    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // 公告页是单页应用，换分页不会离开这一页；要离开的一律交给浏览器
        if (!args.Uri.StartsWith(KuroLauncherClient.ANNOUNCEMENT_PAGE, StringComparison.OrdinalIgnoreCase))
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
        // 网页载完数据会调 loading_success；万一没调，载完三秒后也显示出来
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(3000);
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
                case "unread":
                    SaveUnread(node["ids"]?.AsArray());
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WuWa announcement web message");
        }
    }



    /// <summary>
    /// 网页交回的是新的未读清单；该亮红点却不在清单里的，就是看过了，同步给启动页的红点
    /// </summary>
    private void SaveUnread(JsonArray? ids)
    {
        if (ids is null || _board is null)
        {
            return;
        }
        var unread = new HashSet<string>(ids.Select(x => x?.ToString() ?? ""), StringComparer.Ordinal);
        bool changed = false;
        foreach (var (id, needRedDot) in _board.ReadableEntries)
        {
            if (needRedDot && !unread.Contains(id))
            {
                changed |= _readIds.Add(id);
            }
        }
        if (changed)
        {
            GameNoticeProviderRegistry.SaveReadIds(CurrentGameKey, _readIds, _board);
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
        Border_Loading.Visibility = Visibility.Collapsed;
        WebView_Notice.Visibility = Visibility.Visible;
    }



    private void FallBack(string reason)
    {
        if (_ready || _fellBack)
        {
            return;
        }
        _fellBack = true;
        _loadTimeout.Stop();
        _logger.LogWarning("WuWa announcement page unavailable ({reason}), fall back to the built-in board.", reason);
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
            _logger.LogWarning(ex, "Open announcement link {url}", url);
        }
    }



    private new void Close()
    {
        AppWindow?.Hide();
        base.Close();
    }



    /// <summary>
    /// 在网页脚本之前执行，扮成游戏 SDK 的 PC 桥接：网页的 jsBridge 在 PC 上把 <c>callHandler(名, 数据)</c>
    /// 直接变成调用 <c>window[名](数据)</c>，<c>registerHandler(名, 函数)</c> 变成 <c>window[名] = 函数</c>。
    /// <list type="bullet">
    /// <item><c>loading_success</c>：数据处理完了。此时网页已经挂好 <c>redPoint</c>，把未读清单（JSON 字符串）交给它</item>
    /// <item><c>save_redPoint(清单)</c>：看过一则后的新未读清单</item>
    /// <item><c>close_webview</c> / <c>new_openPage({url, type})</c>：关闭、开外部链接</item>
    /// </list>
    /// 网页一开始是小窗，日历只在大画面才有，所以等展开钮出现就按一下，开成与游戏里展开后一样的大画面。
    /// </summary>
    private const string BridgeScript = """
        (() => {
            if (window.top !== window) return;
            const post = (message) => window.chrome.webview.postMessage(message);
            const UNREAD = __UNREAD__;
            window.loading_success = () => {
                post({ action: 'ready' });
                setTimeout(() => {
                    if (typeof window.redPoint === 'function') window.redPoint(JSON.stringify(UNREAD));
                }, 0);
            };
            window.get_redPoint = () => { };
            window.save_redPoint = (list) => {
                post({ action: 'unread', ids: Array.isArray(list) ? Array.from(list, String) : [] });
            };
            window.close_webview = () => post({ action: 'close' });
            window.new_openPage = (data) => {
                if (data && data.url) post({ action: 'url', url: String(data.url) });
            };
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape') post({ action: 'close' });
            });
            document.addEventListener('DOMContentLoaded', () => {
                const expand = () => {
                    const button = document.querySelector('.notice-header__expand-btn');
                    if (!button) return false;
                    button.click();
                    return true;
                };
                if (expand()) return;
                const observer = new MutationObserver(() => { if (expand()) observer.disconnect(); });
                observer.observe(document.body, { childList: true, subtree: true });
                setTimeout(() => observer.disconnect(), 15000);
            });
        })();
        """;


}
