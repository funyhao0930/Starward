using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 异环游戏本体资源的公开版本配置。
/// <para/>
/// 外壳（NTETW\）自己走 AllFiles.xml，游戏本体（Client\）走的是完美世界的 PatcherSDK：
/// 外壳 Config.ini 的 <c>[Patcher] configPath</c> 指向 PatcherConfig.json，
/// 那里给出资源 CDN（gameResUrl）与分支（branchName），
/// <c>{gameResUrl}/{branchName}/Version/Windows/config.xml</c> 就是明文的版本配置：
/// 资源版本、总大小，以及各个资源标签的版本与大小。
/// 再往下的文件清单（ResList）是加密的，这里不碰。
/// <para/>
/// 地址一样不写死，全由本机的配置给出。这里只有解析，不碰网络与磁盘。
/// </summary>
public static class HottaPatcherConfig
{

    /// <summary>
    /// 外壳 Config.ini 里 PatcherSDK 的配置目录：<c>[Patcher] configPath=/ResFilesM/2000013/PatcherConfig/</c>，相对于外壳目录
    /// </summary>
    private static readonly Regex ConfigPathRegex = new(@"(?m)^[ \t]*configPath[ \t]*=[ \t]*(\S+)[ \t\r]*$", RegexOptions.Compiled);


    public const string SettingsFileName = "PatcherConfig.json";


    /// <summary>
    /// PatcherSDK 的平台目录。本体目录里的 Patch_Windows.json 与 CDN 上的 Version/Windows 都是这个写法
    /// </summary>
    public const string Platform = "Windows";


    /// <summary>
    /// 从外壳的 Config.ini 读出 PatcherSDK 配置目录（相对于外壳目录，以 / 开头），读不到返回 null
    /// </summary>
    public static string? ParseConfigPath(string? configIniText)
    {
        if (string.IsNullOrEmpty(configIniText))
        {
            return null;
        }
        Match match = ConfigPathRegex.Match(configIniText);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }


    /// <summary>
    /// 解析 PatcherConfig.json，资源地址或分支缺一个都返回 null
    /// </summary>
    public static HottaPatcherSettings? ParseSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object
                || !root.TryGetProperty("branchName", out JsonElement branchElement)
                || branchElement.ValueKind is not JsonValueKind.String
                || !root.TryGetProperty("gameResUrl", out JsonElement urlsElement)
                || urlsElement.ValueKind is not JsonValueKind.Array)
            {
                return null;
            }
            string? branch = branchElement.GetString()?.Trim();
            var urls = new List<string>();
            foreach (JsonElement element in urlsElement.EnumerateArray())
            {
                if (element.ValueKind is JsonValueKind.String
                    && element.GetString()?.Trim().TrimEnd('/') is string url
                    && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                {
                    urls.Add(url);
                }
            }
            if (string.IsNullOrWhiteSpace(branch) || urls.Count == 0)
            {
                return null;
            }
            return new HottaPatcherSettings
            {
                ResourceUrls = urls.AsReadOnly(),
                Branch = branch,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }


    /// <summary>
    /// 版本配置的地址，主站在前、备援在后
    /// </summary>
    public static IReadOnlyList<string> GetVersionConfigUrls(HottaPatcherSettings settings)
    {
        string branch = Uri.EscapeDataString(settings.Branch);
        return settings.ResourceUrls.Select(x => $"{x}/{branch}/Version/{Platform}/config.xml").ToList().AsReadOnly();
    }


    /// <summary>
    /// 解析版本配置 config.xml，读不出资源版本时返回 null
    /// </summary>
    public static HottaResourceVersion? ParseVersionConfig(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }
        XElement? root;
        try
        {
            root = XDocument.Parse(xml).Root;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        string? version = root?.Element("ResVersion")?.Value.Trim();
        if (root is null || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }
        // 本体是哪个标签写在 Extra/BaseTag 里，与顶层的 Tag 相同（都是 baseTag）
        var baseTags = new HashSet<string>(root.Element("Extra")?.Element("BaseTag")?.Elements("item")
                                               .Select(x => x.Attribute("name")?.Value.Trim())
                                               .OfType<string>() ?? [], StringComparer.OrdinalIgnoreCase);
        if (root.Element("Tag")?.Value.Trim() is string tag && tag.Length > 0)
        {
            baseTags.Add(tag);
        }
        var tags = new List<HottaResourceTag>();
        // BaseVerson 是官方的拼法
        foreach (XElement element in root.Element("BaseVerson")?.Elements("Res") ?? [])
        {
            string? name = element.Attribute("Tag")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || tags.Any(x => string.Equals(x.Tag, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            tags.Add(new HottaResourceTag
            {
                Tag = name,
                Version = element.Attribute("version")?.Value.Trim() ?? "",
                Size = ParseLong(element.Attribute("ResSize")?.Value),
                IsBase = baseTags.Contains(name),
            });
        }
        return new HottaResourceVersion
        {
            Version = version,
            Size = ParseLong(root.Element("ResSize")?.Value),
            // 本体在前，其余照标签名排：官方是倒着列的（pakchunk104 在最前）
            Tags = tags.OrderByDescending(x => x.IsBase).ThenBy(x => x.Tag, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly(),
        };
    }


    private static long ParseLong(string? value)
    {
        return long.TryParse(value?.Trim(), out long result) ? result : 0;
    }

}


/// <summary>
/// PatcherConfig.json 里与下载有关的部分
/// </summary>
public sealed class HottaPatcherSettings
{

    /// <summary>
    /// 资源 CDN 的根地址，主站在前，末尾不带斜杠
    /// </summary>
    public required IReadOnlyList<string> ResourceUrls { get; init; }


    /// <summary>
    /// 资源分支，例如 <c>PC_140</c>
    /// </summary>
    public required string Branch { get; init; }

}


/// <summary>
/// 线上的游戏本体资源版本
/// </summary>
public sealed class HottaResourceVersion
{

    /// <summary>
    /// 资源版本，例如 <c>1.4.3</c>。与外壳的版本号（1.0.8.0928）是两回事
    /// </summary>
    public required string Version { get; init; }


    /// <summary>
    /// 本体的字节数
    /// </summary>
    public long Size { get; init; }


    /// <summary>
    /// 本体与各个附加资源，本体在前
    /// </summary>
    public required IReadOnlyList<HottaResourceTag> Tags { get; init; }

}


/// <summary>
/// 一个资源标签
/// </summary>
public sealed class HottaResourceTag
{

    /// <summary>
    /// 标签名，本体是 <c>baseTag</c>，附加资源是 <c>pakchunk101</c> 这类，由游戏自己按需下载
    /// </summary>
    public required string Tag { get; init; }


    public required string Version { get; init; }


    public long Size { get; init; }


    /// <summary>
    /// 是否为游戏本体
    /// </summary>
    public bool IsBase { get; init; }

}
