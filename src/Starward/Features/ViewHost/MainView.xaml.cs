using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using NuGet.Versioning;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Features.Gacha;
using Starward.Features.GameLauncher;
using Starward.Features.GamepadControl;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.RPC;
using Starward.Features.Screenshot;
using Starward.Features.SelfQuery;
using Starward.Features.Setting;
using Starward.Features.Update;
using Starward.Helpers;
using VideoCover = Starward.Features.Background.AppBackground.VideoCover;
using Starward.Providers.HoYo;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace Starward.Features.ViewHost;

[INotifyPropertyChanged]
public sealed partial class MainView : UserControl
{


    private readonly ILogger<MainView> _logger = AppConfig.GetLogger<MainView>();


    public GameKey CurrentGameKey { get; private set => SetProperty(ref field, value); }


    /// <summary>
    /// 兼容层：HoYoPlay 的游戏标识，由 <see cref="CurrentGameKey"/> 推导
    /// </summary>
    public GameId? CurrentGameId => HoYoGameIds.Resolve(CurrentGameKey);


    private GameFeatureConfig CurrentGameFeatureConfig { get; set; }



    public MainView()
    {
        this.InitializeComponent();
        InitializeMainView();
    }



    private void InitializeMainView()
    {
        this.Loaded += MainView_Loaded;
        // 崩坏三国际服的区服选择由 HoYoGameIds 在解析时读取，不再就地改写
        CurrentGameKey = GameSelector.CurrentGameKey;
        CurrentGameFeatureConfig = GameFeatureConfig.FromGameKey(CurrentGameKey);
        UpdateNavigationView();
        WeakReferenceMessenger.Default.Register<MainViewNavigateMessage>(this, OnMainViewNavigateMessageReceived);
        WeakReferenceMessenger.Default.Register<BH3GlobalGameServerChangedMessage>(this, OnBH3GlobalGameServerChanged);
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, (_, m) =>
        {
            // 失焦很频繁，而且这时弹更新窗口会打断用户正在做的事
            if (!m.Deactivate)
            {
                _ = CheckUpdateOrShowRecentUpdateContentAsync();
            }
        });
    }




    private async void MainView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateNavIndicator(animate: false));
        CheckSystemProxy();
        HotkeyManager.InitializeHotkey(this.XamlRoot.GetWindowHandle());
        _ = CheckUpdateOrShowRecentUpdateContentAsync();
        AppConfig.GetService<RpcService>().TrySetEnviromentAsync();
        LogUploadService.Start();
        if (AppConfig.EnableGamepadController)
        {
            await Task.Delay(1000);
            var queue = Content.DispatcherQueue;
            _ = Task.Run(() => GamepadController.Initialize(queue));
        }
    }




    private void GameSelector_CurrentGameChanged(object? sender, (GameKey Key, bool DoubleTapped) e)
    {
        CurrentGameKey = e.Key;
        CurrentGameFeatureConfig = GameFeatureConfig.FromGameKey(CurrentGameKey);
        UpdateNavigationView();
    }



    private void OnBH3GlobalGameServerChanged(object _, BH3GlobalGameServerChangedMessage message)
    {
        if (CurrentGameId?.GameBiz == GameBiz.bh3_global)
        {
            // 新的区服 id 已由发送方写入配置，重新解析后通知界面
            OnPropertyChanged(nameof(CurrentGameId));
            NavigateTo(typeof(GameLauncherPage), CurrentGameKey, new SuppressNavigationTransitionInfo());
        }
    }




    #region Navigation





    private void UpdateNavigationView()
    {
        NavigationViewItem_Launcher.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameLauncherPage)).ToVisibility();
        NavigationViewItem_GameSetting.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameSettingPage)).ToVisibility();
        NavigationViewItem_Screenshot.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(ScreenshotPage)).ToVisibility();
        NavigationViewItem_GachaLog.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GachaLogPage)).ToVisibility();
        NavigationViewItem_HoyolabToolbox.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GameRecordPage)).ToVisibility();
        NavigationViewItem_SelfQuery.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(SelfQueryPage)).ToVisibility();
        NavigationViewItem_GenshinBeyondGacha.Visibility = CurrentGameFeatureConfig.SupportedPages.Contains(nameof(GenshinBeyondGachaPage)).ToVisibility();

        // 切换游戏时有些项目会显示或隐藏，选中项的位置可能变了
        DispatcherQueue.TryEnqueue(() => UpdateNavIndicator(animate: _navIndicatorPlaced));

        // 抽卡记录名称，每款游戏叫法都不同
        TextBlock_GachaLog.Text = GachaLogService.GetGachaLogText(CurrentGameKey);

        if (CurrentGameKey.ChannelId is GameChannelIds.China)
        {
            ToolTipService.SetToolTip(NavigationViewItem_HoyolabToolbox, Lang.HyperionToolbox);
            TextBlock_HoyolabToolbox.Text = Lang.HyperionToolbox;
        }
        if (CurrentGameKey.ChannelId is GameChannelIds.Global)
        {
            ToolTipService.SetToolTip(NavigationViewItem_HoyolabToolbox, Lang.HoYoLABToolbox);
            TextBlock_HoyolabToolbox.Text = Lang.HoYoLABToolbox;
        }

        if (!CurrentGameKey.IsValid)
        {
            NavigateTo(typeof(BlankPage));
        }
        else if (MainView_Frame.SourcePageType?.Name is not nameof(SettingPage))
        {
            NavigateTo(MainView_Frame.SourcePageType);
        }
    }



    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            if (args.InvokedItemContainer?.IsSelected ?? false)
            {
                return;
            }
            if (args.IsSettingsInvoked)
            {
                NavigateTo(typeof(SettingPage));
            }
            else
            {
                if (args.InvokedItemContainer is NavigationViewItem item)
                {
                    var type = item.Tag switch
                    {
                        nameof(GameLauncherPage) => typeof(GameLauncherPage),
                        nameof(GameSettingPage) => typeof(GameSettingPage),
                        nameof(ScreenshotPage) => typeof(ScreenshotPage),
                        nameof(GachaLogPage) => typeof(GachaLogPage),
                        nameof(GameRecordPage) => typeof(GameRecordPage),
                        nameof(SelfQueryPage) => typeof(SelfQueryPage),
                        nameof(GenshinBeyondGachaPage) => typeof(GenshinBeyondGachaPage),
                        _ => null,
                    };
                    NavigateTo(type);
                }
            }
        }
        catch { }
    }



    private void NavigateTo(Type? page, object? param = null, NavigationTransitionInfo? infoOverride = null)
    {
        page ??= typeof(GameLauncherPage);
        // 占位页不是一个独立的功能，能力检查要按它代替的页面来做，
        // 否则从占位页切到已实现的游戏会被判成不支持而退回启动器页
        if (page.Name is nameof(GachaLogPlaceholderPage))
        {
            page = typeof(GachaLogPage);
        }
        if (page.Name is nameof(BlankPage) && !CurrentGameKey.IsValid)
        {

        }
        else if (page.Name is not nameof(SettingPage) && !CurrentGameFeatureConfig.SupportedPages.Contains(page.Name))
        {
            page = typeof(GameLauncherPage);
        }
        // 声明了抽卡能力但 Starward 还没有实现的游戏，显示尚待开发
        if (page.Name is nameof(GachaLogPage) && AppConfig.GetService<GachaProviderRegistry>().GetService(CurrentGameKey) is null)
        {
            page = typeof(GachaLogPlaceholderPage);
        }
        if (page.Name is nameof(GameLauncherPage))
        {
            MainView_NavigationView.SelectedItem = NavigationViewItem_Launcher;
        }
        if (infoOverride is null)
        {
            // 内置的 Entrance 只有滑入，换成淡入、轻微缩放加由糊变清
            // 启动器页的 Frame 底下直接就是背景，模糊层会把背景一起糊掉，切游戏时只淡入不模糊
            float blur = page.Name is nameof(GameLauncherPage) ? 0 : MotionTokens.BlurSubtle;
            MainView_Frame.NavigateWithMotion(page, param ?? CurrentGameKey, offsetY: 16, fromScale: 0.985f, blur: blur);
        }
        else
        {
            MainView_Frame.Navigate(page, param ?? CurrentGameKey, infoOverride);
        }
        _ = UpdateOverlayMaskAsync(page.Name is not (nameof(BlankPage) or nameof(GameLauncherPage)));
    }



    private bool _overlayMaskVisible;


    /// <summary>
    /// 启动器以外的页面，背景逐渐模糊并压暗；回到启动器时反向，结束后折叠，不再消耗 GPU
    /// </summary>
    private async Task UpdateOverlayMaskAsync(bool visible)
    {
        if (_overlayMaskVisible == visible)
        {
            return;
        }
        _overlayMaskVisible = visible;
        try
        {
            if (visible)
            {
                Border_OverlayMask.Visibility = Visibility.Visible;
                await OverlayMaskBrush.AnimateToAsync(MotionTokens.BlurHeavy, 0.75, MotionTokens.Emphasized);
                if (_overlayMaskVisible)
                {
                    AppBackground.SetVideoCovered(VideoCover.Page, true);
                }
            }
            else
            {
                AppBackground.SetVideoCovered(VideoCover.Page, false);
                await OverlayMaskBrush.AnimateToAsync(0, 0, MotionTokens.Slow);
                if (!_overlayMaskVisible)
                {
                    Border_OverlayMask.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update overlay mask");
            Border_OverlayMask.Visibility = _overlayMaskVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }



    #region Navigation Indicator


    private bool _navIndicatorPlaced;


    private void MainView_NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 等新的选中项布局好再量位置
        DispatcherQueue.TryEnqueue(() => UpdateNavIndicator(animate: _navIndicatorPlaced));
    }


    private void MainView_NavigationView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 窗口变高时设置项会跟着往下移，指示条直接跟过去
        DispatcherQueue.TryEnqueue(() => UpdateNavIndicator(animate: false));
    }


    /// <summary>
    /// 把自己画的指示条移到选中项内置指示条的位置（内置的已经设为透明，只用它的布局位置）
    /// </summary>
    private void UpdateNavIndicator(bool animate)
    {
        try
        {
            if (MainView_NavigationView.SelectedItem is not FrameworkElement item || item.Visibility is not Visibility.Visible || item.ActualHeight <= 0)
            {
                Rect_NavIndicator.Opacity = 0;
                return;
            }
            FrameworkElement target = FindDescendant(item, "SelectionIndicator") is FrameworkElement indicator && indicator.ActualHeight > 0 ? indicator : item;
            var origin = target.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
            float x, y;
            if (target == item)
            {
                x = (float)origin.X + 4;
                y = (float)(origin.Y + (item.ActualHeight - Rect_NavIndicator.Height) / 2);
            }
            else
            {
                Rect_NavIndicator.Width = target.ActualWidth;
                Rect_NavIndicator.Height = target.ActualHeight;
                x = (float)origin.X;
                y = (float)origin.Y;
            }

            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Rect_NavIndicator);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(Rect_NavIndicator, true);
            var compositor = visual.Compositor;
            var to = new System.Numerics.Vector3(x, y, 0);
            Rect_NavIndicator.Opacity = 1;
            if (!animate || !MotionSettings.IsEnabled)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", to);
                _navIndicatorPlaced = true;
                return;
            }
            visual.Properties.TryGetVector3("Translation", out var from);
            float distance = Math.Abs(to.Y - from.Y);

            var spring = compositor.CreateSpringVector3Animation();
            spring.FinalValue = to;
            spring.DampingRatio = 0.72f;
            spring.Period = TimeSpan.FromMilliseconds(42);
            visual.StartAnimation("Translation", spring);

            // 移动途中拉长，距离越远拉得越长，到位后缩回
            float stretch = 1 + Math.Min(distance / 60f, 1.8f);
            visual.CenterPoint = new System.Numerics.Vector3((float)Rect_NavIndicator.Width / 2, (float)Rect_NavIndicator.Height / 2, 0);
            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, System.Numerics.Vector3.One);
            scale.InsertKeyFrame(0.35f, new System.Numerics.Vector3(1, stretch, 1), MotionTokens.Standard(compositor));
            scale.InsertKeyFrame(1, System.Numerics.Vector3.One, MotionTokens.Decelerate(compositor));
            scale.Duration = MotionTokens.Slow;
            visual.StartAnimation(nameof(Microsoft.UI.Composition.Visual.Scale), scale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update navigation indicator");
        }
    }


    private static DependencyObject? FindDescendant(DependencyObject root, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }
            if (FindDescendant(child, name) is DependencyObject found)
            {
                return found;
            }
        }
        return null;
    }


    #endregion



    private void OnMainViewNavigateMessageReceived(object _, MainViewNavigateMessage message)
    {
        NavigateTo(message.Page);
    }




    #endregion




    #region Update


    private DateTimeOffset _lastCheckUpdateTime;

    private DateTimeOffset _lastShowUpdateTime;

    private SemaphoreSlim _updateLock = new(1, 1);


    private async Task CheckUpdateOrShowRecentUpdateContentAsync()
    {
#if DEBUG || DONOT_CHECK_UPDATE
        return;
#endif
#pragma warning disable CS0162 // 检测到无法访问的代码
        if (!await _updateLock.WaitAsync(0))
        {
            return;
        }
        await Task.Delay(1000);
#pragma warning restore CS0162 // 检测到无法访问的代码
        try
        {
            if (_lastCheckUpdateTime == default && NuGetVersion.TryParse(AppConfig.AppVersion, out var appVersion))
            {
                _ = NuGetVersion.TryParse(AppConfig.LastAppVersion, out var lastVersion);
                if (appVersion != lastVersion)
                {
                    if (AppConfig.ShowUpdateContentAfterUpdateRestart)
                    {
                        new UpdateWindow().Activate();
                    }
                    else
                    {
                        AppConfig.LastAppVersion = AppConfig.AppVersion;
                    }
                    _lastCheckUpdateTime = DateTimeOffset.Now - TimeSpan.FromMinutes(55);
                    return;
                }
            }
            DateTimeOffset now = DateTimeOffset.Now;
            if (now - _lastCheckUpdateTime > TimeSpan.FromHours(1))
            {
                var release = await AppConfig.GetService<UpdateService>().CheckUpdateAsync(false);
                _lastCheckUpdateTime = now;
                if (release != null && now - _lastShowUpdateTime > TimeSpan.FromHours(6) && now.Date != _lastShowUpdateTime.Date)
                {
                    new UpdateWindow { NewVersion = release }.Activate();
                    _lastShowUpdateTime = now;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check update");
        }
        finally
        {
            _updateLock.Release();
        }
    }


    #endregion




    private async void CheckSystemProxy()
    {
        try
        {
            await Task.Delay(1500);
            Uri? proxy = HttpClient.DefaultProxy.GetProxy(new Uri("https://starward.scighost.com"));
            if (proxy is not null)
            {
                InAppToast.MainWindow?.Information(Lang.MainView_CheckSystemProxy_SystemProxyIsEnabled, proxy.ToString(), 5000);
            }
        }
        catch { }
    }


}



file static class BoolToVisibilityExtension
{

    public static Visibility ToVisibility(this bool value)
    {
        return value ? Visibility.Visible : Visibility.Collapsed;
    }

}
