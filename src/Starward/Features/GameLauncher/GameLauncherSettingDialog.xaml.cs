using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.GameInstall;
using Starward.Features.GameSelector;
using Starward.Features.HoYoPlay;
using Starward.Helpers;
using Starward.Providers.HoYo;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

#pragma warning disable MVVMTK0034 // Direct field reference to [ObservableProperty] backing field
#pragma warning disable MVVMTK0045 // Using [ObservableProperty] on fields is not AOT compatible for WinRT


namespace Starward.Features.GameLauncher;

[INotifyPropertyChanged]
public sealed partial class GameLauncherSettingDialog : ContentDialog
{


    private readonly ILogger<GameLauncherSettingDialog> _logger = AppConfig.GetLogger<GameLauncherSettingDialog>();


    private readonly HoYoPlayService _hoyoPlayService = AppConfig.GetService<HoYoPlayService>();

    private readonly IGameProviderRegistry _providerRegistry = AppConfig.GetService<IGameProviderRegistry>();


    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();


    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly GamePackageInfoProviderRegistry _packageInfoRegistry = AppConfig.GetService<GamePackageInfoProviderRegistry>();


    private readonly BackgroundService _backgroundService = AppConfig.GetService<BackgroundService>();


    public GameLauncherSettingDialog()
    {
        this.InitializeComponent();
        this.Loaded += GameLauncherSettingDialog_Loaded;
        this.Unloaded += GameLauncherSettingDialog_Unloaded;
    }



    /// <summary>
    /// 当前游戏，对话框的唯一身份来源
    /// </summary>
    public GameKey CurrentGameKey { get; set; }


    /// <summary>
    /// 兼容层：HoYoPlay 的游戏标识，由 <see cref="CurrentGameKey"/> 推导
    /// </summary>
    public GameId? CurrentGameId => HoYoGameIds.Resolve(CurrentGameKey);

    /// <summary>
    /// 已确定是米哈游游戏时使用：本对话框的在线功能都在能力判断之后才会执行。
    /// 假设不成立时立刻失败，而不是留下空引用。
    /// </summary>
    private GameId RequiredGameId => CurrentGameId
        ?? throw new GameCapabilityNotSupportedException(CurrentGameKey, GameCapability.Install,
                                                        $"Game '{CurrentGameKey}' has no HoYoPlay game id.");


    /// <summary>
    /// 安装器使用的游戏标识，所有能由 Starward 安装的游戏都有，见 <see cref="InstallGameIds"/>
    /// </summary>
    private GameId? InstallGameId => InstallGameIds.Resolve(CurrentGameKey);



    public GameBiz CurrentGameBiz { get; set; }




