using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 异环官方更新程序的文件清单。
/// <para/>
/// 这家没有 JSON 接口，走的是老式的版本文件加文件清单：
/// 游戏自己的 Config.ini 给出 Version.ini 的地址，Version.ini 给出 AllFiles.xml 的地址，
/// AllFiles.xml 才列出每个文件的路径与校验值。
/// <para/>
/// 这里只有解析，不碰网络，因此可以单独测。
/// </summary>
public static class HottaLauncherManifest
{

    /// <summary>
    /// <c>[VERSION] FileListURL=https://.../1.0.8.0903_1/AllFiles.xml</c>
    /// <para/>
    /// 与 <c>VersionInfoFileURL</c> 一样是完整地址，不必自己拼版本号。
    /// </summary>
    private static readonly Regex FileListUrlRegex = new(@"(?m)^[ \t]*FileListURL[ \t]*=[ \t]*(https://\S+)[ \t\r]*$", RegexOptions.Compiled);


    /// <summary>
    /// 从 Version.ini 的内容中读出文件清单的地址，读不到返回 null
    /// </summary>
    public static string? ParseFileListUrl(string? versionIniText)
    {
        if (string.IsNullOrEmpty(versionIniText))
        {
            return null;
        }
        Match match = FileListUrlRegex.Match(versionIniText);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }


    /// <summary>
    /// 解析 AllFiles.xml，解析不出来返回 null。
    /// <para/>
    /// 清单里有上千个文件，调用方只关心其中几个，因此用 <paramref name="pathFilter"/>
    /// 在解析时就筛掉其余的，不必把整份列表建成对象。
    /// </summary>
    /// <param name="pathFilter">留下哪些文件，为 null 时全留</param>
    public static HottaFileManifest? ParseFileManifest(string? xml, Func<string, bool>? pathFilter = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        XElement? root = document.Root;
        if (root is null)
        {
            return null;
        }
        // BaseUrl 与版本号是拼下载地址用的，缺任何一个都拼不出来
        string? baseUrl = root.Element("Url")?.Attribute("BaseUrl")?.Value?.Trim();
        string? version = root.Element("ProductVersion")?.Attribute("Version")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }
        var files = new List<HottaManifestFile>();
        foreach (XElement element in root.Elements("File"))
        {
            string? path = element.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            if (pathFilter is not null && !pathFilter(path))
            {
                continue;
            }
            files.Add(new HottaManifestFile
            {
                Path = path,
                Checksum = element.Attribute("Checksum")?.Value ?? "",
                Size = ParseLong(element.Attribute("Size")?.Value),
                ZipChecksum = element.Attribute("ZipChecksum")?.Value ?? "",
                ZipSize = ParseLong(element.Attribute("ZipSize")?.Value),
            });
        }
        return new HottaFileManifest
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            Version = version,
            Files = files.AsReadOnly(),
        };
    }


    private static long ParseLong(string? value)
    {
        return long.TryParse(value, out long result) ? result : 0;
    }

}


/// <summary>
/// AllFiles.xml 的内容
/// </summary>
public class HottaFileManifest
{

    /// <summary>
    /// 下载根地址，末尾不带斜杠
    /// </summary>
    public required string BaseUrl { get; init; }


    /// <summary>
    /// 带序号的版本目录名，例如 <c>1.0.8.0903_1</c>。
    /// 注意它与游戏的版本号不同，多了一段序号。
    /// </summary>
    public required string Version { get; init; }


    public required IReadOnlyList<HottaManifestFile> Files { get; init; }


    /// <summary>
    /// 某个文件的下载地址。
    /// <para/>
    /// 必须带 <c>.zip</c> 后缀：直接请求原始路径服务器返回 403，
    /// 只有压缩包那一份是公开的。
    /// </summary>
    public string GetDownloadUrl(HottaManifestFile file)
    {
        // Path 本身以 / 开头，拼接时不再补
        return $"{BaseUrl}/{Version}{file.Path}.zip";
    }

}


/// <summary>
/// 清单里的一个文件
/// </summary>
public class HottaManifestFile
{

    /// <summary>
    /// 相对游戏目录的路径，以 / 开头，例如 <c>/ResFilesM/2000013/bgimgs/bg.mp4</c>
    /// </summary>
    public required string Path { get; init; }


    /// <summary>
    /// 解压后文件的 MD5。版本一变它就变，适合拿来当缓存名与「换了没有」的依据。
    /// </summary>
    public required string Checksum { get; init; }


    /// <summary>
    /// 解压后的字节数
    /// </summary>
    public long Size { get; init; }


    /// <summary>
    /// 压缩包的 MD5
    /// </summary>
    public string ZipChecksum { get; init; } = "";


    /// <summary>
    /// 压缩包的字节数
    /// </summary>
    public long ZipSize { get; init; }


    /// <summary>
    /// 文件名，例如 <c>bg.mp4</c>
    /// </summary>
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

}
