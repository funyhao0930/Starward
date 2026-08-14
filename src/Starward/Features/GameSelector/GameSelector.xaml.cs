using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.Setting;
using Starward.Features.ViewHost;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.Foundation;


namespace Starward.Features.GameSelector;

[INotifyPropertyChanged]
public sealed partial class GameSelector : UserControl
{


    public event EventHandler<(GameId, bool DoubleTapped)>? CurrentGameChanged;


    private readonly ILogger<GameSelector> _logger = AppConfig.GetLogger<GameSelector>();


    private readonly IGameProviderRegistry _providerRegistry = AppConfig.GetService<IGameProviderRegistry>();


    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();



    public GameSelector()
    {
        this.InitializeComponent();
        InitializeGameSelector();
        this.Loaded += GameSelector_Loaded;
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, OnLanguageChanged);
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<MainWindowDragRectAdaptToGameIconMessage>(this, OnMainWindowStateChanged);
    }



    public GameBiz CurrentGameBiz { get; set; }


    public GameId? CurrentGameId { get; set; }


    public ObservableCollection<GameBizIcon> GameBizIcons { get; set => SetProperty(ref field, value); } = new();


    public GameBizIcon? CurrentGameBizIcon { get; set => SetProperty(ref field, value); }


    public bool IsPinned { get; set => SetProperty(ref field, value); }


    private bool ignoreDpiChanged = false;

    private double lastScale = 1;



    public void InitializeGameSelector()
    {
        IReadOnlyList<GameDescriptor> games = _providerRegistry.GetAllGames();
        _logger.LogInformation("Game catalog: {count} game(s) from {providers} provider(s).",
                               games.Count, _providerRegistry.CatalogProviders.Count);
        InitializeGameIconsArea(games);
        InitializeGameServerArea(games);
        InitializeInstalledGamesCommand.Execute(null);
    }



    private async void GameSelector_Loaded(object sender, RoutedEventArgs e)
    {
        this.XamlRoot.Changed -= XamlRoot_Changed;
        this.XamlRoot.Changed += XamlRoot_Changed;
        await Task.Delay(1000);
        await UpdateGameInfoAsync();
    }



    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (ignoreDpiChanged)
        {
            return;
        }
        if (lastScale != sender.RasterizationScale)
        {
            lastScale = sender.RasterizationScale;
            UpdateDragRectangles();
        }
    }





    private async void OnLanguageChanged(object? _, LanguageChangedMessage __)
    {
        this.Bindings.Update();
        await UpdateGameInfoAsync();
    }




    private async void OnMainWindowStateChanged(object? _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate && (message.ElapsedOver(TimeSpan.FromMinutes(10)) || message.IsCrossingHour))
            {
                await UpdateGameInfoAsync();
            }
        }
        catch { }
    }



    private void OnMainWindowStateChanged(object? _, MainWindowDragRectAdaptToGameIconMessage message)
    {
        if (!message.IgnoreDpiChanged)
        {
            UpdateDragRectangles();
        }
        ignoreDpiChanged = message.IgnoreDpiChanged;
    }




    /// <summary>
    /// 按存储键查找游戏描述。
    /// 米哈游游戏沿用旧的 GameBiz 字符串，其他供应商使用 GameKey 的正规字符串，
    /// 两者都是纯字符串，配置文件格式不变。
    /// </summary>
    private static GameDescriptor? FindBySettingsKey(IReadOnlyList<GameDescriptor> games, GameBiz gameBiz)
    {
        if (string.IsNullOrWhiteSpace(gameBiz.Value))
        {
            return null;
        }
        return games.FirstOrDefault(x => x.SettingsKey == gameBiz.Value);
    }




    #region Game Icon


    /// <summary>
    /// 初始化游戏图标区域
    /// </summary>
    private void InitializeGameIconsArea(IReadOnlyList<GameDescriptor> games)
    {
        try
        {
            GameBizIcons.CollectionChanged -= GameBizIcons_CollectionChanged;
            GameBizIcons.Clear();

            // 从配置文件读取已选的 GameBiz
            string? bizs = AppConfig.SelectedGameBizs;
            foreach (string str in bizs?.Split(',')?.Distinct() ?? [])
            {
                if (FindBySettingsKey(games, str) is GameDescriptor descriptor)
                {
                    GameBizIcons.Add(new GameBizIcon(descriptor));
                }
            }

            // 选取当前游戏
            GameBiz lastSelectedGameBiz = AppConfig.CurrentGameBiz;
            if (GameBizIcons.FirstOrDefault(x => x.GameBiz == lastSelectedGameBiz) is GameBizIcon icon)
            {
                CurrentGameBizIcon = icon;
                CurrentGameBizIcon.IsSelected = true;
                CurrentGameBiz = lastSelectedGameBiz;
            }
            else if (FindBySettingsKey(games, lastSelectedGameBiz) is GameDescriptor descriptor)
            {
                CurrentGameBizIcon = new GameBizIcon(descriptor);
                CurrentGameBizIcon.IsSelected = true;
                CurrentGameBiz = lastSelectedGameBiz;
            }

            CurrentGameId = CurrentGameBizIcon?.GameId;
            if (CurrentGameId is not null)
            {
                CurrentGameChanged?.Invoke(this, (CurrentGameId, false));
            }

            if (AppConfig.IsGameBizSelectorPinned)
            {
                Pin();
            }

            GameBizIcons.CollectionChanged += GameBizIcons_CollectionChanged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize game icons area");
        }
        if (GameBizIcons.Count == 0 && CurrentGameBizIcon is null)
        {
            TeachTip_SelectGame.IsOpen = true;
        }
    }



    /// <summary>
    /// 游戏图标区域是否可见
    /// </summary>
    public bool GameIconsAreaVisible
    {
        get => Grid_GameIconsArea.Translation == Vector3.Zero;
        set
        {
            if (value)
            {
                Grid_GameIconsArea.Translation = Vector3.Zero;
            }
            else
            {
                Grid_GameIconsArea.Translation = new Vector3(0, -100, 0);
            }
            UpdateDragRectangles();
        }
    }


    /// <summary>
    /// 更新窗口拖拽区域
    /// </summary>
    public void UpdateDragRectangles()
    {
        try
        {
            double x = Border_CurrentGameIcon.ActualWidth;
            if (GameIconsAreaVisible)
            {
                x = Border_CurrentGameIcon.ActualWidth + Grid_GameIconsArea.ActualWidth;
            }
            this.XamlRoot.SetWindowDragRectangles([new Rect(x, 0, 10000, 48)]);
        }
        catch { }
    }



    /// <summary>
    /// 鼠标移入到当前游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Border_CurrentGameIcon_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 显示所有游戏图标
        GameIconsAreaVisible = true;
    }



    /// <summary>
    /// 鼠标移出当前游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Border_CurrentGameIcon_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (FullBackgroundVisible || IsPinned)
        {
            // 当前游戏图标被固定或者全屏显示时，不隐藏所有游戏图标
            return;
        }
        if (sender is UIElement ele)
        {
            var postion = e.GetCurrentPoint(sender as UIElement).Position;
            if (postion.X > ele.ActualSize.X - 1 && postion.Y > 0 && postion.Y < ele.ActualSize.Y)
            {
                // 从右侧移出，此时进入到所有游戏图标区域，不隐藏
                return;
            }
        }
        // 其他方向移出，隐藏所有游戏图标
        GameIconsAreaVisible = false;
    }



    /// <summary>
    /// 鼠标移出所有游戏图标区域
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIconsArea_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (FullBackgroundVisible || IsPinned)
        {
            return;
        }
        GameIconsAreaVisible = false;
    }



    /// <summary>
    /// 点击没有被选择的游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIcon_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon icon)
        {
            if (CurrentGameBizIcon is not null)
            {
                CurrentGameBizIcon.IsSelected = false;
            }

            CurrentGameBizIcon = icon;
            CurrentGameBiz = icon.GameBiz;
            CurrentGameId = icon.GameId;
            icon.IsSelected = true;

            CurrentGameChanged?.Invoke(this, (icon.GameId, false));
            AppConfig.CurrentGameBiz = icon.GameBiz;
        }
    }



    /// <summary>
    /// 双击没有被选择的游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIcon_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon icon)
        {
            if (CurrentGameBizIcon is not null)
            {
                CurrentGameBizIcon.IsSelected = false;
            }

            CurrentGameBizIcon = icon;
            CurrentGameBiz = icon.GameBiz;
            CurrentGameId = icon.GameId;
            icon.IsSelected = true;
            HideFullBackground();

            CurrentGameChanged?.Invoke(this, (icon.GameId, true));
            AppConfig.CurrentGameBiz = icon.GameBiz;
        }
    }


    /// <summary>
    /// 鼠标移入到游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIcon_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon icon)
        {
            if (!icon.IsSelected)
            {
                icon.MaskOpacity = 0;
            }
        }
    }



    /// <summary>
    /// 鼠标移出游戏图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIcon_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon icon)
        {
            if (!icon.IsSelected)
            {
                icon.MaskOpacity = 1;
            }
        }
    }



    /// <summary>
    /// 游戏图标区域大小变化
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameIconsArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDragRectangles();
    }



    /// <summary>
    /// 固定游戏待选区
    /// </summary>
    [RelayCommand]
    private void Pin()
    {
        IsPinned = !IsPinned;
        if (IsPinned)
        {
            GameIconsAreaVisible = true;
        }
        else
        {
            // 避免在固定时更换当前游戏，取消固定后，左上角的图标不改变的问题
            var temp = CurrentGameBizIcon;
            CurrentGameBizIcon = null;
            CurrentGameBizIcon = temp;
            if (!FullBackgroundVisible)
            {
                GameIconsAreaVisible = false;
            }
        }
        AppConfig.IsGameBizSelectorPinned = IsPinned;
    }



    /// <summary>
    /// 待选游戏变更时，保存配置
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void GameBizIcons_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var icon in GameBizIcons)
            {
                sb.Append(icon.GameBiz);
                sb.Append(',');
            }
            AppConfig.SelectedGameBizs = sb.ToString().TrimEnd(',');
        }
        catch { }
    }



    #endregion





    #region Full Background 黑色半透明背景


    public bool FullBackgroundVisible => Border_FullBackground.Opacity > 0;


    [RelayCommand]
    private void ShowFullBackground()
    {
        Border_FullBackground.Opacity = 1;
        Border_FullBackground.IsHitTestVisible = true;
        Border_FullBackground.Visibility = Visibility.Visible;
        Border_Pin.Opacity = 1;
        Border_Pin.IsHitTestVisible = true;
        Border_Pin.Visibility = Visibility.Visible;
        GameIconsAreaVisible = true;
    }


    private void HideFullBackground()
    {
        Border_FullBackground.Opacity = 0;
        Border_FullBackground.IsHitTestVisible = false;
        Border_FullBackground.Visibility = Visibility.Collapsed;
        Border_Pin.Opacity = 0;
        Border_Pin.IsHitTestVisible = false;
        Border_Pin.Visibility = Visibility.Collapsed;
        if (!IsPinned)
        {
            GameIconsAreaVisible = false;
        }
    }


    private void Border_FullBackground_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _isGameBizDisplayPressed = false;
        var position = e.GetPosition(sender as UIElement);
        if (position.X <= Border_CurrentGameIcon.ActualWidth && position.Y <= Border_CurrentGameIcon.ActualHeight)
        {
            Border_FullBackground.Opacity = 0;
            Border_FullBackground.IsHitTestVisible = false;
            Border_FullBackground.Visibility = Visibility.Collapsed;
            Border_Pin.Opacity = 0;
            Border_Pin.IsHitTestVisible = false;
            Border_Pin.Visibility = Visibility.Collapsed;
        }
        else
        {
            HideFullBackground();
        }
    }


    #endregion





    #region Game Server



    public List<GameBizDisplay> GameBizDisplays { get; set => SetProperty(ref field, value); }




    /// <summary>
    /// 渠道的显示顺序，与重构前的 ["_cn", "_global", "_bilibili"] 一致
    /// </summary>
    private static int GetChannelRank(string channelId) => channelId switch
    {
        GameChannelIds.China => 0,
        GameChannelIds.Global => 1,
        GameChannelIds.Bilibili => 2,
        _ => 3,
    };


    /// <summary>
    /// 初始化游戏服务器选择区域。
    /// 游戏清单来自所有 <see cref="IGameCatalogProvider"/>，按游戏分组，每组内是该游戏的所有渠道。
    /// </summary>
    private void InitializeGameServerArea(IReadOnlyList<GameDescriptor> games)
    {
        try
        {
            // 当前语言为简体中文时优先显示国服的图片，否则优先显示国际服的图片
            bool preferChinaServer = LanguageUtil.FilterLanguage(CultureInfo.CurrentUICulture.Name) is "zh-cn";
            string preferredChannel = preferChinaServer ? GameChannelIds.China : GameChannelIds.Global;

            var list = new List<GameBizDisplay>();
            foreach (IGrouping<(string ProviderId, string GameId), GameDescriptor> group
                     in games.GroupBy(x => (x.Key.ProviderId, x.Key.GameId)))
            {
                List<GameDescriptor> channels = group.OrderBy(x => GetChannelRank(x.Key.ChannelId)).ToList();

                // 用于展示的渠道：优先取有缩略图的首选渠道，
                // 没有缩略图的游戏（例如只支持启动的第三方游戏）也要显示，用图标顶替
                GameDescriptor? display = channels.FirstOrDefault(x => x.Key.ChannelId == preferredChannel && !string.IsNullOrWhiteSpace(x.ThumbnailUri))
                                       ?? channels.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ThumbnailUri))
                                       ?? channels.FirstOrDefault(x => x.Key.ChannelId == preferredChannel)
                                       ?? channels.FirstOrDefault();
                if (display is null)
                {
                    continue;
                }

                var item = new GameBizDisplay
                {
                    GameKey = display.Key,
                    ThumbnailUri = display.ThumbnailUri,
                    LogoUri = display.LogoUri ?? display.IconUri,
                    IconUri = display.IconUri,
                };
                foreach (GameDescriptor channel in channels)
                {
                    item.Servers.Add(new GameBizIcon(channel)
                    {
                        IsPinned = GameBizIcons.Any(x => x.Key == channel.Key),
                    });
                }
                list.Add(item);
            }
            GameBizDisplays = new(list);
            _logger.LogInformation("Game selector shows {count} game(s).", list.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize game server area");
        }
    }



    /// <summary>
    /// 更新所有游戏服务器信息，更新游戏图标
    /// </summary>
    /// <returns></returns>
    private async Task UpdateGameInfoAsync()
    {
        try
        {
            await _providerRegistry.RefreshAllAsync();
            IReadOnlyList<GameDescriptor> games = _providerRegistry.GetAllGames();
            InitializeGameServerArea(games);
            foreach (GameBizIcon icon in GameBizIcons)
            {
                if (games.FirstOrDefault(x => x.Key == icon.Key) is GameDescriptor descriptor)
                {
                    icon.UpdateInfo(descriptor);
                }
            }
            await InitializeInstalledGamesCommand.ExecuteAsync(null);
        }
        catch { }
    }




    /// <summary>
    /// 游戏服务器选择区域是否可见
    /// </summary>
    private bool _isGameBizDisplayPressed = false;


    /// <summary>
    /// 鼠标点击游戏服务器选择区域，显示游戏服务器选择菜单
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameBizDisplay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement ele)
        {
            e.Handled = true;
            _isGameBizDisplayPressed = !_isGameBizDisplayPressed;
            FlyoutBase.GetAttachedFlyout(ele)?.ShowAt(ele, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Bottom,
                ShowMode = FlyoutShowMode.Transient,
            });
        }
    }


    /// <summary>
    /// 鼠标移入到游戏服务器选择区域
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_GameBizDisplay_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement ele && _isGameBizDisplayPressed)
        {
            FlyoutBase.GetAttachedFlyout(ele)?.ShowAt(ele, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Bottom,
                ShowMode = FlyoutShowMode.Transient,
            });
        }
    }


    /// <summary>
    /// 点击服务器图标
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Button_GameServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon server)
            {
                if (CurrentGameBizIcon is not null)
                {
                    CurrentGameBizIcon.IsSelected = false;
                }

                if (GameBizIcons.FirstOrDefault(x => x.Key == server.Key) is GameBizIcon icon)
                {
                    CurrentGameBizIcon = icon;
                    CurrentGameBiz = icon.GameBiz;
                    CurrentGameId = icon.GameId;
                    icon.IsSelected = true;
                }
                else
                {
                    CurrentGameBizIcon = server;
                    server.IsSelected = true;
                }

                CurrentGameChanged?.Invoke(this, (server.GameId, false));
                // 关闭弹出的服务器选择菜单
                if (VisualTreeHelper.GetOpenPopupsForXamlRoot(this.XamlRoot).FirstOrDefault() is Popup popup)
                {
                    popup.IsOpen = false;
                }
                HideFullBackground();
                _isGameBizDisplayPressed = false;
                AppConfig.CurrentGameBiz = server.GameBiz;
            }
        }
        catch { }
    }


    /// <summary>
    /// 固定游戏服务器图标到待选区
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Button_PinGameBiz_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameBizIcon server)
        {
            if (GameBizIcons.FirstOrDefault(x => x.Key == server.Key) is GameBizIcon icon)
            {
                GameBizIcons.Remove(icon);
                server.IsPinned = false;
            }
            else
            {
                GameBizIcons.Add(server);
                server.IsPinned = true;
            }
        }
    }




    #endregion





    #region Installed Games


    /// <summary>
    /// 已安装游戏的实际占用空间
    /// </summary>
    public string? InstalledGamesActualSize { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 已安装游戏的总空间
    /// </summary>
    public string? InstalledGamesSavedSize { get; set => SetProperty(ref field, value); }




    public ObservableCollection<GameBizIcon> InstalledGames { get; set; } = new();



    private CancellationTokenSource? _initializeInstalledGamesCancellationTokenSource;


    /// <summary>
    /// 初始化已安装游戏列表，计算已安装游戏的实际占用空间
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task InitializeInstalledGamesAsync()
    {
        const double GB = 1 << 30;
        try
        {
            _initializeInstalledGamesCancellationTokenSource?.Cancel();
            _initializeInstalledGamesCancellationTokenSource = new();
            CancellationToken token = _initializeInstalledGamesCancellationTokenSource.Token;

            InstalledGames.Clear();
            InstalledGamesActualSize = null;
            InstalledGamesSavedSize = null;
            List<FileInfo> files = new();

            foreach (GameBizDisplay display in GameBizDisplays)
            {
                List<FileInfo> _duplicateFiles = new();
                int serverCount = 0;
                foreach (GameBizIcon server in display.Servers)
                {
                    string? installPath = GameLauncherService.GetGameInstallPath(server.GameId);
                    if (Directory.Exists(installPath))
                    {
                        server.InstallPath = installPath;
                        var _files = new DirectoryInfo(installPath).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                        server.TotalSize = _files.Sum(x => x.Length);
                        InstalledGames.Add(server);
                        if (_files.Count() > 0)
                        {
                            serverCount++;
                            _duplicateFiles.AddRange(_files);
                        }
                    }
                }
                if (serverCount > 1)
                {
                    files.AddRange(_duplicateFiles);
                }
            }
            long totalSize = InstalledGames.Sum(x => x.TotalSize);
            InstalledGamesActualSize = $"{totalSize / GB:F2}GB";

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (files.Count > 0)
            {
                (long fileSize, long actualSize) = await Task.Run(() =>
                {
                    long size = 0;
                    Dictionary<string, long> dic = new();
                    foreach (var file in files)
                    {
                        size += file.Length;
                        using var handle = File.OpenHandle(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var idInfo = Kernel32.GetFileInformationByHandleEx<Kernel32.FILE_ID_INFO>(handle, Kernel32.FILE_INFO_BY_HANDLE_CLASS.FileIdInfo);
                        var idInfoBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref idInfo, 1));
                        dic[Convert.ToHexString(idInfoBytes)] = file.Length;
                    }
                    return (size, dic.Values.Sum());
                }, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                InstalledGamesActualSize = $"{(totalSize - fileSize + actualSize) / GB:F2}GB";
                InstalledGamesSavedSize = $"{(fileSize - actualSize) / GB:F2}GB";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }




    /// <summary>
    /// 自动搜索已安装的游戏。
    /// 各游戏公司的注册表位置等细节由对应的 <see cref="IGameDiscoveryProvider"/> 负责，
    /// 游戏选择器不再自行判断。
    /// </summary>
    [RelayCommand]
    public async Task AutoSearchInstalledGamesAsync()
    {
        try
        {
            var keys = new List<string>();
            foreach (IGameDiscoveryProvider provider in _providerRegistry.DiscoveryProviders)
            {
                // 单个供应商失败不能影响其他供应商
                try
                {
                    IReadOnlyList<GameInstallation> installations = await provider.DiscoverAsync();
                    _logger.LogInformation("Provider {provider} found {count} installed game(s).", provider.ProviderId, installations.Count);
                    foreach (GameInstallation installation in installations)
                    {
                        // 配置文件中保存的仍是字符串键
                        if (_providerRegistry.GetGame(installation.Key)?.SettingsKey is string key && !string.IsNullOrWhiteSpace(key))
                        {
                            keys.Add(key);
                            _logger.LogInformation("Found installed game {key} at {path}", key, installation.InstallPath);
                        }
                        else
                        {
                            _logger.LogWarning("No descriptor for discovered game {key}", installation.Key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto search failed for provider {provider}", provider.ProviderId);
                }
            }
            // 与已固定的游戏合并，而不是整个替换：
            // 搜索不到的游戏（例如用户手动指定过目录的）不能因此被移除
            IEnumerable<string> existing = AppConfig.SelectedGameBizs?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            AppConfig.SelectedGameBizs = string.Join(',', existing.Concat(keys).Distinct());
            InitializeGameSelector();
            if (!IsPinned)
            {
                Pin();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto search installed games");
        }
    }




    /// <summary>
    /// 防止点击已安装游戏列表时，触发 <see cref="Border_FullBackground_Tapped(object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs)"/>
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Expander_InstalledGamesActualSize_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }



    #endregion







}