    private void FlipView_Settings_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = VisualTreeHelper.GetChild(FlipView_Settings, 0);
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
                        else if (child is ScrollViewer scrollViewer)
                        {
                            scrollViewer.PointerWheelChanged += (_, e) => e.Handled = true;
                        }
                    }
                }
            }
        }
        catch { }
    }



    private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            if (args.InvokedItemContainer?.Tag is string index && int.TryParse(index, out int target))
            {
                int steps = target - FlipView_Settings.SelectedIndex;
                if (steps > 0)
                {
                    for (int i = 0; i < steps; i++)
                    {
                        FlipView_Settings.SelectedIndex++;
                    }
                }
                else
                {
                    for (int i = 0; i < -steps; i++)
                    {
                        FlipView_Settings.SelectedIndex--;
                    }
                }
            }
        }
        catch { }
    }




    private async void GameLauncherSettingDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 不能用 RequiredGameId：它对非米哈游游戏会直接抛出，整个对话框就初始化不起来了。
        // 存储键与页面一致，米哈游是旧的 GameBiz，其他游戏是 GameKey 的正规字符串。
        CurrentGameBiz = CurrentGameId?.GameBiz ?? new GameBiz(CurrentGameKey.IsValid ? GameKeyResolver.ToSettingsKey(CurrentGameKey) : "");
        CheckCanRepairGame();
        await InitializeBasicInfoAsync();
        InitializeStartArgument();
        InitializeCustomBg();
        await InitializeGamePackagesAsync();
    }


    private void GameLauncherSettingDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        LatestPackageGroups = null!;
        PreInstallPackageGroups = null!;
        FlipView_Settings.Items.Clear();
    }




    [RelayCommand]
    private void Close()
    {
        this.Hide();
    }





    #region 基本信息



    private bool? _hasAudioPackages;


    public bool CanRepairGame { get; set => SetProperty(ref field, value); } = true;


    public GameBizIcon CurrentGameBizIcon { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 安装路径
    /// </summary>
    public string? InstallPath { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 文件夹大小
    /// </summary>
    public string? GameSize { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 是否可以卸载和修复
    /// </summary>
    public bool UninstallAndRepairEnabled { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 是否启用公告
    /// </summary>
    public bool EnableBannerAndPost
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.EnableBannerAndPost = value;
                WeakReferenceMessenger.Default.Send(new GameAnnouncementSettingChangedMessage());
            }
        }
    } = AppConfig.EnableBannerAndPost;


    /// <summary>
    /// 是否启用游戏公告红点
    /// </summary>
    public bool DisableGameNoticeRedHot
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.DisableGameNoticeRedHot = value;
                WeakReferenceMessenger.Default.Send(new GameAnnouncementSettingChangedMessage());
            }
        }
    } = AppConfig.DisableGameNoticeRedHot;


    /// <summary>
    /// 使用 CMD 启动游戏
    /// </summary>
    public bool StartGameWithCMD
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartGameWithCMD = value;
            }
        }
    } = AppConfig.StartGameWithCMD;




    private async Task InitializeBasicInfoAsync()
    {
        try
        {
            if (_providerRegistry.GetGame(CurrentGameKey) is GameDescriptor descriptor)
            {
                CurrentGameBizIcon = new GameBizIcon(descriptor);
            }
            InstallPath = GameLauncherService.GetGameInstallPath(CurrentGameKey, out bool storageRemoved);
            GameSize = GetSize(InstallPath);
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameKey) is null)
            {
                // 修复与卸载都要经过安装器，只支持启动的游戏没有安装器可用
                UninstallAndRepairEnabled = InstallPath != null && !storageRemoved && SupportsInstall;
            }
            else
            {
                UninstallAndRepairEnabled = false;
            }
            await InitializeAudioLanguageAsync();
            await InitializeResourceTiersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeBasicInfoAsync ({biz})", CurrentGameBiz);
        }
    }



    private static string? GetSize(string? path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }
        var size = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        var gb = (double)size / (1 << 30);
        return $"{gb:F2}GB";
    }




    /// <summary>
    /// 该游戏能不能由 Starward 安装、修复、卸载
    /// </summary>
    private bool SupportsInstall => _providerRegistry.SupportsCapability(CurrentGameKey, GameCapability.Install);


    /// <summary>
    /// 该游戏是否走 HoYoPlay 的在线安装包接口。
    /// 音频语言与游戏资源这两个区块只对它们有意义，其他游戏的语音随本体一起下载。
    /// </summary>
    private bool IsHoYoPlayGame => CurrentGameId is not null && SupportsInstall;

    private async Task InitializeAudioLanguageAsync()
    {
        try
        {
            if (!IsHoYoPlayGame)
            {
                // 没有语音包可选，修复按钮直接开始
                _hasAudioPackages = false;
                return;
            }
            GameConfig? config = await _hoyoPlayService.GetGameConfigAsync(RequiredGameId);
            if (config is not null)
            {
                if (!string.IsNullOrWhiteSpace(config.AudioPackageScanDir))
                {
                    _hasAudioPackages = true;
                    Segmented_SelectLanguage.SelectedItems.Clear();
                    AudioLanguage audioLanguage = await _gamePackageService.GetAudioLanguageAsync(RequiredGameId, InstallPath);
                    if (audioLanguage.HasFlag(AudioLanguage.Chinese))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Chinese);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.English))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_English);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.Japanese))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Japanese);
                    }
                    if (audioLanguage.HasFlag(AudioLanguage.Korean))
                    {
                        Segmented_SelectLanguage.SelectedItems.Add(SegmentedItem_Korean);
                    }
                }
                else
                {
                    _hasAudioPackages = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InitializeAudioLanguageAsync ({biz})", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 打开游戏安装文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenInstalGameFolderAsync()
    {
        try
        {
            if (Directory.Exists(InstallPath))
            {
                await Launcher.LaunchUriAsync(new Uri(InstallPath));
            }
        }
        catch { }
    }


    /// <summary>
    /// 删除游戏安装路径
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task DeleteGameInstllPathAsync()
    {
        try
        {
            GameLauncherService.ChangeGameInstallPath(CurrentGameKey, null);
            WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
            await InitializeBasicInfoAsync();
            await TryStopGameInstallTaskAsync();
        }
        catch { }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task LocateGameAsync()
    {
        try
        {
            string? previousInstallPath = InstallPath;
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (DriveHelper.GetDriveType(folder) is DriveType.Network && !new Uri(folder).IsUnc)
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TextBlock_NetworkDriveWarning.Visibility = Visibility.Collapsed;
                    GameLauncherService.ChangeGameInstallPath(CurrentGameKey, folder);
                    await InitializeBasicInfoAsync();
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    if (previousInstallPath != folder)
                    {
                        await TryStopGameInstallTaskAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Locate game failed {GameBiz}", CurrentGameBiz);
        }
    }



    /// <summary>
    /// 检查是否可以修复游戏
    /// </summary>
    private void CheckCanRepairGame()
    {
        if (InstallGameId is GameId installGameId && _gameInstallService.GetGameInstallTask(installGameId) is GameInstallContext task)
        {
            if (task.State is not GameInstallState.Stop and not GameInstallState.Finish)
            {
                Button_RepairGame.IsEnabled = false;
            }
        }
    }



    /// <summary>
    /// 修复游戏
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task RepairGameAsync()
    {
        if (_hasAudioPackages is null)
        {
            return;
        }
        if (_hasAudioPackages.Value && Button_StartRepairing.Visibility is Visibility.Collapsed)
        {
            Segmented_SelectLanguage.Visibility = Visibility.Visible;
            Button_StartRepairing.Visibility = Visibility.Visible;
            Button_RepairGame.Visibility = Visibility.Collapsed;
        }
        else
        {
            await RepairGameInternalAsync();
        }
    }



    [RelayCommand]
    private async Task RepairGameInternalAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath) || InstallGameId is not GameId installGameId)
            {
                return;
            }
            AudioLanguage audio = AudioLanguage.None;
            foreach (SegmentedItem item in Segmented_SelectLanguage.SelectedItems.Cast<SegmentedItem>())
            {
                audio |= item.Tag switch
                {
                    "zh-cn" => AudioLanguage.Chinese,
                    "en-us" => AudioLanguage.English,
                    "ja-jp" => AudioLanguage.Japanese,
                    "ko-kr" => AudioLanguage.Korean,
                    _ => AudioLanguage.None,
                };
            }
            GameInstallContext? task = await _gameInstallService.StartRepairAsync(installGameId, InstallPath, audio);
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair game internal {GameBiz}", CurrentGameBiz);
        }
    }




    #region 资源分级



    private const double GB = 1 << 30;


    /// <summary>
    /// 可选的资源分级与大小，没有分级的游戏为 null
    /// </summary>
    private IReadOnlyList<GameResourceTierPackage>? _resourceTiers;


    /// <summary>
    /// 本机装好的分级
    /// </summary>
    private IReadOnlyList<string> _installedTiers = [];


    /// <summary>
    /// 现在不能变更分级的原因（例如还没更新），可以时为 null
    /// </summary>
    private string? _resourceTierBlockedReason;


    /// <summary>
    /// 程序代码在填选项时也会触发 SelectionChanged，那时不算玩家改了选择
    /// </summary>
    private bool _initializingResourceTiers;


    public string? ResourceTierChangeText { get; set => SetProperty(ref field, value); }


    public bool CanApplyResourceTiers { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 鸣潮的资源分级：勾选想要的几档，套用后交给安装器加装缺的、删掉不要的。
    /// <para/>
    /// 不按游戏判断：安装包信息给得出分级、启动 Provider 认得本机装了哪几档，才显示这一块。
    /// 只在已是最新版本时开放：安装器把「本机已是目标版本」当成只变更分级，原有文件只看大小，
    /// 版本落后时变更分级等于不打补丁地整包重下。
    /// </summary>
    private async Task InitializeResourceTiersAsync()
    {
        try
        {
            StackPanel_ResourceTiers.Visibility = Visibility.Collapsed;
            if (IsHoYoPlayGame
                || InstallPath is null
                || !SupportsInstall
                || _packageInfoRegistry.GetProvider(CurrentGameKey) is not IGamePackageInfoProvider provider
                || _providerRegistry.GetLaunchProvider(CurrentGameKey.ProviderId) is not IGameResourceTierProvider tierProvider)
            {
                return;
            }
            IReadOnlyList<GameResourceTierPackage>? tiers = await provider.GetResourceTiersAsync(CurrentGameKey);
            if (tiers is not { Count: > 0 } || InstallPath is null)
            {
                return;
            }
            _resourceTiers = tiers;
            _installedTiers = tierProvider.GetResourceTierState(CurrentGameKey, InstallPath).InstalledTiers;
            GamePackageState? state = await provider.GetStateAsync(CurrentGameKey, InstallPath);
            // 3.7.0 以前的安装没有分级目录，更新上来才会有。
            // 读不到版本状态时也不开放：分不清是否最新，而版本落后时变更分级会整包重下
            _resourceTierBlockedReason = _installedTiers.Count == 0 || state is not { UpdateAvailable: false }
                                       ? Lang.GameLauncherSettingDialog_UpdateBeforeChangingResourceTiers
                                       : null;

            _initializingResourceTiers = true;
            Segmented_ResourceTiers.Items.Clear();
            foreach (GameResourceTierPackage tier in tiers)
            {
                var item = new SegmentedItem { Content = GameResourceTierNames.Get(tier.Tier), Tag = tier.Tier };
                Segmented_ResourceTiers.Items.Add(item);
                if (_installedTiers.Contains(tier.Tier))
                {
                    Segmented_ResourceTiers.SelectedItems.Add(item);
                }
            }
            _initializingResourceTiers = false;
            StackPanel_ResourceTiers.Visibility = Visibility.Visible;
            UpdateResourceTierChange();
        }
        catch (Exception ex)
        {
            _initializingResourceTiers = false;
            _logger.LogError(ex, "Initialize resource tiers ({biz})", CurrentGameBiz);
        }
    }


    private List<string> GetDesiredResourceTiers()
    {
        return Segmented_ResourceTiers.SelectedItems.Cast<SegmentedItem>().Select(x => x.Tag as string).OfType<string>().ToList();
    }


    private void Segmented_ResourceTiers_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializingResourceTiers)
        {
            UpdateResourceTierChange();
        }
    }


    /// <summary>
    /// 说明这次变更要下载多少、删掉多少，决定能不能套用
    /// </summary>
    private void UpdateResourceTierChange()
    {
        CanApplyResourceTiers = false;
        if (_resourceTiers is null)
        {
            ResourceTierChangeText = null;
            return;
        }
        if (_resourceTierBlockedReason is not null)
        {
            ResourceTierChangeText = _resourceTierBlockedReason;
            return;
        }
        List<string> desired = GetDesiredResourceTiers();
        if (desired.Count == 0)
        {
            ResourceTierChangeText = Lang.GameLauncherSettingDialog_KeepAtLeastOneResourceTier;
            return;
        }
        long download = desired.Except(_installedTiers).Sum(x => _resourceTiers.FirstOrDefault(y => y.Tier == x)?.TierBytes ?? 0);
        long delete = _installedTiers.Except(desired).Sum(x => _resourceTiers.FirstOrDefault(y => y.Tier == x)?.TierBytes ?? 0);
        var parts = new List<string>();
        if (download > 0)
        {
            parts.Add(string.Format(Lang.GameLauncherSettingDialog_ResourceTierDownloadSize, $"{download / GB:F2} GB"));
        }
        if (delete > 0)
        {
            parts.Add(string.Format(Lang.GameLauncherSettingDialog_ResourceTierDeleteSize, $"{delete / GB:F2} GB"));
        }
        ResourceTierChangeText = parts.Count > 0 ? string.Join("  ·  ", parts) : null;
        // 游戏在跑、或已有任务在进行时（修复按钮也因此不能按）都不能动文件
        CanApplyResourceTiers = parts.Count > 0 && UninstallAndRepairEnabled && Button_RepairGame.IsEnabled;
    }


    [RelayCommand]
    private async Task ApplyResourceTiersAsync()
    {
        try
        {
            if (!Directory.Exists(InstallPath) || InstallGameId is not GameId installGameId)
            {
                return;
            }
            List<string> desired = GetDesiredResourceTiers();
            if (desired.Count == 0)
            {
                return;
            }
            GameInstallContext? task = await _gameInstallService.StartInstallAsync(installGameId, InstallPath, AudioLanguage.None, string.Join(',', desired));
            if (task is not null && task.State is not GameInstallState.Stop and not GameInstallState.Error)
            {
                WeakReferenceMessenger.Default.Send(new GameInstallTaskStartedMessage(task));
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apply resource tiers {GameBiz}", CurrentGameBiz);
        }
    }



    #endregion




    public string? UninstallError { get; set => SetProperty(ref field, value); }



    [RelayCommand]
    private void ShowUninstallGameWarning()
    {
        try
        {
            if (Directory.Exists(InstallPath))
            {
                string installPath = Path.GetFullPath(InstallPath);
                if (Path.GetPathRoot(InstallPath) == InstallPath)
                {
                    // 不能删除驱动器根目录
                    UninstallError = Lang.GameLauncherSettingDialog_CannotDeleteTheDriveRootDirectory;
                    return;
                }
                if (Directory.Exists(AppConfig.UserDataFolder))
                {
                    string userDataFolder = Path.GetFullPath(AppConfig.UserDataFolder);
                    if (userDataFolder.StartsWith(installPath))
                    {
                        // Starward 数据文件夹位于游戏文件夹内，删除游戏时会一并删除。请在设置页面修改数据文件夹位置后重试。
                        UninstallError = Lang.GameLauncherSettingDialog_UninstallGameUserDataFolderWarning;
                        return;
                    }
                }
                string baseFolder = AppContext.BaseDirectory.TrimEnd('/', '\\');
                if (baseFolder.StartsWith(installPath))
                {
                    // Starward 程序位于游戏文件夹内，删除游戏时会一并被删除。请将程序移出游戏文件夹后重试。
                    UninstallError = Lang.GameLauncherSettingDialog_UninstallGameStarwardProgramFolderWarning;
                    return;
                }
                Grid_UninstallWarning.Visibility = Visibility.Visible;
            }
            else
            {
                _ = InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Show uninstall game warning {GameBiz}", CurrentGameBiz);
        }
    }



    [RelayCommand]
    private async Task UninstallGameAsync()
    {
        try
        {
            UninstallError = null;
            if (await _gameLauncherService.GetGameProcessAsync(CurrentGameKey) is not null)
            {
                UninstallError = Lang.LauncherPage_GameIsRunning;
                await InitializeBasicInfoAsync();
                return;
            }
            if (Directory.Exists(InstallPath) && InstallGameId is GameId installGameId)
            {
                if (await _gameInstallService.StartUninstallAsync(installGameId, InstallPath))
                {
                    _logger.LogInformation("""
                        Uninstall game finished:
                        GameId: {gameId} {gameBiz}
                        InstallPath: {installPath}
                        """, installGameId.Id, installGameId.GameBiz, InstallPath);
                    // 米哈游游戏的整个安装目录都删掉了，路径自然失效；
                    // 鸣潮这类游戏的安装路径是官方启动器的根目录，删完游戏它还在，要主动忘掉
                    GameLauncherService.ChangeGameInstallPath(CurrentGameKey, null);
                    Grid_UninstallWarning.Visibility = Visibility.Collapsed;
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                    CheckCanRepairGame();
                    await InitializeBasicInfoAsync();
                }
            }
            else
            {
                await InitializeBasicInfoAsync();
            }
        }
        catch (Exception ex)
        {
            UninstallError = ex.Message;
            _logger.LogError(ex, "Uninstall game failed {GameBiz}", CurrentGameBiz);
        }
    }



    private void Segmented_SelectLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is Segmented segmented)
        {
            CanRepairGame = segmented.SelectedItems.Count > 0;
        }
    }



    private async Task TryStopGameInstallTaskAsync()
    {
        try
        {
            if (InstallGameId is GameId installGameId && _gameInstallService.GetGameInstallTask(installGameId) is GameInstallContext task)
            {
                if (task.State is not GameInstallState.Stop and not GameInstallState.Finish)
                {
                    await _gameInstallService.StopTaskAsync(task);
                    await Task.Delay(1000);
                    CheckCanRepairGame();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Try stop game install task {GameBiz}", CurrentGameBiz);
        }
    }




    #endregion




    #region 启动参数


    /// <summary>
    /// 命令行启动参数
    /// </summary>
    [ObservableProperty]
    public string? _StartGameArgument;
    partial void OnStartGameArgumentChanged(string? value)
    {
        AppConfig.SetStartArgument(CurrentGameBiz, value);
    }


    /// <summary>
    /// 启动游戏后的操作
    /// </summary>
    public int StartGameAction
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.StartGameAction = (StartGameAction)value;
            }
        }
    } = Math.Clamp((int)AppConfig.StartGameAction, 0, 2);


    /// <summary>
    /// 是否启用第三方工具
    /// </summary>
    [ObservableProperty]
    public bool _EnableThirdPartyTool;
    partial void OnEnableThirdPartyToolChanged(bool value)
    {
        AppConfig.SetEnableThirdPartyTool(CurrentGameBiz, value);
    }


    /// <summary>
    /// 第三方工具路径
    /// </summary>
    [ObservableProperty]
    public string? _ThirdPartyToolPath;
    partial void OnThirdPartyToolPathChanged(string? value)
    {
        try
        {
            GameLauncherService.SetThirdPartyToolPath(CurrentGameKey, value);
        }
        catch { }
    }



    private void InitializeStartArgument()
    {
        _StartGameArgument = AppConfig.GetStartArgument(CurrentGameBiz);
        _EnableThirdPartyTool = AppConfig.GetEnableThirdPartyTool(CurrentGameBiz);
        _ThirdPartyToolPath = GameLauncherService.GetThirdPartyToolPath(CurrentGameKey);
        OnPropertyChanged(nameof(StartGameArgument));
        OnPropertyChanged(nameof(EnableThirdPartyTool));
        OnPropertyChanged(nameof(ThirdPartyToolPath));
    }



    /// <summary>
    /// 修改第三方启动工具路径
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task ChangeThirdPartyPathAsync()
    {
        try
        {
            var file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot);
            if (File.Exists(file))
            {
                ThirdPartyToolPath = file;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change third party tool path ({biz})", CurrentGameBiz);
        }
    }


    /// <summary>
    /// 打开第三方工具文件夹
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenThirdPartyToolFolderAsync()
    {
        try
        {
            if (File.Exists(ThirdPartyToolPath))
            {
                var folder = Path.GetDirectoryName(ThirdPartyToolPath);
                var file = await StorageFile.GetFileFromPathAsync(ThirdPartyToolPath);
                var option = new FolderLauncherOptions();
                option.ItemsToSelect.Add(file);
                await Launcher.LaunchFolderPathAsync(folder, option);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open third party tool folder {folder}", ThirdPartyToolPath);
        }
    }


    /// <summary>
    /// 删除第三方工具路径
    /// </summary>
    [RelayCommand]
    private void DeleteThirdPartyToolPath()
    {
        ThirdPartyToolPath = null;
    }





    #endregion




    #region 自定义背景



    /// <summary>
    /// 是否启用自定义背景
    /// </summary>
    [ObservableProperty]
    public bool _EnableCustomBg;
    partial void OnEnableCustomBgChanged(bool value)
    {
        AppConfig.SetEnableCustomBg(CurrentGameBiz, value);
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }


    /// <summary>
    /// 自定义背景，文件名，存储在 UserDataFolder/bg
    /// </summary>
    public string? CustomBg { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 修改背景错误信息
    /// </summary>
    public string? ChangeBgError { get; set => SetProperty(ref field, value); }


    private void InitializeCustomBg()
    {
        _EnableCustomBg = AppConfig.GetEnableCustomBg(CurrentGameBiz);
        CustomBg = AppConfig.GetCustomBg(CurrentGameBiz);
        OnPropertyChanged(nameof(EnableCustomBg));
    }



    /// <summary>
    /// 修改自定义背景
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task ChangeCustomBgAsync()
    {
        try
        {
            ChangeBgError = null;
            string? name = await _backgroundService.ChangeCustomBackgroundFileAsync(this.XamlRoot);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            CustomBg = name;
            AppConfig.SetCustomBg(CurrentGameBiz, name);
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
    }



    /// <summary>
    /// 打开自定义背景文件
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private async Task OpenCustomBgAsync()
    {
        try
        {
            string path = Path.Join(AppConfig.CacheFolder, "bg", CustomBg);
            if (File.Exists(path))
            {
                await Launcher.LaunchUriAsync(new Uri(path));
            }
        }
        catch { }
    }



    /// <summary>
    /// 删除自定义背景
    /// </summary>
    [RelayCommand]
    private void DeleteCustomBg()
    {
        CustomBg = null;
        AppConfig.SetCustomBg(CurrentGameBiz, null);
        WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
    }



    /// <summary>
    /// 视频背景音量
    /// </summary>
    public int VideoBgVolume
    {
        get; set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(VideoBgVolumeButtonIcon));
                WeakReferenceMessenger.Default.Send(new VideoBgVolumeChangedMessage(value));
                AppConfig.VideoBgVolume = value;
            }
        }
    } = AppConfig.VideoBgVolume;



    /// <summary>
    /// 音量图标
    /// </summary>
    public string VideoBgVolumeButtonIcon => VideoBgVolume switch
    {
        > 66 => "\uE995",
        > 33 => "\uE994",
        > 1 => "\uE993",
        _ => "\uE992",
    };


    private int notMuteVolume = 100;

    /// <summary>
    /// 静音
    /// </summary>
    [RelayCommand]
    private void Mute()
    {
        if (VideoBgVolume > 0)
        {
            notMuteVolume = VideoBgVolume;
            VideoBgVolume = 0;
        }
        else
        {
            VideoBgVolume = notMuteVolume;
        }
    }





    /// <summary>
    /// 接受拖放文件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Grid_BackgroundDragIn_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }



    /// <summary>
    /// 拖放文件，修改自定义背景
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void Grid_BackgroundDragIn_Drop(object sender, DragEventArgs e)
    {
        ChangeBgError = null;
        var defer = e.GetDeferral();
        try
        {
            if ((await e.DataView.GetStorageItemsAsync()).FirstOrDefault() is StorageFile file)
            {
                string? name = await BackgroundService.ChangeCustomBackgroundFileAsync(file);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }
                CustomBg = name;
                AppConfig.SetCustomBg(CurrentGameBiz, name);
                if (EnableCustomBg)
                {
                    WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
                }
                else
                {
                    EnableCustomBg = true;
                }
            }
        }
        catch (COMException ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_CannotDecodeFile;
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            ChangeBgError = Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs;
            _logger.LogError(ex, "Change custom background failed");
        }
        defer.Complete();
    }



    #endregion




    #region 游戏包体



    /// <summary>
    /// 最新版本
    /// </summary>
    public string LatestVersion { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 最新版本包体
    /// </summary>
    public List<PackageGroup> LatestPackageGroups { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 预下载版本
    /// </summary>
    public string PreInstallVersion { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 预下载版本包体
    /// </summary>
    public List<PackageGroup> PreInstallPackageGroups { get; set => SetProperty(ref field, value); }




    private async Task InitializeGamePackagesAsync()
    {
        try
        {
            if (!IsHoYoPlayGame)
            {
                return;
            }
            var gamePackage = await _hoyoPlayService.GetGamePackageAsync(RequiredGameId);
            LatestVersion = gamePackage.Main.Major!.Version;
            var list = GetGameResourcePackageGroups(gamePackage.Main);
            var sdk = await _hoyoPlayService.GetGameChannelSDKAsync(RequiredGameId);
            if (sdk is not null)
            {
                list.Add(new PackageGroup
                {
                    Name = "Channel SDK",
                    Items = [new PackageItem
                    {
                        FileName = Path.GetFileName(sdk.ChannelSDKPackage.Url),
                        Url = sdk.ChannelSDKPackage.Url,
                        Md5 = sdk.ChannelSDKPackage.MD5,
                        PackageSize = sdk.ChannelSDKPackage.Size,
                        DecompressSize = sdk.ChannelSDKPackage.DecompressedSize,
                    }],
                });
            }
            // todo plugin
            LatestPackageGroups = list;
            if (!string.IsNullOrWhiteSpace(gamePackage.PreDownload?.Major?.Version))
            {
                PreInstallVersion = gamePackage.PreDownload.Major.Version;
                PreInstallPackageGroups = GetGameResourcePackageGroups(gamePackage.PreDownload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get game resource failed, gameBiz: {gameBiz}", CurrentGameBiz);
        }
    }



    private List<PackageGroup> GetGameResourcePackageGroups(GamePackageVersion gameResource)
    {
        var list = new List<PackageGroup>();
        var fullPackageGroup = new PackageGroup
        {
            Name = Lang.GameResourcePage_FullPackages,
            Items = new List<PackageItem>()
        };
        foreach (var item in gameResource.Major?.GamePackages ?? [])
        {
            fullPackageGroup.Items.Add(new PackageItem
            {
                FileName = Path.GetFileName(item.Url),
                Url = item.Url,
                Md5 = item.MD5,
                PackageSize = item.Size,
                DecompressSize = item.DecompressedSize,
            });
        }
        foreach (var item in gameResource.Major?.AudioPackages ?? [])
        {
            fullPackageGroup.Items.Add(new PackageItem
            {
                FileName = Path.GetFileName(item.Url),
                Url = item.Url,
                Md5 = item.MD5,
                PackageSize = item.Size,
                DecompressSize = item.DecompressedSize,
            });
        }
        list.Add(fullPackageGroup);

        foreach (var patch in gameResource.Patches ?? [])
        {
            var diffPackageGroup = new PackageGroup
            {
                Name = $"{Lang.GameResourcePage_DiffPackages}  {patch.Version}",
                Items = new List<PackageItem>()
            };
            foreach (var item in patch.GamePackages ?? [])
            {
                diffPackageGroup.Items.Add(new PackageItem
                {
                    FileName = Path.GetFileName(item.Url),
                    Url = item.Url,
                    Md5 = item.MD5,
                    PackageSize = item.Size,
                    DecompressSize = item.DecompressedSize,
                });
            }
            foreach (var item in patch.AudioPackages ?? [])
            {
                diffPackageGroup.Items.Add(new PackageItem
                {
                    FileName = Path.GetFileName(item.Url),
                    Url = item.Url,
                    Md5 = item.MD5,
                    PackageSize = item.Size,
                    DecompressSize = item.DecompressedSize,
                });
            }
            list.Add(diffPackageGroup);
        }
        return list;
    }



    private async void Button_CopyUrl_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            if (sender is Button button)
            {
                if (button.DataContext is PackageGroup group)
                {
                    if (group.Items is not null)
                    {
                        var sb = new StringBuilder();
                        foreach (var item in group.Items)
                        {
                            if (!string.IsNullOrEmpty(item.Url))
                            {
                                sb.AppendLine(item.Url);
                            }
                        }
                        string url = sb.ToString().TrimEnd();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            ClipboardHelper.SetText(url);
                            await CopySuccessAsync(button);
                        }
                    }
                }
                if (button.DataContext is PackageItem package)
                {
                    if (!string.IsNullOrEmpty(package.Url))
                    {
                        ClipboardHelper.SetText(package.Url);
                        await CopySuccessAsync(button);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url failed");
        }
    }



    private async Task CopySuccessAsync(Button button)
    {
        try
        {
            button.IsEnabled = false;
            if (button.Content is FontIcon icon)
            {
                // Accpet
                icon.Glyph = "\uF78C";
                await Task.Delay(1000);
            }
        }
        finally
        {
            button.IsEnabled = true;
            if (button.Content is FontIcon icon)
            {
                // Link
                icon.Glyph = "\uE71B";
            }
        }
    }




    public class PackageGroup
    {
        public string Name { get; set; }

        public List<PackageItem> Items { get; set; }
    }



    public class PackageItem
    {
        public string FileName { get; set; }

        public string Url { get; set; }

        public string Md5 { get; set; }

        public long PackageSize { get; set; }

        public long DecompressSize { get; set; }

        public string PackageSizeString => GetSizeString(PackageSize);

        public string DecompressSizeString => GetSizeString(DecompressSize);

        private string GetSizeString(long size)
        {
            const double KB = 1 << 10;
            const double MB = 1 << 20;
            const double GB = 1 << 30;
            if (size >= GB)
            {
                return $"{size / GB:F2} GB";
            }
            else if (size >= MB)
            {
                return $"{size / MB:F2} MB";
            }
            else
            {
                return $"{size / KB:F2} KB";
            }
        }
    }



    #endregion





    private void TextBlock_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (sender.FontSize > 12)
        {
            sender.FontSize -= 1;
        }
    }


}
