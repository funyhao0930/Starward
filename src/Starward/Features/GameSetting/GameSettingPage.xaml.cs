using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Display;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Features.GameLauncher;
using Starward.Features.GameSelector;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace Starward.Features.GameSetting;

public sealed partial class GameSettingPage : PageBase
{

    private readonly ILogger<GameSettingPage> _logger = AppConfig.GetLogger<GameSettingPage>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();

    private readonly GameSettingProviderRegistry _settingProviderRegistry = AppConfig.GetService<GameSettingProviderRegistry>();



    public GameSettingPage()
    {
        this.InitializeComponent();
    }




    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Image_Emoji.Source = CurrentGameBiz.ToGame().Value switch
        {
            GameBiz.bh3 => new BitmapImage(AppConfig.EmojiAI),
            GameBiz.hk4e => new BitmapImage(AppConfig.EmojiPaimon),
            GameBiz.hkrpg => new BitmapImage(AppConfig.EmojiPom),
            GameBiz.nap => new BitmapImage(AppConfig.EmojiBangboo),
            _ => null,
        };
        // 崩坏三国际服要细分区服，其他供应商没有 HoYoPlay 的游戏标识
        if (CurrentGameId?.GameBiz == GameBiz.bh3_global)
        {
            CurrentGameBiz = RequiredGameId.Id switch
            {
                "g0mMIvshDb" => GameBiz.bh3_jp,
                "uxB4MC7nzC" => GameBiz.bh3_kr,
                "bxPTXSET5t" => GameBiz.bh3_os,
                "wkE5P5WsIf" => GameBiz.bh3_asia,
                _ => GameBiz.bh3_global,
            };
        }
    }


    protected override async void OnLoaded()
    {
        InitializeResolutionItem();
        await InitializeGameSettingAsync();
    }


    protected override void OnUnloaded()
    {
        if (_displayInformation is not null)
        {
            _displayInformation.AdvancedColorInfoChanged -= _displayInformation_AdvancedColorInfoChanged;
            _displayInformation.Dispose();
            _displayInformation = null!;
        }
    }


    public bool IsBaseSettingEnable { get; set => SetProperty(ref field, value); }

    public bool IsLanguageSettingEnable { get; set => SetProperty(ref field, value); }

    public bool IsGraphicsSettingEnable { get; set => SetProperty(ref field, value); }

    public bool IsApplyButtonEnable { get; set => SetProperty(ref field, value); }

    public string ErrorMessage { get; set => SetProperty(ref field, value); } = Lang.GameSettingPage_SettingNotEffect; // 游戏运行时应用的设置无法生效





    public string? StartArgument
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                AppConfig.SetStartArgument(CurrentGameBiz, value);
            }
        }
    }


    public bool EnableFullScreen
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public bool UsePopupWindow
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }



    public bool EnableCustomResolution { get; set => SetProperty(ref field, value); }


    public int ResolutionWidth
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public int ResolutionHeight
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public int LanguageIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public int StarRailFpsIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public bool EnableGenshinHDR
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                IsApplyButtonEnable = true;
            }
        }
    }


    public bool HDRNotSupported { get; set => SetProperty(ref field, value); }

    public bool HDRNotEnabled { get; set => SetProperty(ref field, value); }


    private async Task InitializeGameSettingAsync()
    {
        try
        {
            // 有的游戏读不到版本号（没有实现版本检查），那就看游戏本体在不在，
            // 否则它们的设置页会永远显示「游戏未安装」
            bool installed = await _gameLauncherService.GetLocalGameVersionAsync(CurrentGameKey) is not null
                          || await _gameLauncherService.IsGameExeExistsAsync(CurrentGameKey);
            if (!installed)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
                return;
            }
            IsBaseSettingEnable = true;
            if (CurrentGameBiz.ToGame().Value is GameBiz.hk4e or GameBiz.hkrpg)
            {
                IsLanguageSettingEnable = true;
            }
            if (CurrentGameBiz.Game is GameBiz.hkrpg)
            {
                IsGraphicsSettingEnable = true;
                StackPanel_StarRailFPS.Visibility = Visibility.Visible;
                StarRailFpsIndex = GameSettingService.GetStarRailFPSIndex(CurrentGameBiz);
            }
            if (CurrentGameKey.ProviderId == KuroGameMapping.ProviderId)
            {
                InitializeKuroTweaks();
            }
            if (CurrentGameBiz.Game is GameBiz.hk4e)
            {
                IsGraphicsSettingEnable = true;
                StackPanel_GenshinHDR.Visibility = Visibility.Visible;
                EnableGenshinHDR = AppConfig.EnableGenshinHDR;
                _displayInformation = DisplayInformation.CreateForWindowId(this.XamlRoot.GetAppWindow().Id);
                _displayInformation.AdvancedColorInfoChanged += _displayInformation_AdvancedColorInfoChanged;
                UpdateHdrState(_displayInformation);
            }
            StartArgument = AppConfig.GetStartArgument(CurrentGameBiz);
            UsePopupWindow = AppConfig.GetUsePopupWindow(CurrentGameBiz);
            GameResolutionSetting? resolutionSetting = _settingProviderRegistry.GetProvider(CurrentGameKey)?.GetResolution(CurrentGameKey);
            if (resolutionSetting is not null)
            {
                EnableFullScreen = resolutionSetting.Value.FullScreen;
                if (resolutionSetting.Value.Width * resolutionSetting.Value.Height > 0)
                {
                    ResolutionWidth = resolutionSetting.Value.Width;
                    ResolutionHeight = resolutionSetting.Value.Height;
                    EnableCustomResolution = !UpdateResolutionComboBoxSelection(ResolutionWidth, ResolutionHeight);
                }
                else
                {
                    ComboBox_Resolution.SelectedIndex = 0;
                }
            }
            else
            {
                ComboBox_Resolution.SelectedIndex = 0;
            }
            if (IsLanguageSettingEnable)
            {
                var langSetting = GameSettingService.GetGameVoiceLanguageSetting(CurrentGameBiz);
                if (langSetting != null)
                {
                    LanguageIndex = langSetting.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize Game Setting");
        }
        finally
        {
            IsApplyButtonEnable = false;
        }
    }



    private void InitializeResolutionItem()
    {
        var display = DisplayArea.GetFromWindowId(this.XamlRoot.ContentIslandEnvironment.AppWindowId, DisplayAreaFallback.Nearest);
        var width = display.OuterBounds.Width;
        var height = display.OuterBounds.Height;
        var list = Resolutions.Where(x => x.Width <= width && x.Height <= height).ToList();
        if (list.Count == 0)
        {
            list.Add((width, height));
        }
        else
        {
            if (list[0].Width != width || list[0].Height != height)
            {
                list.Add((width, height));
            }
        }
        foreach (var item in list)
        {
            ComboBox_Resolution.Items.Add(new ComboBoxItem
            {
                Content = $"{item.Width} × {item.Height}",
            });
        }
    }




    private static List<(int Width, int Height)> Resolutions = new List<(int Width, int Height)>()
    {
        (3840 , 2160),
        (2560 , 1600),
        (2560 , 1440),
        (2048 , 1536),
        (1920 , 1440),
        (1920 , 1200),
        (1920 , 1080),
        (1680 , 1050),
        (1600 , 1200),
        (1440 , 900 ),
        (1280 , 960 ),
        (1280 , 800 ),
        (1280 , 720 ),
        (1152 , 864 ),
        (1024 , 768 ),
        (800  , 600 ),
        (640  , 480 ),
    };



    private void ComboBox_Resolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is ComboBoxItem item)
        {
            if (item.Content is string str)
            {
                var split = str.Split('×', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (split.Length == 2)
                {
                    if (int.TryParse(split[0], out int width) && int.TryParse(split[1], out int height))
                    {
                        ResolutionWidth = width;
                        ResolutionHeight = height;
                        IsApplyButtonEnable = true;
                    }
                }
            }
        }
    }



    private bool UpdateResolutionComboBoxSelection(int width, int height)
    {
        foreach (ComboBoxItem item in ComboBox_Resolution.Items)
        {
            if (item.Content is string str)
            {
                var split = str.Split('×', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (split.Length == 2)
                {
                    if (int.TryParse(split[0], out int _width) && int.TryParse(split[1], out int _height))
                    {
                        if (width == _width && height == _height)
                        {
                            ComboBox_Resolution.SelectedItem = item;
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }


    [RelayCommand]
    private async Task OpenGenshinHDRLumianceSettingWindow()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new MainWindowDragRectAdaptToGameIconMessage(true));
            await new GenshinHDRLuminanceSettingDialog { XamlRoot = this.XamlRoot, CurrentGameBiz = this.CurrentGameBiz }.ShowAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            WeakReferenceMessenger.Default.Send(new MainWindowDragRectAdaptToGameIconMessage());
        }
    }



    [RelayCommand]
    private void ApplySetting()
    {
        try
        {
            if (IsBaseSettingEnable)
            {
                if (ResolutionWidth <= 0 || ResolutionHeight <= 0)
                {
                    // 分辨率必须大于0
                    ErrorMessage = Lang.GameSettingPage_ResolutionMustBeGreaterThan0;
                    return;
                }
                _settingProviderRegistry.GetProvider(CurrentGameKey)
                                        ?.SetResolution(CurrentGameKey, new GameResolutionSetting(ResolutionWidth, ResolutionHeight, EnableFullScreen));
                AppConfig.SetUsePopupWindow(CurrentGameBiz, UsePopupWindow);
            }
            if (IsLanguageSettingEnable)
            {
                GameSettingService.SetGameVoiceLanguageSetting(CurrentGameBiz, LanguageIndex);
            }
            if (IsGraphicsSettingEnable)
            {
                if (CurrentGameBiz.Game is GameBiz.hkrpg)
                {
                    GameSettingService.SetStarRailFPSIndex(CurrentGameBiz, StarRailFpsIndex);
                }
                if (CurrentGameBiz.Game is GameBiz.hk4e)
                {
                    AppConfig.EnableGenshinHDR = EnableGenshinHDR;
                    GameSettingService.SetGenshinEnableHDR(CurrentGameBiz, EnableGenshinHDR);
                }
            }
            if (IsKuroTweakEnable)
            {
                ApplyKuroTweaks();
            }
            // 游戏运行时应用的设置无法生效
            ErrorMessage = Lang.GameSettingPage_SettingNotEffect;
            IsApplyButtonEnable = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Apply Setting");
        }
    }



    #region 鸣潮 Engine.ini 调校


    public bool IsKuroTweakEnable { get; set => SetProperty(ref field, value); }

    public bool UserEngineIniExists { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Engine.ini 里写了但不会生效的键，没有时为 null
    /// </summary>
    public string? KuroIneffectiveKeysMessage { get; set => SetProperty(ref field, value); }

    public List<KuroEngineTweakCategoryViewModel> KuroTweakCategories { get; set => SetProperty(ref field, value); } = [];

    /// <summary>
    /// 有符合搜索的项目的分类；没在搜索时等于 <see cref="KuroTweakCategories"/>
    /// </summary>
    public List<KuroEngineTweakCategoryViewModel> KuroVisibleTweakCategories { get; set => SetProperty(ref field, value); } = [];

    public bool KuroSearchNoResult { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 开始搜索前各分类的展开状态，清空搜索时还原
    /// </summary>
    private Dictionary<KuroEngineTweakCategoryViewModel, bool>? _kuroExpandedBeforeSearch;

    public List<string> KuroPresetNames { get; } = KuroEngineTweakCatalog.Presets.Select(KuroEngineTweakText.Preset).ToList();

    public int SelectedKuroPresetIndex { get; set => SetProperty(ref field, value); } = -1;


    /// <summary>
    /// 读进来时的值，套用时只写有变动的键：没动过的键（包括读不懂的值）原样留在文件里
    /// </summary>
    private readonly Dictionary<KuroEngineTweak, string?> _kuroLoadedValues = new();

    private IEnumerable<KuroEngineTweakItemViewModel> KuroTweakItems => KuroTweakCategories.SelectMany(x => x.Items);


    private string? GetKuroInstallPath() => GameLauncherService.GetGameInstallPath(CurrentGameKey);

    private string? GetKuroConfigDirectory() => GetKuroInstallPath() is string path ? Path.Join(path, KuroGameMapping.SavedConfigRelativePath) : null;

    private string? GetUserEngineIniPath() => GetKuroInstallPath() is string path ? Path.Join(path, KuroGameMapping.UserEngineIniRelativePath) : null;


    private void InitializeKuroTweaks()
    {
        try
        {
            string? configDirectory = GetKuroConfigDirectory();
            if (configDirectory is null)
            {
                return;
            }
            Dictionary<KuroEngineTweak, string> values = KuroEngineIniStore.Read(configDirectory);
            var categories = new List<KuroEngineTweakCategoryViewModel>();
            _kuroLoadedValues.Clear();
            foreach (string category in KuroEngineTweakCatalog.Categories)
            {
                var items = new List<KuroEngineTweakItemViewModel>();
                foreach (KuroEngineTweak tweak in KuroEngineTweakCatalog.Tweaks.Where(x => x.Category == category))
                {
                    var item = new KuroEngineTweakItemViewModel(tweak);
                    item.SetValue(values.GetValueOrDefault(tweak), raiseChanged: false);
                    item.Changed += (_, _) => IsApplyButtonEnable = true;
                    _kuroLoadedValues[tweak] = item.Value;
                    items.Add(item);
                }
                categories.Add(new KuroEngineTweakCategoryViewModel(category, items));
            }
            KuroTweakCategories = categories;
            KuroVisibleTweakCategories = categories;
            _kuroExpandedBeforeSearch = null;
            KuroSearchNoResult = false;
            UserEngineIniExists = File.Exists(GetUserEngineIniPath());
            var ineffective = KuroEngineIniStore.FindIneffectiveKeys(configDirectory);
            KuroIneffectiveKeysMessage = ineffective.Count > 0 ? KuroEngineTweakText.IneffectiveKeys(ineffective) : null;
            IsKuroTweakEnable = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize WuWa Engine.ini tweaks");
        }
    }


    [RelayCommand]
    private async Task LoadKuroPresetAsync()
    {
        if (SelectedKuroPresetIndex < 0 || SelectedKuroPresetIndex >= KuroEngineTweakCatalog.Presets.Count)
        {
            return;
        }
        KuroEngineTweakPreset preset = KuroEngineTweakCatalog.Presets[SelectedKuroPresetIndex];
        string message = string.Format(KuroEngineTweakText.Ui_ConfirmLoadPreset, KuroEngineTweakText.Preset(preset), KuroEngineTweakCatalog.PresetKeys.Count);
        if (!await ConfirmKuroTweakActionAsync(KuroEngineTweakText.Ui_LoadPreset, message))
        {
            return;
        }
        foreach (KuroEngineTweakItemViewModel item in KuroTweakItems)
        {
            // 任一份预设涉及的键都要重设，否则从 Config 1 换到 Config 5 会留下 Config 1 才有的键
            if (KuroEngineTweakCatalog.PresetKeys.Contains(item.Key))
            {
                item.SetValue(preset.Values.GetValueOrDefault(item.Key), raiseChanged: false);
            }
        }
        IsApplyButtonEnable = true;
    }


    [RelayCommand]
    private async Task ResetKuroTweaksAsync()
    {
        if (!KuroTweakItems.Any(x => x.IsSet))
        {
            return;
        }
        string message = string.Format(KuroEngineTweakText.Ui_ConfirmResetAll, KuroTweakItems.Count());
        if (!await ConfirmKuroTweakActionAsync(KuroEngineTweakText.Ui_ResetAll, message))
        {
            return;
        }
        foreach (KuroEngineTweakItemViewModel item in KuroTweakItems)
        {
            item.SetValue(null, raiseChanged: false);
        }
        IsApplyButtonEnable = true;
    }


    /// <summary>
    /// 载入预设、全部重设会一次盖掉很多项，先问一声；这时还没写文件，按「应用」才写
    /// </summary>
    private async Task<bool> ConfirmKuroTweakActionAsync(string action, string message)
    {
        var dialog = new ContentDialog
        {
            Title = action,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = action,
            SecondaryButtonText = Lang.Common_Cancel,
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = this.XamlRoot,
        };
        return await dialog.ShowAsync() is ContentDialogResult.Primary;
    }


    [RelayCommand]
    private async Task OpenKuroConfigFolderAsync()
    {
        try
        {
            // 游戏没开过时 Saved 还不存在，打开最近一层存在的目录
            string? folder = GetKuroConfigDirectory();
            while (folder is not null && !Directory.Exists(folder))
            {
                folder = Path.GetDirectoryName(folder);
            }
            if (folder is not null)
            {
                await Windows.System.Launcher.LaunchFolderPathAsync(folder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open WuWa config folder");
        }
    }


    /// <summary>
    /// 改名而不是删除，玩家想要回去时改回来就行
    /// </summary>
    [RelayCommand]
    private void DisableUserEngineIni()
    {
        try
        {
            string? path = GetUserEngineIniPath();
            if (File.Exists(path))
            {
                File.Move(path, path + KuroEngineIniStore.BackupSuffix, true);
            }
            UserEngineIniExists = File.Exists(path);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Disable UserEngine.ini");
        }
    }


    private void AutoSuggestBox_KuroTweakSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        string query = sender.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            foreach (KuroEngineTweakCategoryViewModel category in KuroTweakCategories)
            {
                category.ApplySearch(null);
                if (_kuroExpandedBeforeSearch?.TryGetValue(category, out bool expanded) ?? false)
                {
                    category.IsExpanded = expanded;
                }
            }
            _kuroExpandedBeforeSearch = null;
            KuroVisibleTweakCategories = KuroTweakCategories;
            KuroSearchNoResult = false;
            return;
        }
        _kuroExpandedBeforeSearch ??= KuroTweakCategories.ToDictionary(x => x, x => x.IsExpanded);
        var visible = new List<KuroEngineTweakCategoryViewModel>();
        foreach (KuroEngineTweakCategoryViewModel category in KuroTweakCategories)
        {
            if (category.ApplySearch(query))
            {
                category.IsExpanded = true;
                visible.Add(category);
            }
        }
        KuroVisibleTweakCategories = visible;
        KuroSearchNoResult = visible.Count == 0;
    }


    private void ApplyKuroTweaks()
    {
        string? configDirectory = GetKuroConfigDirectory();
        if (configDirectory is null)
        {
            return;
        }
        var changed = new Dictionary<KuroEngineTweak, string?>();
        foreach (KuroEngineTweakItemViewModel item in KuroTweakItems)
        {
            if (!KuroEngineTweakCatalog.ValueEquals(_kuroLoadedValues.GetValueOrDefault(item.Tweak), item.Value))
            {
                changed[item.Tweak] = item.Value;
            }
        }
        if (changed.Count == 0)
        {
            return;
        }
        List<string> written = KuroEngineIniStore.Write(configDirectory, changed);
        _logger.LogInformation("WuWa ini tweaks: {count} keys changed, wrote {files}", changed.Count, string.Join(", ", written));
        foreach (var (tweak, value) in changed)
        {
            _kuroLoadedValues[tweak] = value;
        }
    }


    #endregion



    private DisplayInformation _displayInformation;

    private void _displayInformation_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        UpdateHdrState(sender);
    }


    private void UpdateHdrState(DisplayInformation displayInformation)
    {
        try
        {
            HDRNotEnabled = false;
            HDRNotSupported = false;
            var info = displayInformation.GetAdvancedColorInfo();
            if (!info.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange))
            {
                HDRNotSupported = true;
            }
            else if (info.CurrentAdvancedColorKind is not DisplayAdvancedColorKind.HighDynamicRange)
            {
                HDRNotEnabled = true;
            }
        }
        catch { }
    }



}
