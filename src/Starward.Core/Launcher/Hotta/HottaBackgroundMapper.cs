using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 把异环的文件清单与背景配置换成应用统一使用的 <see cref="GameBackground"/>。
/// <para/>
/// 这段映射不依赖任何 IO，值得单独测。
/// </summary>
public static class HottaBackgroundMapper
{

    /// <summary>
    /// 背景素材所在的目录名。中间一层是 Config.ini 里的 GameID，随代理商与版本可能变，
    /// 因此不写死，只按目录名匹配。
    /// </summary>
    public const string BackgroundFolderName = "bgimgs";


    /// <summary>
    /// 背景配置的文件名
    /// </summary>
    public const string ConfigFileName = "config.json";


    /// <summary>
    /// 这个路径是不是背景目录里的文件，用来在解析清单时就筛掉其余上千个文件
    /// </summary>
    public static bool IsBackgroundFile(string path)
    {
        return path.Contains($"/{BackgroundFolderName}/", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 在清单里找出背景配置文件，找不到返回 null
    /// </summary>
    public static HottaManifestFile? FindConfigFile(HottaFileManifest? manifest)
    {
        return manifest?.Files.FirstOrDefault(x => x.FileName.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase));
    }


    /// <summary>
    /// 按背景配置在清单里挑出这一版要用的静态图与视频。
    /// <para/>
    /// 一定按配置指名的文件名去找，不是「挑目录里最新的那个」：
    /// 作废的旧文件（见 <see cref="HottaBackgroundConfig.Discards"/>）也在目录里，
    /// 按时间挑会挑到它们。
    /// </summary>
    public static (HottaManifestFile? Poster, HottaManifestFile? Video) SelectFiles(
        HottaBackgroundConfig? config, HottaFileManifest? manifest)
    {
        if (config is null || manifest is null)
        {
            return (null, null);
        }
        return (Find(manifest, config.GetPosterFileName()), Find(manifest, config.Video));
    }


    private static HottaManifestFile? Find(HottaFileManifest manifest, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }
        return manifest.Files.FirstOrDefault(x => x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }


    /// <summary>
    /// 缓存文件名。带上校验值有两个用处：版本一换名字就换，不会读到上一版留下的旧文件；
    /// 也不会与别款游戏的背景图重名——<c>bg.mp4</c> 这种名字太常见了。
    /// </summary>
    public static string GetCacheFileName(HottaManifestFile file)
    {
        string extension = System.IO.Path.GetExtension(file.FileName);
        string checksum = string.IsNullOrWhiteSpace(file.Checksum) ? file.FileName : file.Checksum;
        return $"nte_{checksum}{extension}";
    }


    /// <summary>
    /// 换成统一的背景，凑不出可用背景时返回 null。
    /// <para/>
    /// 传进来的是已经放进缓存目录的文件名，显示层会用它去缓存目录取文件，
    /// 见 <c>BackgroundService.GetBackgroundFileAsync</c>。
    /// </summary>
    /// <param name="poster">静态图，也是视频的首帧图</param>
    /// <param name="video">动态背景，可以为 null</param>
    public static GameBackground? ToGameBackground(HottaManifestFile? poster, HottaManifestFile? video,
                                                   string? posterCacheName, string? videoCacheName)
    {
        // 没有静态图就凑不出背景：显示层要拿它算主题色，也要拿它当停播后的静态背景
        if (poster is null || string.IsNullOrWhiteSpace(posterCacheName))
        {
            return null;
        }
        if (video is not null && !string.IsNullOrWhiteSpace(videoCacheName))
        {
            return new GameBackground
            {
                Id = poster.Checksum,
                Type = GameBackground.BACKGROUND_TYPE_VIDEO,
                Background = new GameImage { Url = posterCacheName },
                Video = new GameImage { Url = videoCacheName },
                // 异环没有压在视频上的版本标语图，与终末地一样只有视频和首帧图
                Theme = null,
            };
        }
        return new GameBackground
        {
            Id = poster.Checksum,
            Type = GameBackground.BACKGROUND_TYPE_UNSPECIFIED,
            Background = new GameImage { Url = posterCacheName },
        };
    }

}
