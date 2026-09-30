using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方新启动器（3.0.x）的游戏配置，按「用户端资源分级」把游戏拆成几个资源包。
/// <para/>
/// 路径是 <c>launcher/game/{APP_ID}_{新 AppKey}/{GAME_ID}/official/index.json</c>，
/// 与旧启动器的 <see cref="KuroLauncherGameIndex"/> 是两份文件、两组 AppKey。
/// <see cref="ResourcePacks"/> 是 common（共用）与 uhd / hd / sd 各档专属的资源包，
/// 每个都是一份完整的下载配置（文件清单、下载目录、补丁）；<see cref="Bundles"/> 说明每一档由哪几个包组成，
/// 例如 HD = common + hd。3.7.0 时 common + hd 与旧版配置的整包逐个文件相同（719 个，MD5 一致）。
/// </summary>
public class KuroOfficialGameIndex
{

    /// <summary>
    /// 下载 CDN，与旧版配置 default 里的是同一批
    /// </summary>
    [JsonPropertyName("cdnList")]
    public List<KuroLauncherCdn>? CdnList { get; set; }


    /// <summary>
    /// 资源包，键是包名（common、uhd、hd、sd）
    /// </summary>
    [JsonPropertyName("resourcePacks")]
    public Dictionary<string, KuroLauncherGameConfig>? ResourcePacks { get; set; }


    /// <summary>
    /// 各档资源由哪几个包组成，键是大写的分级名（UHD、HD、SD）
    /// </summary>
    [JsonPropertyName("bundles")]
    public Dictionary<string, KuroResourceBundle>? Bundles { get; set; }


    [JsonPropertyName("config")]
    public KuroOfficialGameSettings? Config { get; set; }


    /// <summary>
    /// 预下载。3.7.0 时是 null，形状还没见过，原样保留，
    /// 由 <see cref="KuroResourcePackPlanner.GetPredownload"/> 试着解读。
    /// 声明成具体类型的话，形状对不上会让整份配置都读不出来。
    /// </summary>
    [JsonPropertyName("predownload")]
    public JsonElement? Predownload { get; set; }

}



/// <summary>
/// 分级配置里的预下载，猜它与顶层同形。
/// 还没见过线上的样子，对不上时 <see cref="KuroResourcePackPlanner.GetPredownload"/> 当成没有。
/// </summary>
public class KuroOfficialPredownload
{

    [JsonPropertyName("cdnList")]
    public List<KuroLauncherCdn>? CdnList { get; set; }


    [JsonPropertyName("resourcePacks")]
    public Dictionary<string, KuroLauncherGameConfig>? ResourcePacks { get; set; }


    [JsonPropertyName("bundles")]
    public Dictionary<string, KuroResourceBundle>? Bundles { get; set; }

}



/// <summary>
/// 一档资源
/// </summary>
public class KuroResourceBundle
{

    /// <summary>
    /// 组成这一档的资源包名，例如 <c>["common", "hd"]</c>
    /// </summary>
    [JsonPropertyName("resourcePacks")]
    public List<string>? ResourcePacks { get; set; }


    [JsonPropertyName("config")]
    public KuroResourceBundleConfig? Config { get; set; }

}



public class KuroResourceBundleConfig
{

    /// <summary>
    /// 各语言的名称，键与 <see cref="KuroLauncherClient.GetLanguageCode"/> 相同，例如 zh-Hant 是「高畫質」
    /// </summary>
    [JsonPropertyName("displayName")]
    public Dictionary<string, string>? DisplayName { get; set; }


    /// <summary>
    /// 官方推荐这一档的显卡
    /// </summary>
    [JsonPropertyName("recommendGpu")]
    public List<string>? RecommendGpu { get; set; }

}



/// <summary>
/// 与分级无关的设置
/// </summary>
public class KuroOfficialGameSettings
{

    [JsonPropertyName("keyFileCheckList")]
    public List<string>? KeyFileCheckList { get; set; }


    /// <summary>
    /// 预下载功能总开关
    /// </summary>
    [JsonPropertyName("predownloadSwitch")]
    public int PredownloadSwitch { get; set; }


    /// <summary>
    /// 与旧版配置同一个实验开关，修复时清理多余 pak 的目录清单在这里
    /// </summary>
    [JsonPropertyName("experiment")]
    public KuroLauncherExperiment? Experiment { get; set; }

}
