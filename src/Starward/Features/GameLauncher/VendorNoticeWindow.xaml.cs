using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Localization;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.System;
using Color = Windows.UI.Color;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 米哈游以外的游戏内公告窗口：上方分页、左边标题、右边正文。
/// <para/>
/// 米哈游的游戏内公告是官方网页，由 <see cref="GameNoticeWindow"/> 直接嵌入；
/// 其他几家只有数据接口，这里自己画，数据来自 <see cref="GameNoticeProviderRegistry"/>。
/// 窗口的摆法与 <see cref="GameNoticeWindow"/> 相同：盖在启动器上，按 Esc 或点外面关闭。
/// </summary>
public sealed partial class VendorNoticeWindow : WindowEx
{


    private readonly ILogger<VendorNoticeWindow> _logger = AppConfig.GetLogger<VendorNoticeWindow>();


    private readonly GameNoticeProviderRegistry _registry = AppConfig.GetService<GameNoticeProviderRegistry>();


    public GameKey CurrentGameKey { get; set; }


    public nint ParentWindowHandle { get; set; }


    private GameNoticeBoard? _board;


    private HashSet<string> _readIds = [];


    private VendorNoticeTheme _theme = VendorNoticeTheme.Default;


    private readonly CancellationTokenSource _closeCts = new();


    /// <summary>
    /// 换篇时取消上一篇还没取完的正文
    /// </summary>
    private CancellationTokenSource? _contentCts;


    /// <summary>
    /// 正文是用 NavigateToString 塞进去的，只放行这一次导航，其余链接交给浏览器
    /// </summary>
    private bool _allowNextNavigation;



    public VendorNoticeWindow()
    {
        this.InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();
        InitializeWindow();
        Closed += VendorNoticeWindow_Closed;
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
        ApplyTheme(VendorNoticeTheme.ForGame(CurrentGameKey));
        await InitializeWebViewAsync();
        await LoadBoardAsync();
    }



    /// <summary>
    /// 套上这款游戏的公告板样式。分页与列表项的颜色由各自的数据模板按主题取。
    /// </summary>
    private void ApplyTheme(VendorNoticeTheme theme)
    {
        _theme = theme;
        RootGrid.Background = new SolidColorBrush(theme.Overlay);
        Grid_Board.Background = new SolidColorBrush(theme.BoardBackground);
        Grid_Board.CornerRadius = theme.BoardCornerRadius;
        Grid_Board.BorderBrush = new SolidColorBrush(theme.BoardBorder);
        Grid_Board.BorderThickness = theme.BoardBorderThickness;
        Grid_Header.Background = new SolidColorBrush(theme.HeaderBackground);
        Grid_Header.BorderBrush = new SolidColorBrush(theme.HeaderBorder);
        Grid_Header.BorderThickness = theme.HeaderBorderThickness;
        ListView_Tabs.HorizontalAlignment = theme.TabAlignment;
        FontIcon_Close.Foreground = new SolidColorBrush(theme.CloseForeground);
        ListView_Notices.Background = new SolidColorBrush(theme.ListBackground);
        Grid_Content.Background = new SolidColorBrush(theme.ContentBackground);
        Grid_BoardState.Background = new SolidColorBrush(theme.BoardBackground);
        var accent = new SolidColorBrush(theme.Accent);
        ProgressRing_Board.Foreground = accent;
        ProgressRing_Content.Foreground = accent;
        var secondary = new SolidColorBrush(theme.ItemSecondaryForeground);
        TextBlock_BoardError.Foreground = secondary;
        TextBlock_ContentError.Foreground = secondary;
    }



    private async Task InitializeWebViewAsync()
    {
        try
        {
            await WebView_Content.EnsureCoreWebView2Async();
            CoreWebView2 core = WebView_Content.CoreWebView2;
            // 正文是远端来的 HTML，只拿来排版，不跑脚本
            core.Settings.IsScriptEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += CoreWebView2_NavigationStarting;
            core.NewWindowRequested += CoreWebView2_NewWindowRequested;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize WebView2 for vendor notices");
        }
    }



