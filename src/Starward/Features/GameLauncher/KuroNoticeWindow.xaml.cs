using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Kuro;
using Starward.Core.Localization;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.Language;
using Starward.Providers.Kuro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 鸣潮的游戏内公告板，照游戏里的样子画：卡片墙、点开后左边标题卡右边正文。
/// <para/>
/// 游戏里那块是引擎原生画的，不是网页，没有官方页面可以嵌。这里用 WebView2 跑一页自己的网页
/// （<c>KuroNotice/</c>），尺寸与颜色照游戏截图量出来；字用游戏 SDK 自带的文鼎方新书 H7，
/// 从本机的游戏目录读，没装游戏时网页退回系统字体。网页、样式、脚本与字体都由本窗口
/// 在 <see cref="Origin"/> 这个假网址下供给，不经网络；公告数据与正文由 <see cref="KuroGameNoticeProvider"/> 取。
/// <para/>
/// 网页打不开时调用 <see cref="Fallback"/>，改用通用的 <see cref="VendorNoticeWindow"/>。
/// </summary>
public sealed partial class KuroNoticeWindow : WindowEx
{


    /// <summary>
    /// 公告板网页所在的假网址，只在本窗口的 WebView2 里由 <see cref="CoreWebView2_WebResourceRequested"/> 应答
    /// </summary>
    private const string Origin = "https://wuwa-notice.starward";


