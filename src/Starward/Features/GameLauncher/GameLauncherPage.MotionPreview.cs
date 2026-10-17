#if DEBUG
using Microsoft.UI.Xaml.Input;
using Starward.Helpers;
using Windows.System;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 只在 Debug 版：用快捷键预览平常很难遇到的状态动画
/// <list type="bullet">
/// <item>Ctrl+Shift+F8：预下载按钮出现 / 消失</item>
/// <item>Ctrl+Shift+F9：模拟下载安装到完成</item>
/// <item>Ctrl+Shift+F10：模拟启动游戏中的流光</item>
/// <item>Ctrl+Shift+F11：弹出四种 App 内通知</item>
/// </list>
/// </summary>
public sealed partial class GameLauncherPage
{


    partial void InitializeMotionPreview()
    {
        AddPreviewAccelerator(VirtualKey.F8, () => IsPredownloadButtonEnabled = !IsPredownloadButtonEnabled);
        AddPreviewAccelerator(VirtualKey.F9, () => _ = Button_StartGame.PreviewInstallMotionAsync());
        AddPreviewAccelerator(VirtualKey.F10, () => _ = Button_StartGame.PreviewLaunchMotionAsync());
        AddPreviewAccelerator(VirtualKey.F11, () =>
        {
            InAppToast.MainWindow?.Information("動畫預覽", "這是一則一般通知");
            InAppToast.MainWindow?.Success("動畫預覽", "這是一則成功通知");
            InAppToast.MainWindow?.Warning("動畫預覽", "這是一則警告通知");
            InAppToast.MainWindow?.Error("動畫預覽", "這是一則錯誤通知");
        });
    }


    private void AddPreviewAccelerator(VirtualKey key, System.Action action)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
        };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        KeyboardAccelerators.Add(accelerator);
    }


}
#endif
