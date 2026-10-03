using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Features.HoYoPlay;
using Starward.Features.ViewHost;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.System;


namespace Starward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class GameBannerAndPost : UserControl
{


    private Microsoft.UI.Dispatching.DispatcherQueueTimer _bannerTimer;


    private readonly ILogger<GameBannerAndPost> _logger = AppConfig.GetLogger<GameBannerAndPost>();


    private readonly LauncherContentProviderRegistry _contentRegistry = AppConfig.GetService<LauncherContentProviderRegistry>();


    private readonly GameNoticeService _gameNoticeService = AppConfig.GetService<GameNoticeService>();


    private readonly GameNoticeProviderRegistry _gameNoticeRegistry = AppConfig.GetService<GameNoticeProviderRegistry>();


    /// <summary>
    /// 横幅与资讯由供应商提供，本控件不认识具体是哪款游戏
    /// </summary>
    public GameKey CurrentGameKey { get; set; }


    /// <summary>
    /// 兼容层：只有游戏内通知窗口还需要 HoYoPlay 的游戏标识
    /// </summary>
    public GameId? CurrentGameId { get; set; }



    public GameBannerAndPost()
    {
        this.InitializeComponent();
        this.Loaded += GameBannerAndPost_Loaded;
        this.Unloaded += GameBannerAndPost_Unloaded;
        _bannerTimer = DispatcherQueue.CreateTimer();
        _bannerTimer.Interval = TimeSpan.FromSeconds(5);
        _bannerTimer.IsRepeating = true;
        _bannerTimer.Tick += _bannerTimer_Tick;
    }






