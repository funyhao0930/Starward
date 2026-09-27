using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器的游戏配置，描述当前线上的游戏版本与下载资源。
/// <para/>
/// 与背景图配置不是同一条路径：背景图按语言分文件，这一份不分语言，
/// 整个渠道只有一份。
/// </summary>
public class KuroLauncherGameIndex
{

    /// <summary>
    /// 默认渠道的配置。官方启动器按渠道名取键，国际服只有 <c>default</c> 一个。
    /// </summary>
    [JsonPropertyName("default")]
    public KuroLauncherGameResource? Default { get; set; }


    /// <summary>
    /// 预下载，只在新版本公布之后、上线之前出现，平时整个键都不在。
    /// <para/>
    /// 形状与 <see cref="Default"/> 相同：官方启动器 <c>launcher_main.dll</c>
    /// 用同一个类型反序列化这两个键。
    /// </summary>
    [JsonPropertyName("predownload")]
    public KuroLauncherGameResource? Predownload { get; set; }


    /// <summary>
    /// 预下载功能总开关，是 1 且 <see cref="Predownload"/> 存在时才可以预下载
    /// </summary>
    [JsonPropertyName("predownloadSwitch")]
    public int PredownloadSwitch { get; set; }


    /// <summary>
    /// 官方启动器启动游戏前检查的关键文件，缺了任何一个就认为需要修复
    /// </summary>
    [JsonPropertyName("keyFileCheckList")]
    public List<string>? KeyFileCheckList { get; set; }


    /// <summary>
    /// 官方的实验开关，修复时要按其中的目录清单删掉多余的 pak
    /// </summary>
    [JsonPropertyName("experiment")]
    public KuroLauncherExperiment? Experiment { get; set; }

}


/// <summary>
/// 一个渠道（或预下载）的游戏资源配置
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


    /// <summary>
    /// 下载资源用的 CDN，与取这份配置的 CDN 不是同一批域名
    /// </summary>
    [JsonPropertyName("cdnList")]
    public List<KuroLauncherCdn>? CdnList { get; set; }


    /// <summary>
    /// 完整文件清单（resource.json）的相对路径，内容与 <see cref="KuroLauncherGameConfig.IndexFile"/> 相同
    /// </summary>
    [JsonPropertyName("resources")]
    public string? Resources { get; set; }


    /// <summary>
    /// 完整文件的下载目录，相对于 CDN 根目录
    /// </summary>
    [JsonPropertyName("resourcesBasePath")]
    public string? ResourcesBasePath { get; set; }


    [JsonPropertyName("config")]
    public KuroLauncherGameConfig? Config { get; set; }

}


/// <summary>
/// 下载 CDN
/// </summary>
public class KuroLauncherCdn
{

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// 权重，官方启动器先测速再按它挑，数字越大越优先。0 表示备用。
    /// </summary>
    [JsonPropertyName("P")]
    public int Priority { get; set; }

}


/// <summary>
/// 一个版本的下载配置：完整安装用的文件清单，以及从各个旧版本更新上来的补丁清单
/// </summary>
public class KuroLauncherGameConfig
{

    [JsonPropertyName("version")]
    public string? Version { get; set; }


    /// <summary>
    /// 完整文件清单的相对路径
    /// </summary>
    [JsonPropertyName("indexFile")]
    public string? IndexFile { get; set; }


    [JsonPropertyName("indexFileMd5")]
    public string? IndexFileMd5 { get; set; }


    /// <summary>
    /// 清单里文件的下载目录，结尾带斜杠
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }


    /// <summary>
    /// 要下载的总字节数
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }


    /// <summary>
    /// 安装后占用的字节数
    /// </summary>
    [JsonPropertyName("unCompressSize")]
    public long UnCompressSize { get; set; }


    /// <summary>
    /// 从各个旧版本更新到本版本的补丁，按旧版本号查
    /// </summary>
    [JsonPropertyName("patchConfig")]
    public List<KuroLauncherPatchConfig>? PatchConfig { get; set; }

}


/// <summary>
/// 从某个旧版本更新到新版本的补丁。
/// <para/>
/// 离得近的旧版本给的是二进制差分（krpdiff），更旧的只是一份「哪些文件变了」的清单，
/// 两者都用 <see cref="IndexFile"/> 描述，区别在清单内容里。
/// </summary>
public class KuroLauncherPatchConfig
{

    /// <summary>
    /// 旧版本号，与本机 launcherDownloadConfig.json 的 version 比较
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }


    [JsonPropertyName("indexFile")]
    public string? IndexFile { get; set; }


    [JsonPropertyName("indexFileMd5")]
    public string? IndexFileMd5 { get; set; }


    /// <summary>
    /// 清单里文件的默认下载目录，单个文件可以用 fromFolder 另指
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }


    [JsonPropertyName("size")]
    public long Size { get; set; }


    [JsonPropertyName("unCompressSize")]
    public long UnCompressSize { get; set; }


    [JsonPropertyName("ext")]
    public KuroLauncherPatchExtension? Ext { get; set; }

}


public class KuroLauncherPatchExtension
{

    /// <summary>
    /// 打补丁时临时需要的磁盘空间
    /// </summary>
    [JsonPropertyName("requiredDiskSpace")]
    public long RequiredDiskSpace { get; set; }

}


public class KuroLauncherExperiment
{

    [JsonPropertyName("repair")]
    public KuroLauncherRepairExperiment? Repair { get; set; }

}


public class KuroLauncherRepairExperiment
{

    /// <summary>
    /// 一段 JSON 字符串（不是对象），形如
    /// <c>[{"dir":"Client/Content/Paks","exts":["pak","sig"],"recursive":true}]</c>：
    /// 这些目录里不在清单上的同类文件都是旧版本留下的，修复时要删掉，
    /// 否则虚幻引擎会把过期的 pak 一起挂载。
    /// </summary>
    [JsonPropertyName("directoryIntegrityCheckList")]
    public string? DirectoryIntegrityCheckList { get; set; }

}


/// <summary>
/// <see cref="KuroLauncherRepairExperiment.DirectoryIntegrityCheckList"/> 的一项
/// </summary>
public class KuroDirectoryIntegrityCheck
{

    [JsonPropertyName("dir")]
    public string? Dir { get; set; }

    [JsonPropertyName("exts")]
    public List<string>? Exts { get; set; }

    [JsonPropertyName("recursive")]
    public bool Recursive { get; set; }

}