    /// <summary>
    /// 网页可以要的文件，其余一律 404
    /// </summary>
    private static readonly Dictionary<string, (string File, string ContentType)> PageFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/index.html"] = ("index.html", "text/html; charset=utf-8"),
        ["/notice.css"] = ("notice.css", "text/css; charset=utf-8"),
        ["/notice.js"] = ("notice.js", "text/javascript; charset=utf-8"),
    };


    private const string FontPath = "/font/h7.ttf";


    private readonly ILogger<KuroNoticeWindow> _logger = AppConfig.GetLogger<KuroNoticeWindow>();


    private readonly GameNoticeProviderRegistry _registry = AppConfig.GetService<GameNoticeProviderRegistry>();


    private readonly KuroGameNoticeProvider _provider = AppConfig.GetService<KuroGameNoticeProvider>();


    public GameKey CurrentGameKey { get; set; }


    public nint ParentWindowHandle { get; set; }


    /// <summary>
    /// 网页打不开时调用，由打开本窗口的一方决定怎么退
    /// </summary>
    public Action? Fallback { get; set; }


    private readonly DispatcherQueueTimerHolder _loadTimeout = new();


    private readonly CancellationTokenSource _closeCts = new();


    private GameNoticeBoard? _board;


    private HashSet<string> _readIds = [];


    private bool _ready;


    private bool _fellBack;



    /// <summary>
    /// 只有国际服有公告数据
    /// </summary>
    public static bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;



    public KuroNoticeWindow()
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
        // 网页拿到焦点前按 Esc 也能关；拿到焦点后由网页自己处理
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
            await WebView_Notice.EnsureCoreWebView2Async();
            CoreWebView2 core = WebView_Notice.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.ProcessFailed += (_, _) => FallBack("process failed");
            core.AddWebResourceRequestedFilter($"{Origin}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += CoreWebView2_WebResourceRequested;
            core.NavigationStarting += CoreWebView2_NavigationStarting;
            core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
            core.WebMessageReceived += CoreWebView2_WebMessageReceived;
            core.Navigate($"{Origin}/index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize WuWa notice page");
            FallBack("initialize failed");
        }
    }



    /// <summary>
    /// 网页、样式、脚本取自输出目录的 <c>Features\GameLauncher\KuroNotice</c>，字体取自本机的游戏目录
    /// </summary>
    private void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            string path = new Uri(args.Request.Uri).AbsolutePath;
            if (path is "/")
            {
                path = "/index.html";
            }
            string? file = null;
            string contentType = "application/octet-stream";
            if (PageFiles.TryGetValue(path, out var page))
            {
                file = Path.Combine(AppContext.BaseDirectory, "Features", "GameLauncher", "KuroNotice", page.File);
                contentType = page.ContentType;
            }
            else if (path.Equals(FontPath, StringComparison.OrdinalIgnoreCase))
            {
                file = GetFontFile();
                contentType = "font/ttf";
            }
            if (file is null || !File.Exists(file))
            {
                args.Response = sender.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            // 读进内存再交出去，文件马上放掉：直接把文件流交给 WebView2 的话，要等垃圾回收才会关，
            // 期间游戏的字体文件被占着，官方启动器更新时就换不掉它
            byte[] bytes;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }
            args.Response = sender.Environment.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(), 200, "OK",
                $"Content-Type: {contentType}\r\nCache-Control: no-cache");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Serve WuWa notice page file {uri}", args.Request.Uri);
            args.Response = sender.Environment.CreateWebResourceResponse(null, 500, "Error", "");
        }
    }


    /// <summary>
    /// 游戏 SDK 自带的文鼎方新书 H7，没装游戏或找不到时返回 null
    /// </summary>
    private string? GetFontFile()
    {
        string? installPath = GameLauncherService.GetGameInstallPath(CurrentGameKey);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        string file = Path.Combine(installPath, KuroGameMapping.NoticeFontRelativePath);
        return File.Exists(file) ? file : null;
    }



    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!args.Uri.StartsWith(Origin + "/", StringComparison.OrdinalIgnoreCase))
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
        }
    }


    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        _ = OpenInBrowserAsync(args.Uri);
    }



    private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(args.WebMessageAsJson);
            switch (node?["action"]?.ToString())
            {
                case "ready":
                    await SendBoardAsync();
                    break;
                case "content":
                    await SendContentAsync(node["id"]?.ToString());
                    break;
                case "read":
                    MarkAsRead(node["id"]?.ToString());
                    break;
                case "url":
                    await OpenInBrowserAsync(node["url"]?.ToString());
                    break;
                case "close":
                    Close();
                    break;
            }
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WuWa notice web message");
        }
    }



    /// <summary>
    /// 网页就绪后把公告板送过去。取不到时也照常显示，由网页显示提示文字。
    /// </summary>
    private async Task SendBoardAsync()
    {
        string? message = null;
        try
        {
            _board = await _registry.GetBoardAsync(CurrentGameKey, _closeCts.Token);
            if (_board is null)
            {
                message = Lang.HoyolabToolboxPage_NoData;
            }
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load WuWa notice board");
            message = Lang.Common_NetworkError;
        }

        var tabs = new JsonArray();
        foreach (GameNoticeTab tab in _board?.Tabs ?? [])
        {
            var items = new JsonArray();
            foreach (GameNoticeItem item in tab.Items)
            {
                items.Add(new JsonObject
                {
                    ["id"] = item.Id,
                    ["title"] = item.Title,
                    ["date"] = item.Date,
                    ["tag"] = item.Tag,
                    ["card"] = item.BannerUrl,
                    ["unread"] = GameNoticeProviderRegistry.IsUnread(item, _readIds),
                });
            }
            tabs.Add(new JsonObject
            {
                ["key"] = tab.Type,
                ["label"] = tab.Type is GamePostType.POST_TYPE_INFO ? CoreLang.PostType_Information : CoreLang.PostType_Announcement,
                ["icon"] = tab.Type is GamePostType.POST_TYPE_INFO ? "info" : "announce",
                ["items"] = items,
            });
        }
        var init = new JsonObject
        {
            ["type"] = "init",
            ["tabs"] = tabs,
            ["text"] = new JsonObject
            {
                ["empty"] = message ?? Lang.HoyolabToolboxPage_NoData,
                ["error"] = Lang.Common_NetworkError,
            },
        };
        WebView_Notice.CoreWebView2.PostWebMessageAsJson(init.ToJsonString());
        ShowPage();
    }



    private async Task SendContentAsync(string? id)
    {
        GameNoticeItem? item = _board?.Tabs.SelectMany(x => x.Items).FirstOrDefault(x => x.Id == id);
        if (item is null)
        {
            return;
        }
        string? html = null;
        string? banner = item.BannerUrl;
        try
        {
            KuroGameNoticeContent? content = await _provider.GetContentAsync(item, _closeCts.Token);
            html = KuroNoticeMapper.ToContentHtml(content);
            banner ??= content?.Banner;
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load WuWa notice content {id}", id);
        }
        var message = new JsonObject
        {
            ["type"] = "content",
            ["id"] = item.Id,
            ["banner"] = banner,
            ["html"] = html,
        };
        WebView_Notice.CoreWebView2?.PostWebMessageAsJson(message.ToJsonString());
    }



    /// <summary>
    /// 网页里看过的公告同步给启动页的红点
    /// </summary>
    private void MarkAsRead(string? id)
    {
        if (_board is null || string.IsNullOrWhiteSpace(id) || !_readIds.Add(id))
        {
            return;
        }
        GameNoticeProviderRegistry.SaveReadIds(CurrentGameKey, _readIds, _board);
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
        _logger.LogWarning("WuWa notice page unavailable ({reason}), fall back to the built-in board.", reason);
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
            _logger.LogWarning(ex, "Open notice link {url}", url);
        }
    }



    private new void Close()
    {
        AppWindow?.Hide();
        base.Close();
    }


}
