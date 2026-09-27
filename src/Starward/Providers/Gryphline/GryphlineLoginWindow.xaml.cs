using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Starward.Frameworks;
using System;
using System.Linq;
using System.Threading.Tasks;


namespace Starward.Providers.Gryphline;

/// <summary>
/// 在 WebView2 里登录鹰角通行证，取得 <c>ACCOUNT_TOKEN</c>。
/// <para/>
/// 与米游社 / HoYoLAB 的登录是同一个做法：让玩家在官方网页上自己登录，
/// 登录成功后从 WebView2 的 Cookie 里把令牌取出来。账号密码只在官方网页里输入，
/// Starward 不经手。
/// <para/>
/// ACCOUNT_TOKEN 是 httpOnly 的 Cookie，网页脚本读不到，
/// 但 WebView2 的 CookieManager 读得到，这正是它能用的原因。
/// </summary>
public sealed partial class GryphlineLoginWindow : WindowEx
{

    /// <summary>
    /// SKPort 的终末地签到页。没登录时它会自己弹出登录框，登录后令牌落在 .skport.com 上。
    /// </summary>
    private const string LoginUrl = "https://game.skport.com/endfield/sign-in";

    /// <summary>
    /// ACCOUNT_TOKEN 不在根路径下：它设在 .skport.com 的 <c>/cookie_store/account_token</c>。
    /// CookieManager 按网址的路径筛选，拿 https://game.skport.com 去问是拿不到它的，
    /// 必须带上这个路径。
    /// </summary>
    private const string TokenCookieUrl = "https://web-api.skport.com/cookie_store/account_token";

    private const string TokenCookieName = "ACCOUNT_TOKEN";


    private readonly ILogger<GryphlineLoginWindow> _logger = AppConfig.GetLogger<GryphlineLoginWindow>();

    private readonly TaskCompletionSource<string?> _result = new();

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _pollTimer;



    public GryphlineLoginWindow()
    {
        this.InitializeComponent();
        Title = Lang.GryphlineLoginWindow_Title;
        SetIcon();
        AdaptTitleBarButtonColorToActuallTheme();
        CenterInScreen(480, 760);
        // 登录框是网页前端自己画的，登录成功不一定会跳转页面，只靠导航事件会漏掉，
        // 因此另外每秒看一次 Cookie
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(1);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += async (_, _) => await TryCompleteAsync();
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            // 玩家直接关掉窗口就当取消
            _result.TrySetResult(null);
        };
    }



    /// <summary>
    /// 等到登录完成，返回令牌；玩家关掉窗口时返回 null
    /// </summary>
    public Task<string?> WaitForTokenAsync() => _result.Task;



    private async void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webview.EnsureCoreWebView2Async();
            CoreWebView2 core = webview.CoreWebView2;
            // 只有在 Starward 里登出过才清掉网页的登录状态，让「登出后换一个账号登录」按预期工作。
            // 平时不清：网页还登录着就直接取令牌，玩家不必再输一次密码。
            if (GryphlineAccountStore.ConsumeClearWebLoginRequest())
            {
                // 空字符串取出本配置文件的全部 Cookie；按网址问会被路径筛掉一部分，
                // ACCOUNT_TOKEN 就是在非根路径下的
                foreach (CoreWebView2Cookie cookie in await core.CookieManager.GetCookiesAsync(""))
                {
                    if (IsGryphlineDomain(cookie.Domain))
                    {
                        core.CookieManager.DeleteCookie(cookie);
                    }
                }
            }
            core.NavigationCompleted += async (_, _) =>
            {
                webview.Visibility = Visibility.Visible;
                await TryCompleteAsync();
            };
            core.Navigate(LoginUrl);
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize Gryphline login webview");
        }
    }



    private async Task TryCompleteAsync()
    {
        if (_result.Task.IsCompleted || webview.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync(TokenCookieUrl);
            string? value = cookies.FirstOrDefault(x => x.Name == TokenCookieName)?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                // 路径哪天换了也别漏掉：退回在全部 Cookie 里按名称与网域找
                cookies = await core.CookieManager.GetCookiesAsync("");
                value = cookies.FirstOrDefault(x => x.Name == TokenCookieName && IsGryphlineDomain(x.Domain))?.Value;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            // 网页把它编码过一次存进 Cookie，签到脚本读出来时也用 decodeURIComponent 解码。
            // 不能用 WebUtility.UrlDecode：它会把 + 变成空格，而令牌里可能有 +。
            string token = Uri.UnescapeDataString(value);
            _pollTimer.Stop();
            if (_result.TrySetResult(token))
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read Gryphline login cookie");
        }
    }



    private static bool IsGryphlineDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }
        string d = domain.TrimStart('.');
        return d.Equals("skport.com", StringComparison.OrdinalIgnoreCase)
            || d.EndsWith(".skport.com", StringComparison.OrdinalIgnoreCase)
            || d.Equals("gryphline.com", StringComparison.OrdinalIgnoreCase)
            || d.EndsWith(".gryphline.com", StringComparison.OrdinalIgnoreCase);
    }

}
