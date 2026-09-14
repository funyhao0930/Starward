using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器的游戏配置，描述当前线上的游戏版本与下载资源。
/// <para/>
/// 与背景图配置不是同一条路径：背景图按语言分文件，这一份不分语言，
/// 整个渠道只有一份。Starward 没有实现下载器，因此只取其中的版本号，
/// 用来提醒玩家该回官方启动器更新了。
/// </summary>
public class KuroLauncherGameIndex
{

    /// <summary>
    /// 默认渠道的配置。官方启动器按渠道名取键，国际服只有 <c>default</c> 一个。
    /// </summary>
    [JsonPropertyName("default")]
    public KuroLauncherGameResource? Default { get; set; }

}


/// <summary>
/// 一个渠道的游戏资源配置。下载相关的字段（cdnList、resources、config.patchConfig 等）
/// 这里不建模，需要实现下载器时再补。
/// </summary>
public class KuroLauncherGameResource
{

    /// <summary>
    /// 线上的游戏版本号，形如 <c>3.6.1</c>。
    /// <para/>
    /// 与游戏目录下 <c>launcherDownloadConfig.json</c> 的 <c>version</c> 是同一个东西，
    /// 因此可以直接比较。
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

}
