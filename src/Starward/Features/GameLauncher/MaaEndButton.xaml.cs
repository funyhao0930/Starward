using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Helpers;
using Starward.Language;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 终末地启动页右边侧栏的 MaaEnd 按钮，点一下启动本机的 MaaEnd（<see href="https://github.com/MaaEnd/MaaEnd"/>）。
/// <para/>
/// 点开是跟云游戏按钮同一个样子的小窗：选好 MaaEnd.exe 后可以启动、换位置、打开所在文件夹。图标直接取 exe 自己的。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class MaaEndButton : UserControl
{

    private readonly ILogger<MaaEndButton> _logger = AppConfig.GetLogger<MaaEndButton>();


    public MaaEndButton()
    {
        this.InitializeComponent();
        this.Visibility = Visibility.Collapsed;
    }



    public GameKey CurrentGameKey
    {
        get; set
        {
            field = value;
            bool isEndfield = value.IsSameGame(GryphlineGameMapping.EndfieldDefault);
            this.Visibility = isEndfield ? Visibility.Visible : Visibility.Collapsed;
            if (isEndfield && ExePath is null)
            {
                _ = LoadIconAsync();
            }
        }
    }


    public BitmapImage? Icon { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 存在的 MaaEnd.exe，没选或文件已不在时为 null
    /// </summary>
    public string? ExePath { get; set => SetProperty(ref field, value); }



    private static string? ExistingPath => AppConfig.MaaEndPath is string path && File.Exists(path) ? path : null;


    private async Task LoadIconAsync()
    {
        try
        {
            ExePath = ExistingPath;
            if (ExePath is not string path)
            {
                Icon = null;
                return;
            }
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 128);
            var bitmap = new BitmapImage { DecodePixelWidth = 128 };
            await bitmap.SetSourceAsync(thumbnail);
            Icon = bitmap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load MaaEnd icon");
        }
    }



    [RelayCommand]
    private async Task LaunchAsync()
    {
        try
        {
            string? path = ExistingPath ?? await ChoosePathInternalAsync();
            if (path is null)
            {
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
            });
            Flyout_MaaEnd.Hide();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launch MaaEnd");
            InAppToast.MainWindow?.Error(Lang.MaaEndButton_LaunchFailed, ex.Message);
        }
    }


    [RelayCommand]
    private async Task ChoosePathAsync()
    {
        try
        {
            await ChoosePathInternalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Choose MaaEnd path");
        }
    }


    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            if (ExistingPath is string path)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
                Flyout_MaaEnd.Hide();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Open MaaEnd folder");
        }
    }


    private async Task<string?> ChoosePathInternalAsync()
    {
        string? path = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot, ("MaaEnd", ".exe"));
        if (path is null)
        {
            return null;
        }
        AppConfig.MaaEndPath = path;
        await LoadIconAsync();
        return path;
    }


}