    private async void GameBannerAndPost_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<GameNoticeWindowClosedMessage>(this, OnGameNoticeWindowClosed);
        WeakReferenceMessenger.Default.Register<GameAnnouncementSettingChangedMessage>(this, OnGameAnnouncementSettingChanged);
        await UpdateGameContentAsync();
        await UpdateGameNoticeAlertAsync();
    }


    private void GameBannerAndPost_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _bannerTimer.Stop();
        _bannerTimer.Tick -= _bannerTimer_Tick;
        Banners = null;
        PostGroups = null;
    }



    private void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate)
            {
                _bannerTimer.Start();
            }
            else if (message.Hide || message.SessionLock || message.Deactivate)
            {
                _bannerTimer.Stop();
            }
        }
        catch { }
    }



    private async void OnGameNoticeWindowClosed(object _, GameNoticeWindowClosedMessage message)
    {
        User32.SetForegroundWindow(XamlRoot.GetWindowHandle());
        await UpdateGameNoticeAlertAsync();
    }



    private async void OnGameAnnouncementSettingChanged(object _, GameAnnouncementSettingChangedMessage message)
    {
        // 没有设置取消，网络不好时可能会造成状态异常，懒得写了
        if (AppConfig.EnableBannerAndPost)
        {
            ShowBannerAndPost = true;
            await UpdateGameContentAsync();
            if (AppConfig.DisableGameNoticeRedHot)
            {
                IsGameNoticesAlert = false;
            }
            else
            {
                await UpdateGameNoticeAlertAsync();
            }
        }
        else
        {
            ShowBannerAndPost = false;
        }
    }



    public List<GameBanner>? Banners { get; set => SetProperty(ref field, value); }





    public List<GamePostGroup>? PostGroups { get; set => SetProperty(ref field, value); }





    public bool IsGameNoticesAlert { get; set => SetProperty(ref field, value); }




    // 与 xaml 里 Grid_BannerAndPost 的两行和行距一致
    private const double BannerRowHeight = 176;

    private const double PostRowHeight = 124;

    private const double RowSpacing = 4;


    public bool ShowBannerAndPost
    {
        get => this.Opacity == 1;
        set
        {
            bool hasBanners = Banners?.Count > 0;
            bool hasPosts = PostGroups?.Count > 0;
            if (value && (hasBanners || hasPosts))
            {
                // 只有一样时收起另一列：横幅全都下架、或资讯读不到时，剩下的那一样照样显示。
                // 控件靠下对齐，收起来是往下缩，不会留一块空白
                Grid_BannerContainer.Visibility = hasBanners ? Visibility.Visible : Visibility.Collapsed;
                Border_Post.Visibility = hasPosts ? Visibility.Visible : Visibility.Collapsed;
                Grid_BannerAndPost.RowDefinitions[0].Height = new GridLength(hasBanners ? BannerRowHeight : 0);
                Grid_BannerAndPost.RowDefinitions[1].Height = new GridLength(hasPosts ? PostRowHeight : 0);
                Grid_BannerAndPost.RowSpacing = hasBanners && hasPosts ? RowSpacing : 0;
                Grid_BannerAndPost.Height = (hasBanners ? BannerRowHeight : 0) + (hasPosts ? PostRowHeight : 0) + Grid_BannerAndPost.RowSpacing;
                if (hasBanners)
                {
                    _bannerTimer.Start();
                }
                else
                {
                    _bannerTimer.Stop();
                }
                this.Opacity = 1;
                this.IsHitTestVisible = true;
            }
            else
            {
                _bannerTimer.Stop();
                this.Opacity = 0;
                this.IsHitTestVisible = false;
            }
        }
    }




    private async Task UpdateGameContentAsync()
    {
        try
        {
            // 能力标志说的是「这款游戏有公告接口」，注册表才知道具体渠道接不接得上，
            // 两道都过了才去请求
            if (!GameFeatureConfig.FromGameKey(CurrentGameKey).BannerAndPost)
            {
                ShowBannerAndPost = false;
                return;
            }
            GameContent? content = await _contentRegistry.GetContentAsync(CurrentGameKey);
            if (content is null || !AppConfig.EnableBannerAndPost)
            {
                ShowBannerAndPost = false;
                return;
            }
            Banners = content.Banners;
            PostGroups = GamePostGroup.FromGameContent(content);
            ShowBannerAndPost = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game launcher content ({key})", CurrentGameKey);
        }
    }




    private async Task UpdateGameNoticeAlertAsync()
    {
        try
        {
            bool hoyoNotices = IsHoYoGameNotices();
            if (hoyoNotices || _gameNoticeRegistry.Supports(CurrentGameKey))
            {
                Button_InGameNotices.Visibility = Visibility.Visible;
            }
            else
            {
                Button_InGameNotices.Visibility = Visibility.Collapsed;
                return;
            }
            if (AppConfig.DisableGameNoticeRedHot)
            {
                IsGameNoticesAlert = false;
            }
            else if (hoyoNotices)
            {
                IsGameNoticesAlert = await _gameNoticeService.IsNoticeAlertAsync(CurrentGameId!.GameBiz);
            }
            else
            {
                IsGameNoticesAlert = await _gameNoticeRegistry.HasUnreadAsync(CurrentGameKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game notice alert ({key})", CurrentGameKey);
        }
    }




    private void _bannerTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (Banners?.Count > 0)
            {
                FlipView_Banner.SelectedIndex = (FlipView_Banner.SelectedIndex + 1) % Banners.Count;
            }
        }
        catch { }
    }



    private async void Image_Banner_Tapped(object sender, TappedRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is GameBanner banner)
            {
                await Launcher.LaunchUriAsync(new Uri(banner.Image.Link));
            }
        }
        catch { }
    }



    /// <summary>
    /// 隐藏 FilpView 中自动出现的翻页按键
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void FlipView_Banner_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = VisualTreeHelper.GetChild(FlipView_Banner, 0);
            if (grid != null)
            {
                var count = VisualTreeHelper.GetChildrenCount(grid);
                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var child = VisualTreeHelper.GetChild(grid, i);
                        if (child is Button button)
                        {

                            button.IsHitTestVisible = false;
                            button.Opacity = 0;
                        }
                    }
                }
            }
        }
        catch { }
    }




    private void Grid_BannerContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _bannerTimer.Stop();
        Border_PipsPager.Visibility = Visibility.Visible;
    }



    private void Grid_BannerContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _bannerTimer.Start();
        Border_PipsPager.Visibility = Visibility.Collapsed;
    }






    [RelayCommand]
    private void OpenGameNoticeWindow()
    {
        try
        {
            nint parentWindowHandle = (nint)this.XamlRoot.ContentIslandEnvironment.AppWindowId.Value;
            // 按钮只在有游戏内通知时显示，这里再挡一次以防被其他方式触发
            if (IsHoYoGameNotices())
            {
                new GameNoticeWindow
                {
                    CurrentGameBiz = CurrentGameId!.GameBiz,
                    ParentWindowHandle = parentWindowHandle,
                }.Activate();
            }
            else if (GryphlineBulletinWindow.Supports(CurrentGameKey))
            {
                // 终末地的游戏内公告本身是网页，直接嵌入；官方网页打不开时退回自己画的公告板
                GameKey key = CurrentGameKey;
                new GryphlineBulletinWindow
                {
                    CurrentGameKey = key,
                    ParentWindowHandle = parentWindowHandle,
                    Fallback = () => new VendorNoticeWindow
                    {
                        CurrentGameKey = key,
                        ParentWindowHandle = parentWindowHandle,
                    }.Activate(),
                }.Activate();
            }
            else if (_gameNoticeRegistry.Supports(CurrentGameKey))
            {
                new VendorNoticeWindow
                {
                    CurrentGameKey = CurrentGameKey,
                    ParentWindowHandle = parentWindowHandle,
                }.Activate();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open game notice window ({key})", CurrentGameKey);
        }
    }


    /// <summary>
    /// 米哈游的游戏内公告是官方网页，要用 HoYoPlay 的游戏标识；其他游戏走 <see cref="GameNoticeProviderRegistry"/>
    /// </summary>
    private bool IsHoYoGameNotices()
    {
        return CurrentGameId is not null && GameFeatureConfig.FromGameKey(CurrentGameKey).InGameNoticesWindow;
    }





    public static string AddOne(int number)
    {
        return (number + 1).ToString();
    }




}
