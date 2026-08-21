namespace Starward.Core.Games;

/// <summary>
/// 从本机文件中找一张适合当启动页背景的图。
/// <para/>
/// 只支持启动的游戏没有在线背景图接口，否则会一路退到应用的全局默认图
/// （一张原神插画），放在别款游戏下面并不合适。这里按两级来找：
/// 先看游戏自己带的启动器美术，没有就用玩家自己的截图。
/// </summary>
public static class LocalGameArtwork
{

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm"];


    /// <summary>
    /// 找出该游戏可用的背景图，找不到返回 null。
    /// </summary>
    /// <param name="descriptor">游戏描述，提供美术与截图目录</param>
    /// <param name="installPath">游戏安装目录，用于解析相对路径</param>
    public static string? FindBackgroundImage(GameDescriptor? descriptor, string? installPath)
    {
        if (descriptor is null)
        {
            return null;
        }
        // 游戏自带的美术是固定的，优先于截图；截图会随玩家的进度变化
        return FindNewestFile(descriptor.ResolveBackgroundPaths(installPath), ImageExtensions)
            ?? FindNewestFile(descriptor.ResolveScreenshotPaths(installPath), ImageExtensions);
    }


    /// <summary>
    /// 找出该游戏自带的动态背景（视频），找不到返回 null。
    /// <para/>
    /// 只查启动器自带的背景目录，不退回截图——截图里不会有视频。
    /// </summary>
    public static string? FindBackgroundVideo(GameDescriptor? descriptor, string? installPath)
    {
        if (descriptor is null)
        {
            return null;
        }
        return FindNewestFile(descriptor.ResolveBackgroundPaths(installPath), VideoExtensions);
    }


    /// <summary>
    /// 这些目录里符合扩展名的最新一个文件，包含子目录
    /// </summary>
    private static string? FindNewestFile(IReadOnlyList<string> folders, IReadOnlyList<string> extensions)
    {
        string? newest = null;
        DateTime newestTime = DateTime.MinValue;
        foreach (string folder in folders)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string file in files)
            {
                if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    DateTime time = File.GetLastWriteTime(file);
                    if (time > newestTime)
                    {
                        newest = file;
                        newestTime = time;
                    }
                }
                catch (IOException)
                {
                    // 读不到时间就当它不存在
                }
            }
        }
        return newest;
    }

}
