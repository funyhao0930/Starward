using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.CloudGame;
using Starward.Features.GameInstall;
using Starward.Features.HoYoPlay;
using Starward.Features.Overlay;
using Starward.Features.ViewHost;
using Starward.Frameworks;
using Starward.Helpers;
using Starward.RPC.GameInstall;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.GameLauncher;

public sealed partial class GameLauncherPage : PageBase
{


    private readonly ILogger<GameLauncherPage> _logger = AppConfig.GetLogger<GameLauncherPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GamePackageService _gamePackageService = AppConfig.GetService<GamePackageService>();

    private readonly BackgroundService _backgroundService = AppConfig.GetService<BackgroundService>();

    private readonly GameInstallService _gameInstallService = AppConfig.GetService<GameInstallService>();

    private readonly HoYoPlayService _hoYoPlayService = AppConfig.GetService<HoYoPlayService>();


    private readonly IGameProviderRegistry _providerRegistry = AppConfig.GetService<IGameProviderRegistry>();


    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _dispatchTimer;


    public GameLauncherPage()
    {
        this.InitializeComponent();
        _dispatchTimer = DispatcherQueue.CreateTimer();
        _dispatchTimer.Interval = TimeSpan.FromMilliseconds(100);
        _dispatchTimer.Tick += UpdateGameInstallTaskProgress;
    }



    protected override void OnLoaded()
    {
        InitializeGameFeature();
        CheckGameVersion();
        UpdateGameInstallTask();
        CheckCloudGame();
        _ = InitializeGameServerAsync();
        _ = InitializeBackgameImageSwitcherAsync();
        WeakReferenceMessenger.Default.Register<GameInstallPathChangedMessage>(this, OnGameInstallPathChanged);
        WeakReferenceMessenger.Default.Register<MainWindowStateChangedMessage>(this, OnMainWindowStateChanged);
        WeakReferenceMessenger.Default.Register<RemovableStorageDeviceChangedMessage>(this, OnRemovableStorageDeviceChanged);
        WeakReferenceMessenger.Default.Register<GameInstallTaskStartedMessage>(this, OnGameInstallTaskStarted);
        WeakReferenceMessenger.Default.Register<BackgroundChangedMessage>(this, OnBackgroundChanged);
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _dispatchTimer.Tick -= UpdateGameInstallTaskProgress;
        _dispatchTimer.Stop();
        BackgroundImages = null!;
    }




