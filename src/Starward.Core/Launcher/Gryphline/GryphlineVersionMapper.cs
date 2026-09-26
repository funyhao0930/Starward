using Starward.Core.Games;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 判断本机的终末地是不是最新版。
/// <para/>
/// 别家都是比版本号，终末地比不了：本机的版本文件 <c>config.ini</c> 是加密的，
/// 连同 <c>game_files</c>、<c>package_files</c> 与官方启动器的日志都读不出明文版本号。
/// 但官方启动器装完会把这一版的安装清单 <c>game_files</c> 原样放进游戏目录，
/// 接口又给出了同一份清单的 MD5，两者一致就说明本机已经是这一版。
/// <para/>
/// 这段判断不依赖任何 IO，值得单独测。
/// </summary>
public static class GryphlineVersionMapper
{

    /// <summary>
    /// 安装清单的文件名，位于游戏目录下
    /// </summary>
    public const string GameFilesName = "game_files";


    /// <summary>
    /// 换成统一的更新信息，无法判断时返回 null。
    /// </summary>
    /// <param name="latest">接口返回的最新游戏包</param>
    /// <param name="localGameFilesMd5">本机 <see cref="GameFilesName"/> 的 MD5，读不到时为 null</param>
    public static GameUpdateInfo? ToUpdateInfo(GryphlineLatestGame? latest, string? localGameFilesMd5)
    {
        if (!Version.TryParse(latest?.Version, out Version? version))
        {
            return null;
        }
        string? remoteMd5 = latest?.Package?.GameFilesMd5;
        // 两边任何一边缺了都比不了，这时不下结论：
        // 报「有更新」会让已是最新的玩家白跑一趟官方启动器
        if (string.IsNullOrWhiteSpace(remoteMd5) || string.IsNullOrWhiteSpace(localGameFilesMd5))
        {
            return null;
        }
        bool upToDate = remoteMd5.Equals(localGameFilesMd5, StringComparison.OrdinalIgnoreCase);
        return new GameUpdateInfo(version, !upToDate);
    }

}