    private async Task LoadBoardAsync()
    {
        Grid_BoardState.Visibility = Visibility.Visible;
        ProgressRing_Board.IsActive = true;
        StackPanel_BoardError.Visibility = Visibility.Collapsed;
        try
        {
            _board = await _registry.GetBoardAsync(CurrentGameKey, _closeCts.Token);
            if (_board is null)
            {
                ShowBoardError(Lang.HoyolabToolboxPage_NoData);
                return;
            }
            _readIds = GameNoticeProviderRegistry.GetReadIds(CurrentGameKey);
            var tabs = _board.Tabs.Select(x => new VendorNoticeTabItem(x, LocalizePostType(x.Type), _theme)).ToList();
            ListView_Tabs.ItemsSource = tabs;
            Grid_BoardState.Visibility = Visibility.Collapsed;
            ProgressRing_Board.IsActive = false;
            ListView_Tabs.SelectedIndex = tabs.Count > 0 ? 0 : -1;
        }
        catch (OperationCanceledException) when (_closeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load vendor notices ({key})", CurrentGameKey);
            ShowBoardError(Lang.Common_NetworkError);
        }
    }


    private void ShowBoardError(string message)
    {
        Grid_BoardState.Visibility = Visibility.Visible;
        ProgressRing_Board.IsActive = false;
        TextBlock_BoardError.Text = message;
        StackPanel_BoardError.Visibility = Visibility.Visible;
    }


    private async void Button_Retry_Click(object sender, RoutedEventArgs e)
    {
        await LoadBoardAsync();
    }