    private void InitializeGameFeature()
    {
        GameFeatureConfig feature = GameFeatureConfig.FromGameKey(CurrentGameKey);
        if (feature.SupportCloudGame)
        {
            Button_CloudGame.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        if (feature.SupportGameAccountSwitcher && AppConfig.EnableGameAccountSwitcher)
        {
            EnableGameAccountSwitcher = true;
        }
    }


    public bool EnableGameAccountSwitcher { get; set => SetProperty(ref field, value); }



    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstalledLocateGameEnabled))]
    public partial GameState GameState { get; set; }




    [RelayCommand]
    private async Task ClickStartGameButtonAsync()
    {
        await Task.Delay(1);
        switch (GameState)
        {
            case GameState.None:
                break;
            case GameState.StartGame:
                await StartGameAsync();
                break;
            case GameState.GameIsRunning:
            case GameState.InstallGame:
                await InstallGameAsync();
                break;
            case GameState.Installing:
                await ChangeGameInstallTaskStateAsync();
                break;
            case GameState.UpdateGame:
                await UpdateGameAsync();
                break;
            case GameState.UpdatePlugin:
            case GameState.ResumeDownload:
                await ResumeDownloadAsync();
                break;
            case GameState.ComingSoon:
                break;
            default:
                break;
        }
    }




    #region Game Server


    public List<GameServerConfig>? GameServers { get; set => SetProperty(ref field, value); }

    [ObservableProperty]
    public partial GameServerConfig? SelectedGameServer { get; set; }
    partial void OnSelectedGameServerChanged(GameServerConfig? oldValue, GameServerConfig? newValue)
    {
        if (oldValue is not null && newValue is not null)
        {
            AppConfig.LastGameIdOfBH3Global = newValue.GameId;
            WeakReferenceMessenger.Default.Send(new BH3GlobalGameServerChangedMessage(newValue.GameId));
        }
    }


    /// <summary>
    /// 该游戏是否有 HoYoPlay 那样的在线接口。只支持启动的游戏没有。
    /// </summary>
    private bool SupportsPackageApi => _providerRegistry.SupportsCapability(CurrentGameKey, GameCapability.Install);


    /// <summary>
    /// 初始化区服选项，仅崩坏三国际服使用
    /// </summary>
    /// <returns></returns>
    private async Task InitializeGameServerAsync()
    {
        try
        {
            if (!SupportsPackageApi)
            {
                return;
            }
            GameInfo? gameInfo;
            if (CurrentGameBiz == GameBiz.bh3_global)
            {
                gameInfo = await _hoYoPlayService.GetGameInfoAsync(GameId.FromGameBiz(GameBiz.bh3_global)!);
            }
            else
            {
                gameInfo = await _hoYoPlayService.GetGameInfoAsync(RequiredGameId);
            }
            if (gameInfo?.GameServerConfigs?.Count > 0)
            {
                GameServers = gameInfo.GameServerConfigs;
                if (GameServers.FirstOrDefault(x => x.GameId == RequiredGameId.Id) is GameServerConfig config)
                {
                    SelectedGameServer = config;
                }
                else
                {
                    SelectedGameServer = GameServers.FirstOrDefault();
                    if (SelectedGameServer is not null)
                    {
                        // 记录选择而不是就地改写身份对象，HoYoGameIds 解析时会读取它
                        AppConfig.LastGameIdOfBH3Global = SelectedGameServer.GameId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize game server");
        }
    }



    #endregion




    #region Game Version


    public string? GameInstallPath { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 可移动存储设备提示
    /// </summary>
    public bool IsInstallPathRemovableTipEnabled { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 已安装？定位游戏
    /// </summary>
    public bool InstalledLocateGameEnabled => GameState is GameState.InstallGame && !IsInstallPathRemovableTipEnabled;

    /// <summary>
    /// 预下载按钮是否可用
    /// </summary>
    public bool IsPredownloadButtonEnabled { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 预下载是否完成
    /// </summary>
    public bool IsPredownloadFinished { get; set => SetProperty(ref field, value); }


    private Version? localGameVersion;


    private Version? latestGameVersion;


    private Version? predownloadGameVersion;


    private bool isGameExeExists;


    /// <summary>
    /// 是否显示 DX12 选项
    /// </summary>
    public bool IsDX12OptionVisible { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// DX12 配置
    /// </summary>
    private GameDXConfig? _dxConfig;


    /// <summary>
    /// 启用 DX12
    /// </summary>
    public bool EnableDX12
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.SetEnableDX12(CurrentGameBiz, value);
            }
        }
    }


    private async void CheckGameVersion()
    {
        try
        {
            GameInstallPath = GameLauncherService.GetGameInstallPath(CurrentGameKey, out bool storageRemoved);
            IsInstallPathRemovableTipEnabled = storageRemoved;
            if (GameInstallPath is null || storageRemoved)
            {
                GameState = GameState.InstallGame;
                return;
            }
            // 只支持启动的游戏没有版本接口，本地版本号读不到也不影响启动
            GameDescriptor? descriptor = GameKeyResolver.Resolve(CurrentGameBiz.Value) is GameKey gameKey
                                       ? _providerRegistry.GetGame(gameKey)
                                       : null;
            bool supportsVersionCheck = descriptor?.HasCapability(GameCapability.VersionCheck) ?? true;

            isGameExeExists = await _gameLauncherService.IsGameExeExistsAsync(CurrentGameKey);
            localGameVersion = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameKey);
            if (isGameExeExists && (localGameVersion != null || !supportsVersionCheck))
            {
                GameState = GameState.StartGame;
            }
            else
            {
                GameState = GameState.ResumeDownload;
                return;
            }
            await CheckGameRunningAsync();
            if (!SupportsPackageApi)
            {
                // VersionCheck 说的是「读得到本地版本号」，上面已经用过了；
                // 下面要问的是官方的最新版本，那是下载接口的一部分，没有下载器就到此为止。
                // 但有些游戏自己公布了版本号，能提醒一句「该去官方启动器更新了」
                await CheckOfficialLauncherUpdateAsync();
                return;
            }
            (latestGameVersion, predownloadGameVersion) = await _gameLauncherService.GetLatestGameVersionAsync(RequiredGameId);
            if (latestGameVersion > localGameVersion)
            {
                GameState = GameState.UpdateGame;
                return;
            }
            if (predownloadGameVersion > localGameVersion)
            {
                IsPredownloadButtonEnabled = true;
                IsPredownloadFinished = await _gamePackageService.CheckPreDownloadFinishedAsync(RequiredGameId);
            }
            _ = CheckDX12ConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check game version");
        }
    }



    /// <summary>
    /// 官方启动器有没有新版本。
    /// <para/>
    /// 只是提醒，不改 <see cref="GameState"/>：更新状态会把按钮接到下载器上，
    /// 而这些游戏没有下载器，更新要回官方启动器做。
    /// </summary>
    private async Task CheckOfficialLauncherUpdateAsync()
    {
        try
        {
            OfficialLauncherUpdateText = null;
            if (string.IsNullOrWhiteSpace(GameInstallPath) || localGameVersion is null)
            {
                return;
            }
            IGameDiscoveryProvider? discovery = _providerRegistry.GetDiscoveryProvider(CurrentGameKey.ProviderId);
            if (discovery is null)
            {
                return;
            }
            Version? latest = await discovery.GetLatestVersionAsync(CurrentGameKey, GameInstallPath);
            if (latest is null)
            {
                return;
            }
            _logger.LogInformation("Official launcher version of ({key}): local {local}, latest {latest}.", CurrentGameKey, localGameVersion, latest);
            if (latest > localGameVersion)
            {
                OfficialLauncherUpdateText = string.Format(Lang.GameLauncherPage_OfficialLauncherUpdateAvailable, latest);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Check official launcher update ({key})", CurrentGameKey);
        }
    }


    /// <summary>
    /// 官方启动器有新版本时的提示文字，没有时为 null
    /// </summary>
    public string? OfficialLauncherUpdateText { get; set => SetProperty(ref field, value); }



    /// <summary>
    /// 检查 DX12 配置
    /// </summary>
    /// <returns></returns>
    private async Task CheckDX12ConfigAsync()
    {
        try
        {
            if (!SupportsPackageApi)
            {
                return;
            }
            EnableDX12 = AppConfig.GetEnableDX12(CurrentGameBiz);
            if (EnableDX12)
            {
                IsDX12OptionVisible = true;
            }

            List<GameDXConfig> dxConfigs = await _hoYoPlayService.GetGameDXConfigsAsync([RequiredGameId]);
            _dxConfig = dxConfigs.FirstOrDefault(x => x.GameId == RequiredGameId);

            if (_dxConfig?.EnableDXSwitch is true)
            {
                IsDX12OptionVisible = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check DX12 config");
        }
    }


    /// <summary>
    /// 获取 DX12 启动参数
    /// </summary>
    /// <returns></returns>
    public string? GetDX12LaunchArgument()
    {
        if (EnableDX12 && _dxConfig is not null)
        {
            return _dxConfig.CmdArgs;
        }
        return null;
    }


    /// <summary>
    /// 显示 DX12 说明对话框
    /// </summary>
    private async void Hyperlink_DX12Intro_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        if (_dxConfig is not null)
        {
            await new DX12IntroDialog { GameDXConfig = _dxConfig, XamlRoot = this.XamlRoot }.ShowAsync();
        }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <returns></returns>
    private async Task LocateGameAsync()
    {
        try
        {
            string? folder = await FileDialogHelper.PickFolderAsync(this.XamlRoot);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (DriveHelper.GetDriveType(folder) is DriveType.Network && !new Uri(folder).IsUnc)
                {
                    InAppToast.MainWindow?.Warning(null, Lang.InstallGameDialog_MappedNetworkDrivesAreNotSupportedPleaseUseANetworkSharePathStartingWithDoubleBackslashes, 0);
                }
                else
                {
                    GameLauncherService.ChangeGameInstallPath(CurrentGameKey, folder);
                    CheckGameVersion();
                    WeakReferenceMessenger.Default.Send(new GameInstallPathChangedMessage());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Locate game");
        }
    }



    /// <summary>
    /// 定位游戏路径
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    private async void Hyperlink_LocateGame_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender, Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        await LocateGameAsync();
    }




    private void OnGameInstallPathChanged(object _, GameInstallPathChangedMessage message)
    {
        CheckGameVersion();
    }




    private void OnMainWindowStateChanged(object _, MainWindowStateChangedMessage message)
    {
        try
        {
            if (message.Activate && (message.ElapsedOver(TimeSpan.FromMinutes(10)) || message.IsCrossingHour))
            {
                CheckGameVersion();
            }
        }
        catch { }
    }




    private void OnRemovableStorageDeviceChanged(object _, RemovableStorageDeviceChangedMessage message)
    {
        try
        {
            CheckGameVersion();
        }
        catch { }
    }




    #endregion




    #region Start Game




    private Timer processTimer;


    [ObservableProperty]
    private partial Process? GameProcess { get; set; }
    partial void OnGameProcessChanged(Process? oldValue, Process? newValue)
    {
        processTimer?.Stop();
        if (processTimer is null)
        {
            processTimer = new(1000);
            processTimer.Elapsed += (_, _) => CheckGameExited();
        }
        if (newValue != null)
        {
            processTimer?.Start();
            RunningGameInfo = $"{newValue.ProcessName}.exe ({newValue.Id})";
            RunningGameService.AddRuninngGame(CurrentGameBiz, newValue);
        }
        else
        {
            RunningGameInfo = null;
            _logger.LogInformation("Game process exited");
        }
    }



    public string? RunningGameInfo { get; set => SetProperty(ref field, value); }


    public string? RunningGameTime { get; set => SetProperty(ref field, value); }


    private async Task<bool> CheckGameRunningAsync()
    {
        try
        {
            GameProcess = await _gameLauncherService.GetGameProcessAsync(CurrentGameKey);
            if (GameProcess != null)
            {
                GameState = GameState.GameIsRunning;
                RunningGameTime = TimeSpanToString(DateTime.Now - GameProcess.StartTime);
                _logger.LogInformation("Game is running ({name}, {pid})", GameProcess.ProcessName, GameProcess.Id);
                return true;
            }
        }
        catch { }
        return false;
    }




    private void CheckGameExited()
    {
        try
        {
            if (GameProcess != null)
            {
                if (GameProcess.HasExited)
                {
                    DispatcherQueue.TryEnqueue(() => RunningGameTime = null);
                    DispatcherQueue.TryEnqueue(CheckGameVersion);
                    GameProcess = null;
                }
                else
                {
                    DispatcherQueue?.TryEnqueue(() => RunningGameTime = TimeSpanToString(DateTime.Now - GameProcess.StartTime));
                }
            }
        }
        catch { }
    }



    private static string TimeSpanToString(TimeSpan value)
    {
        return $"{value.Days * 24 + value.Hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";
    }



    [RelayCommand]
    private async Task StartGameAsync()
    {
        try
        {
            var process = await _gameLauncherService.StartGameAsync(CurrentGameKey);
            if (process is not null)
            {
                GameState = GameState.GameIsRunning;
                GameProcess = process;
                WeakReferenceMessenger.Default.Send(new GameStartedMessage());
            }
        }
        catch (FileNotFoundException)
        {
            CheckGameVersion();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start game");
        }
    }




    #endregion




    #region Install Game




    private async Task InstallGameAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                await new InstallGameDialog { CurrentGameKey = CurrentGameKey, XamlRoot = this.XamlRoot, }.ShowAsync();
            }
            else
            {
                await ChangeGameInstallTaskStateAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install game {GameBiz}", CurrentGameBiz);
        }
    }



    private async Task ResumeDownloadAsync()
    {
        try
        {
            if (!Directory.Exists(GameInstallPath))
            {
                CheckGameVersion();
                return;
            }
            AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(RequiredGameId, GameInstallPath);
            var task = await _gameInstallService.StartInstallAsync(RequiredGameId, GameInstallPath, audio);
            if (task is not null)
            {
                _gameInstallTask = task;
                _dispatchTimer.Start();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume download {GameBiz}", CurrentGameBiz);
        }
    }






    #endregion




    #region Predownload




    [RelayCommand]
    private async Task PredownloadAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                await new PreDownloadDialog { CurrentGameId = this.RequiredGameId, XamlRoot = this.XamlRoot }.ShowAsync();
            }
            else if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
            {
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing)
                {
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    await _gameInstallService.PauseTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else
                {
                    // GameInstallState.Stop
                    CheckGameVersion();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(PredownloadAsync));
            if (_gameInstallTask?.Operation is GameInstallOperation.Predownload)
            {
                _gameInstallTask.State = GameInstallState.Error;
                _gameInstallTask.ErrorMessage = ex.Message;
            }
        }
    }





    #endregion



    #region Update



    private async Task UpdateGameAsync()
    {
        try
        {
            if (localGameVersion is not null && latestGameVersion > localGameVersion)
            {
                AudioLanguage audio = await _gamePackageService.GetAudioLanguageAsync(RequiredGameId, GameInstallPath);
                GameInstallContext? task = await _gameInstallService.StartUpdateAsync(RequiredGameId, GameInstallPath!, audio);
                if (task is not null)
                {
                    _gameInstallTask = task;
                    _dispatchTimer.Start();
                }
            }
            else
            {
                CheckGameVersion();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update game {GameBiz}", CurrentGameBiz);
        }
    }



    #endregion



    #region Game Install Task




    private GameInstallContext? _gameInstallTask;



    private async Task ChangeGameInstallTaskStateAsync()
    {
        try
        {
            if (_gameInstallTask is null)
            {
                CheckGameVersion();
            }
            else if (_gameInstallTask.Operation is not GameInstallOperation.Predownload)
            {
                if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Paused or GameInstallState.Error or GameInstallState.Queueing)
                {
                    await _gameInstallService.ContinueTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else if (_gameInstallTask.State is GameInstallState.Waiting or GameInstallState.Downloading or GameInstallState.Decompressing or GameInstallState.Merging or GameInstallState.Verifying)
                {
                    await _gameInstallService.PauseTaskAsync(_gameInstallTask);
                    _dispatchTimer.Start();
                }
                else
                {
                    // GameInstallState.Stop
                    CheckGameVersion();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change game install task state {GameBiz}", CurrentGameBiz);
        }
    }



    private void UpdateGameInstallTask()
    {
        try
        {
            _gameInstallTask ??= _gameInstallService.GetGameInstallTask(RequiredGameId);
            if (_gameInstallTask is not null)
            {
                if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
                {
                    IsPredownloadButtonEnabled = true;
                }
                _dispatchTimer.Start();
            }
        }
        catch { }
    }



    private void OnGameInstallTaskStarted(object _, GameInstallTaskStartedMessage message)
    {
        if (message.InstallTask.GameId == RequiredGameId)
        {
            _gameInstallTask = message.InstallTask;
            _dispatchTimer.Start();
        }
    }



    private void UpdateGameInstallTaskProgress(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_gameInstallTask is null)
        {
            _dispatchTimer.Stop();
            return;
        }
        try
        {
            if (_gameInstallTask.Operation is GameInstallOperation.Predownload)
            {
                Button_Predownload.UpdateGameInstallTaskState(_gameInstallTask);
            }
            else
            {
                GameState = GameState.Installing;
                Button_StartGame.UpdateGameInstallTaskState(_gameInstallTask);
            }
            if (_gameInstallTask.State is GameInstallState.Error)
            {
                _dispatchTimer.Stop();
            }
            else if (_gameInstallTask.State is GameInstallState.Stop or GameInstallState.Finish)
            {
                _dispatchTimer.Stop();
                _gameInstallTask = null;
                CheckGameVersion();
            }
        }
        catch { }
    }




    #endregion




    #region Drop Background File




    private void RootGrid_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            Border_BackgroundDragIn.Opacity = 1;
        }
    }




    private async void RootGrid_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        Border_BackgroundDragIn.Opacity = 0;
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
                AppConfig.SetCustomBg(CurrentGameBiz, name);
                AppConfig.SetEnableCustomBg(CurrentGameBiz, true);
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage());
            }
        }
        catch (COMException ex)
        {
            InAppToast.MainWindow?.Error(Lang.GameLauncherSettingDialog_CannotDecodeFile);
            _logger.LogError(ex, "Change custom background failed");
        }
        catch (Exception ex)
        {
            InAppToast.MainWindow?.Error(Lang.GameLauncherSettingDialog_AnUnknownErrorOccurredPleaseCheckTheLogs);
            _logger.LogError(ex, "Change custom background failed");
        }
        defer.Complete();
    }



    private void RootGrid_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        Border_BackgroundDragIn.Opacity = 0;
    }



    #endregion




    #region Game Setting



    [RelayCommand]
    private async Task OpenGameLauncherSettingDialogAsync()
    {
        await new GameLauncherSettingDialog { CurrentGameKey = this.CurrentGameKey, XamlRoot = this.XamlRoot }.ShowAsync();
    }




    #endregion



    #region Switch Background Image


    private const string PlayIcon = "\uF5B0";

    private const string PauseIcon = "\uE62E";


    public List<GameBackground> BackgroundImages { get; set => SetProperty(ref field, value); }

    public bool CanStopVideo { get; set => SetProperty(ref field, value); }

    public string StartStopButtonIcon { get; set => SetProperty(ref field, value); }


    private int currentBackgroundImageIndex;
    public int CurrentBackgroundImageIndex
    {
        get => currentBackgroundImageIndex;
        set
        {
            if (SetProperty(ref currentBackgroundImageIndex, value))
            {
                ChangeBackgroundImageIndex(value);
            }
        }
    }


    private void OnBackgroundChanged(object _, BackgroundChangedMessage message)
    {
        if (message.GameBackground is null)
        {
            _ = InitializeBackgameImageSwitcherAsync();
        }
    }


    private async Task InitializeBackgameImageSwitcherAsync()
    {
        try
        {
            CanStopVideo = false;
            BackgroundImages = await _backgroundService.GetGameBackgroundsAsync(CurrentGameKey);
            if (BackgroundImages.Count > 1)
            {
                Border_SwitchBackgroundImage.Visibility = Visibility.Visible;
                GameBackground? currentBackground = await _backgroundService.GetSuggestedGameBackgroundAsync(CurrentGameKey);
                if (currentBackground != null && BackgroundImages.FirstOrDefault(x => x.Id == currentBackground.Id) is GameBackground current)
                {
                    currentBackgroundImageIndex = Math.Clamp(BackgroundImages.IndexOf(current), 0, BackgroundImages.Count - 1);
                    OnPropertyChanged(nameof(CurrentBackgroundImageIndex));
                    CanStopVideo = current.Type is GameBackground.BACKGROUND_TYPE_VIDEO;
                    if (CanStopVideo)
                    {
                        current.StopVideo = currentBackground.StopVideo;
                        StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
                    }
                }
            }
            else
            {
                Border_SwitchBackgroundImage.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize background image switcher {GameBiz}", CurrentGameBiz);
        }
    }


    private void ChangeBackgroundImageIndex(int index)
    {
        try
        {
            if (index < 0 || index >= BackgroundImages.Count)
            {
                return;
            }
            GameBackground current = BackgroundImages[index];
            WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage(current));
            CanStopVideo = current.Type is GameBackground.BACKGROUND_TYPE_VIDEO;
            if (CanStopVideo)
            {
                StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
            }
        }
        catch { }
    }


    private void Border_SwitchBackgroundImage_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Border_SwitchBackgroundImage.Opacity = 1;
    }


    private void Border_SwitchBackgroundImage_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Border_SwitchBackgroundImage.Opacity = 0;
    }


    int _switchBackgroundTotalDelta = 0;

    private void Border_SwitchBackgroundImage_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        _switchBackgroundTotalDelta += delta;
        if (_switchBackgroundTotalDelta <= -120)
        {
            CurrentBackgroundImageIndex++;
            _switchBackgroundTotalDelta = 0;
        }
        else if (_switchBackgroundTotalDelta >= 120)
        {
            CurrentBackgroundImageIndex--;
            _switchBackgroundTotalDelta = 0;
        }
    }


    [RelayCommand]
    public void OpenBackgroundViewWindow()
    {
        try
        {
            new BackgroundViewWindow().Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open background view window.");
        }
    }


    [RelayCommand]
    private async Task CopyCurrentBackgroundImageAsync()
    {
        try
        {
            string? path = null;
            GameBackground? background = AppBackground.Current.CurrentGameBackground;
            if (background?.Type is GameBackground.BACKGROUND_TYPE_CUSTOM)
            {
                path = background.Background.Url;
            }
            else if (background?.Type is GameBackground.BACKGROUND_TYPE_VIDEO && !background.StopVideo)
            {
                string name = Path.GetFileName(background.Video.Url);
                path = BackgroundService.GetBgFilePath(name);
            }
            else if (background is not null)
            {
                string name = Path.GetFileName(background.Background.Url);
                path = BackgroundService.GetBgFilePath(name);
            }
            if (File.Exists(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                ClipboardHelper.SetStorageItems(DataPackageOperation.Copy, file);
                InAppToast.MainWindow?.Information(Lang.Common_CopiedToClipboard);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy current background image {GameBiz}", CurrentGameBiz);
        }
    }


    [RelayCommand]
    private async Task SaveCurrentBackgroundImageAsync()
    {
        try
        {
            string? path = null;
            GameBackground? background = AppBackground.Current.CurrentGameBackground;
            if (background?.Type is GameBackground.BACKGROUND_TYPE_CUSTOM)
            {
                path = background.Background.Url;
            }
            else if (background?.Type is GameBackground.BACKGROUND_TYPE_VIDEO && !background.StopVideo)
            {
                string name = Path.GetFileName(background.Video.Url);
                path = BackgroundService.GetBgFilePath(name);
            }
            else if (background is not null)
            {
                string name = Path.GetFileName(background.Background.Url);
                path = BackgroundService.GetBgFilePath(name);
            }
            if (File.Exists(path))
            {
                var savePath = await FileDialogHelper.OpenSaveFileDialogAsync(this.XamlRoot, Path.GetFileName(path));
                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    File.Copy(path, savePath, true);
                    var file = await StorageFile.GetFileFromPathAsync(savePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save as current background image {GameBiz}", CurrentGameBiz);
        }
    }


    [RelayCommand]
    private void StartOrStopVideoBackground()
    {
        try
        {
            GameBackground current = BackgroundImages[CurrentBackgroundImageIndex];
            if (current.Type is GameBackground.BACKGROUND_TYPE_VIDEO)
            {
                current.StopVideo = !current.StopVideo;
                StartStopButtonIcon = current.StopVideo ? PlayIcon : PauseIcon;
                WeakReferenceMessenger.Default.Send(new BackgroundChangedMessage(current));
            }
        }
        catch { }
    }


    #endregion



    #region Cloud Game


    private void CheckCloudGame()
    {
        try
        {
            // 云游戏是米哈游专属能力，其他游戏没有对应的 GameId
            if (!_providerRegistry.SupportsCapability(CurrentGameKey, GameCapability.CloudGame))
            {
                return;
            }
            Process? process = CloudGameService.GetCloudGameProcess(RequiredGameId);
            if (process is not null)
            {
                RunningGameService.AddRuninngGame(CurrentGameBiz, process);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check cloud game {GameBiz}", CurrentGameBiz);
        }
    }



    #endregion


}
