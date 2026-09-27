using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮的文件清单（indexFile.json / resource.json）。
/// <para/>
/// 完整安装的清单只有 <see cref="Resource"/>；更新用的补丁清单还会带
/// <see cref="DeleteFiles"/>，以及二进制差分的 <see cref="GroupInfos"/>。
/// </summary>
public class KuroResourceIndex
{

    /// <summary>
    /// 要下载的文件。补丁清单里可能混着普通文件与 <c>.krpdiff</c> 差分包。
    /// </summary>
    [JsonPropertyName("resource")]
    public List<KuroResourceFile> Resource { get; set; } = [];


    /// <summary>
    /// 更新完成后要删掉的旧文件，路径相对于游戏目录
    /// </summary>
    [JsonPropertyName("deleteFiles")]
    public List<string>? DeleteFiles { get; set; }


    /// <summary>
    /// 每个 <c>.krpdiff</c> 差分包把哪些旧文件变成哪些新文件
    /// </summary>
    [JsonPropertyName("groupInfos")]
    public List<KuroResourceGroup>? GroupInfos { get; set; }


    /// <summary>
    /// 差分的应用方式，目前只见过 <c>group</c>
    /// </summary>
    [JsonPropertyName("applyTypes")]
    public List<string>? ApplyTypes { get; set; }


    public const string KrpdiffExtension = ".krpdiff";

    public const string KrdiffExtension = ".krdiff";

}


/// <summary>
/// 清单里的一个文件
/// </summary>
public class KuroResourceFile
{

    /// <summary>
    /// 相对于游戏目录（或差分包目录）的路径，用正斜杠
    /// </summary>
    [JsonPropertyName("dest")]
    public string Dest { get; set; } = "";


    [JsonPropertyName("md5")]
    public string Md5 { get; set; } = "";


    [JsonPropertyName("size")]
    public long Size { get; set; }


    /// <summary>
    /// 不为空时从这个目录下载，而不是补丁配置的默认 baseUrl。
    /// 补丁清单用它把「差分到上一个版本」与「上一个版本之后的热更新文件」放在同一份清单里。
    /// </summary>
    [JsonPropertyName("fromFolder")]
    public string? FromFolder { get; set; }

}


/// <summary>
/// 一个 <c>.krpdiff</c> 差分包的内容说明
/// </summary>
public class KuroResourceGroup
{

    /// <summary>
    /// 差分包的文件名，对应 <see cref="KuroResourceIndex.Resource"/> 里的一项
    /// </summary>
    [JsonPropertyName("dest")]
    public string Dest { get; set; } = "";


    /// <summary>
    /// 打补丁需要的旧文件
    /// </summary>
    [JsonPropertyName("srcFiles")]
    public List<KuroResourceFile> SrcFiles { get; set; } = [];


    /// <summary>
    /// 打完补丁得到的新文件
    /// </summary>
    [JsonPropertyName("dstFiles")]
    public List<KuroResourceFile> DstFiles { get; set; } = [];

}


/// <summary>
/// 预下载完成的标记，见 <see cref="KuroDownloadPlanner.IsPredownloadFinished"/>
/// </summary>
public class KuroPredownloadMarker
{

    [JsonPropertyName("localVersion")]
    public string LocalVersion { get; set; } = "";

    [JsonPropertyName("targetVersion")]
    public string TargetVersion { get; set; } = "";

}


/// <summary>
/// 游戏目录下的 <c>launcherDownloadConfig.json</c>，官方启动器用它记录本机版本。
/// Starward 装好或更新完也要写一份，否则官方启动器会当成没装过。
/// </summary>
public class KuroLauncherDownloadConfig
{

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("reUseVersion")]
    public string ReUseVersion { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("isPreDownload")]
    public bool IsPreDownload { get; set; }

    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "";

}
