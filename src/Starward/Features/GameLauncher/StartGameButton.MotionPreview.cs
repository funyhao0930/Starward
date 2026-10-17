#if DEBUG
using Starward.RPC.GameInstall;
using System;
using System.Threading.Tasks;


namespace Starward.Features.GameLauncher;

/// <summary>
/// 只在 Debug 版：不必真的下载或启动游戏，就能看到按钮各状态的动画
/// </summary>
public sealed partial class StartGameButton
{


    private bool _motionPreviewRunning;


    /// <summary>
    /// 模拟一次完整的下载：排队 → 下载 0~100% → 完成打勾 → 回到原来的状态
    /// </summary>
    public async Task PreviewInstallMotionAsync()
    {
        if (_motionPreviewRunning)
        {
            return;
        }
        _motionPreviewRunning = true;
        GameState previous = GameState;
        try
        {
            GameState = GameState.Installing;
            InstallState = GameInstallState.Waiting;
            ProgressRingValue = 0;
            ProgressPercentText = "0%";
            RemainTimeText = "--:--:--";
            await Task.Delay(1200);
            InstallState = GameInstallState.Downloading;
            for (int p = 0; p <= 100; p += 3)
            {
                ProgressRingValue = p;
                ProgressPercentText = $"{p}%";
                RemainTimeText = TimeSpan.FromSeconds((100 - p) / 3 * 0.15).ToString(@"hh\:mm\:ss");
                await Task.Delay(150);
            }
            ProgressRingValue = 100;
            ProgressPercentText = "100%";
            InstallState = GameInstallState.Finish;
            await Task.Delay(1800);
        }
        finally
        {
            InstallState = GameInstallState.Waiting;
            GameState = previous;
            _motionPreviewRunning = false;
        }
    }


    /// <summary>
    /// 模拟按下开始游戏后、游戏进程出现前的那几秒
    /// </summary>
    public async Task PreviewLaunchMotionAsync()
    {
        if (_motionPreviewRunning)
        {
            return;
        }
        _motionPreviewRunning = true;
        try
        {
            Button_GameAction.IsEnabled = false;
            await Task.Delay(4000);
        }
        finally
        {
            Button_GameAction.IsEnabled = GameState is not GameState.GameIsRunning and not GameState.ComingSoon;
            _motionPreviewRunning = false;
        }
    }


}
#endif