    private void ListView_Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelection(e);
        if (ListView_Tabs.SelectedItem is not VendorNoticeTabItem selected)
        {
            return;
        }
        var items = selected.Tab.Items.Select(x => new VendorNoticeListItem(x, GameNoticeProviderRegistry.IsUnread(x, _readIds), _theme)).ToList();
        ListView_Notices.ItemsSource = items;
        ListView_Notices.SelectedIndex = items.Count > 0 ? 0 : -1;
    }


    /// <summary>
    /// 系统的选中底色去掉了，选中的样子由数据模板按 <see cref="VendorNoticeSelectable.IsSelected"/> 画
    /// </summary>
    private static void SyncSelection(SelectionChangedEventArgs e)
    {
        foreach (VendorNoticeSelectable item in e.RemovedItems.OfType<VendorNoticeSelectable>())
        {
            item.IsSelected = false;
        }
        foreach (VendorNoticeSelectable item in e.AddedItems.OfType<VendorNoticeSelectable>())
        {
            item.IsSelected = true;
        }
    }


    private void Tab_PointerEntered(object sender, PointerRoutedEventArgs e) => SetHovered(sender, true);

    private void Tab_PointerExited(object sender, PointerRoutedEventArgs e) => SetHovered(sender, false);

    private void NoticeItem_PointerEntered(object sender, PointerRoutedEventArgs e) => SetHovered(sender, true);

    private void NoticeItem_PointerExited(object sender, PointerRoutedEventArgs e) => SetHovered(sender, false);


    private static void SetHovered(object sender, bool value)
    {
        if (sender is FrameworkElement { DataContext: VendorNoticeSelectable item })
        {
            item.IsHovered = value;
        }
    }



    private async void ListView_Notices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelection(e);
        if (ListView_Notices.SelectedItem is not VendorNoticeListItem selected)
        {
            return;
        }
        MarkAsRead(selected);
        _contentCts?.Cancel();
        _contentCts = CancellationTokenSource.CreateLinkedTokenSource(_closeCts.Token);
        CancellationToken token = _contentCts.Token;
        WebView_Content.Visibility = Visibility.Collapsed;
        TextBlock_ContentError.Visibility = Visibility.Collapsed;
        ProgressRing_Content.IsActive = true;
        try
        {
            string? html = await _registry.GetContentHtmlAsync(CurrentGameKey, selected.Item, token);
            token.ThrowIfCancellationRequested();
            if (html is null || WebView_Content.CoreWebView2 is null)
            {
                TextBlock_ContentError.Visibility = Visibility.Visible;
                return;
            }
            _allowNextNavigation = true;
            WebView_Content.CoreWebView2.NavigateToString(BuildDocument(selected.Item, html, _theme));
            WebView_Content.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load vendor notice content ({key}, {id})", CurrentGameKey, selected.Item.Id);
            TextBlock_ContentError.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                ProgressRing_Content.IsActive = false;
            }
        }
    }


    private void MarkAsRead(VendorNoticeListItem selected)
    {
        if (_board is null || !_readIds.Add(selected.Item.Id))
        {
            return;
        }
        selected.IsUnread = false;
        GameNoticeProviderRegistry.SaveReadIds(CurrentGameKey, _readIds, _board);
    }



    /// <summary>
    /// 正文包成完整的网页：标题、日期、横幅在上，图片不超过版面宽度。
    /// 颜色是 CSS 变量，由 <see cref="VendorNoticeTheme.ContentCss"/> 给值并补上各游戏的标题样式。
    /// </summary>
    private static string BuildDocument(GameNoticeItem item, string content, VendorNoticeTheme theme)
    {
        string title = WebUtility.HtmlEncode((item.Header ?? item.Title).ReplaceLineEndings(" "));
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        if (Uri.TryCreate(item.BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            sb.Append($"<base href=\"{WebUtility.HtmlEncode(baseUri.AbsoluteUri)}\">");
        }
        sb.Append("""
            <style>
            :root { --fg: #1A1A1A; --muted: #707070; --link: #0067C0; --thumb: #00000040; --rule: #D0D0D0; }
            html, body { margin: 0; background: transparent; color: var(--fg); }
            body { padding: 4px 28px 32px 28px; font-family: "Segoe UI Variable Text", "Microsoft JhengHei UI", "Microsoft YaHei UI", "Yu Gothic UI", "Malgun Gothic", sans-serif; font-size: 15px; line-height: 1.7; word-wrap: break-word; }
            img, video { max-width: 100% !important; height: auto !important; }
            table { max-width: 100%; border-collapse: collapse; }
            td, th { border: 1px solid var(--rule); padding: 4px 8px; }
            a { color: var(--link); }
            .sw-title { font-size: 22px; font-weight: 600; line-height: 1.4; margin: 20px 0 4px 0; }
            .sw-date { color: var(--muted); font-size: 13px; margin-bottom: 16px; }
            .sw-banner { display: block; width: 100%; border-radius: 6px; margin-bottom: 16px; }
            ::-webkit-scrollbar { width: 6px; }
            ::-webkit-scrollbar-thumb { background: var(--thumb); border-radius: 3px; }
            """);
        sb.Append(theme.ContentCss);
        // WebView2 的透明底不会透出底下 Grid 的颜色，底色直接写进网页
        Color background = theme.ContentBackground;
        sb.Append($"html {{ background: #{background.R:X2}{background.G:X2}{background.B:X2}; }}");
        sb.Append("</style></head><body>");
        sb.Append($"<div class=\"sw-title\">{title}</div>");
        if (!string.IsNullOrWhiteSpace(item.Date))
        {
            sb.Append($"<div class=\"sw-date\">{WebUtility.HtmlEncode(item.Date)}</div>");
        }
        if (Uri.TryCreate(item.BannerUrl, UriKind.Absolute, out Uri? banner) && banner.Scheme is "http" or "https")
        {
            sb.Append($"<img class=\"sw-banner\" src=\"{WebUtility.HtmlEncode(banner.AbsoluteUri)}\">");
        }
        sb.Append(content);
        sb.Append("</body></html>");
        return sb.ToString();
    }



    private async void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (_allowNextNavigation)
        {
            _allowNextNavigation = false;
            return;
        }
        // 正文里的链接（活动网页、社群）一律交给系统浏览器，不在公告板里跳走
        args.Cancel = true;
        await OpenInBrowserAsync(args.Uri);
    }


    private async void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        await OpenInBrowserAsync(args.Uri);
    }


    private async Task OpenInBrowserAsync(string? url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open notice link {url}", url);
        }
    }



    private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 点公告板外面的暗处关闭，与游戏内一致
        Close();
    }


    private void Grid_Board_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }


    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }



    private void VendorNoticeWindow_Closed(object sender, WindowEventArgs args)
    {
        _closeCts.Cancel();
        _contentCts?.Cancel();
        WeakReferenceMessenger.Default.Send(new GameNoticeWindowClosedMessage());
    }


    private new void Close()
    {
        AppWindow?.Hide();
        base.Close();
    }



    private static string LocalizePostType(string postType)
    {
        return postType switch
        {
            GamePostType.POST_TYPE_ACTIVITY => CoreLang.PostType_Activity,
            GamePostType.POST_TYPE_ANNOUNCE => CoreLang.PostType_Announcement,
            GamePostType.POST_TYPE_INFO => CoreLang.PostType_Information,
            _ => postType,
        };
    }


}



/// <summary>
/// 分页与列表项共用：系统的选中、悬停底色去掉了，由数据模板按这两个状态取主题里的颜色
/// </summary>
public abstract partial class VendorNoticeSelectable : ObservableObject
{

    protected VendorNoticeSelectable(VendorNoticeTheme theme)
    {
        Theme = theme;
    }


    public VendorNoticeTheme Theme { get; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Background), nameof(Foreground), nameof(SecondaryForeground), nameof(Indicator), nameof(Marker))]
    public partial bool IsSelected { get; set; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Background))]
    public partial bool IsHovered { get; set; }


    public abstract Brush Background { get; }

    public abstract Brush Foreground { get; }

    public virtual Brush SecondaryForeground => Foreground;

    public virtual Brush Indicator => Transparent;

    public virtual Brush Marker => Transparent;


    protected static Brush Transparent { get; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);


    protected static Brush Pick(bool selected, bool hovered, Color selectedColor, Color hoverColor, Color normalColor)
    {
        return new SolidColorBrush(selected ? selectedColor : hovered ? hoverColor : normalColor);
    }

}



/// <summary>
/// 公告板上方的一个分页
/// </summary>
public sealed partial class VendorNoticeTabItem : VendorNoticeSelectable
{

    public VendorNoticeTabItem(GameNoticeTab tab, string title, VendorNoticeTheme theme) : base(theme)
    {
        Tab = tab;
        Title = title;
        Subtitle = VendorNoticeTheme.GetTabSubtitle(tab.Type);
    }


    public GameNoticeTab Tab { get; }


    public string Title { get; }


    public string Subtitle { get; }


    public override Brush Background => Pick(IsSelected, IsHovered, Theme.TabSelectedBackground, Theme.TabHoverBackground, Theme.TabBackground);

    public override Brush Foreground => new SolidColorBrush(IsSelected ? Theme.TabSelectedForeground : Theme.TabForeground);

    public override Brush Indicator => IsSelected ? new SolidColorBrush(Theme.TabIndicator) : Transparent;

}



/// <summary>
/// 公告板列表里的一项，红点看过就消掉
/// </summary>
public sealed partial class VendorNoticeListItem : VendorNoticeSelectable
{

    public VendorNoticeListItem(GameNoticeItem item, bool isUnread, VendorNoticeTheme theme) : base(theme)
    {
        Item = item;
        IsUnread = isUnread;
        DotBrush = new SolidColorBrush(theme.RedDot);
        DotRadius = theme.RedDotShape is NoticeDotShape.Circle ? 4 : 0;
        DotAngle = theme.RedDotShape is NoticeDotShape.Diamond ? 45 : 0;
    }


    public GameNoticeItem Item { get; }


    public string Title => Item.Title.Trim();


    [ObservableProperty]
    public partial bool IsUnread { get; set; }


    public Brush DotBrush { get; }

    public double DotRadius { get; }

    public double DotAngle { get; }


    public override Brush Background => Pick(IsSelected, IsHovered, Theme.ItemSelectedBackground, Theme.ItemHoverBackground, Theme.ItemBackground);

    public override Brush Foreground => new SolidColorBrush(IsSelected ? Theme.ItemSelectedForeground : Theme.ItemForeground);

    public override Brush SecondaryForeground => new SolidColorBrush(IsSelected ? Theme.ItemSelectedSecondaryForeground : Theme.ItemSecondaryForeground);

    public override Brush Marker => IsSelected ? new SolidColorBrush(Theme.ItemSelectedMarker) : Transparent;

}
