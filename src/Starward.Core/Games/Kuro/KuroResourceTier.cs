using System.Text.RegularExpressions;

namespace Starward.Core.Games.Kuro;

/// <summary>
/// 鸣潮 3.7.0 起的「用户端资源分级」：同一个版本的游戏分成极致（UHD）、高清（HD）、流畅（SD）三档资源。
/// <para/>
/// 三档共用 <c>Client\Content\Paks</c> 等文件，各档专属的 pak 放在 <c>Client\Content\UHD|HD|SD</c>，
/// PC 上可以同时装好几档。游戏按启动参数 <c>-krqlv=</c> 决定挂载哪一档：
/// Client-Win64-Shipping.exe 里 <c>-krqlv=</c> 后面紧接着就是 HD、SD、UHD 三个值与各自的 <c>%sHD/</c> 这类目录，
/// 不带这个参数会崩溃在「kuro: Use launcher to start game!」（Client.log 先写一句「launch not by commandlet, use kuro quality!」）。
/// <para/>
/// 公开的旧版游戏配置只有 HD（没拿到测试资格的玩家一律是 HD），分三档的是官方新启动器的配置。
/// 值一律用官方启动器传的小写写法，游戏比对时不分大小写。
/// </summary>
public static class KuroResourceTier
{

    public const string UHD = "uhd";

    public const string HD = "hd";

    public const string SD = "sd";


    /// <summary>
    /// 全部分级，画质从高到低
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[] { UHD, HD, SD }.AsReadOnly();


    /// <summary>
    /// 玩家没得选时的一档：官方启动器的默认值，也是旧版游戏配置唯一提供的一档
    /// </summary>
    public const string Default = HD;


    private const string LaunchArgumentName = "-krqlv=";


    /// <summary>
    /// Starward 加装一档时先在那一档的目录里放这个文件，全部下载完才删掉。
    /// 有它的目录不算装好：中途暂停、失败或程序被关掉时，已经下完的 pak 会让目录看起来像装好了，
    /// 带着那一档的参数启动，游戏会因为资源不全而出错。
    /// </summary>
    public const string IncompleteMarkerFileName = "starward_incomplete";


    /// <summary>
    /// 删除一档时先把目录改成这个后缀再删。改名是一步完成的，删到一半失败（文件被占用）时，
    /// 剩下的东西也不在分级目录里，不会被当成还装着；下次再处理分级时顺手清掉。
    /// </summary>
    public const string RemovingFolderSuffix = ".starward_removing";


    /// <summary>
    /// 启动参数里的分级，前面必须是开头或空白，免得把别的参数的一部分当成它
    /// </summary>
    private static readonly Regex LaunchArgumentRegex = new(@"(?:^|\s)-krqlv=(\S*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    /// <summary>
    /// 连同前面的空白一起删，删完不会留下两个连在一起的空格
    /// </summary>
    private static readonly Regex LaunchArgumentWithLeadingSpaceRegex = new(@"(?:^|\s+)-krqlv=\S*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);



    /// <summary>
    /// 统一成小写，不认得的值返回 null
    /// </summary>
    public static string? Normalize(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return null;
        }
        string value = tier.Trim().Trim('"').ToLowerInvariant();
        return All.Contains(value) ? value : null;
    }


    /// <summary>
    /// 解析逗号分隔的分级（安装任务经 RPC 传递时的写法），统一成小写、去掉不认得的、画质从高到低排好
    /// </summary>
    public static IReadOnlyList<string> Parse(string? tiers)
    {
        if (string.IsNullOrWhiteSpace(tiers))
        {
            return [];
        }
        var set = new HashSet<string>(tiers.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(Normalize).OfType<string>());
        return All.Where(set.Contains).ToList().AsReadOnly();
    }


    /// <summary>
    /// <see cref="Parse"/> 的反向
    /// </summary>
    public static string Format(IEnumerable<string> tiers) => string.Join(',', Parse(string.Join(',', tiers)));


    /// <summary>
    /// 启动参数，例如 <c>-krqlv=hd</c>
    /// </summary>
    public static string GetLaunchArgument(string tier) => LaunchArgumentName + (Normalize(tier) ?? Default);


    /// <summary>
    /// 某一档专属文件的目录，相对于游戏目录（Wuthering Waves Game），写法与文件清单一致
    /// </summary>
    public static string GetContentFolder(string tier) => $"Client/Content/{(Normalize(tier) ?? Default).ToUpperInvariant()}";



    /// <summary>
    /// 游戏目录里装好的分级，画质从高到低。
    /// <para/>
    /// 目录里至少要有一个 pak 才算：官方启动器切换或删除分级之后可能留下空目录，
    /// 这时带着那一档的参数启动，游戏会因为挂载不到任何资源而出错。
    /// Starward 还没加装完的一档（目录里有 <see cref="IncompleteMarkerFileName"/>）也不算。
    /// </summary>
    /// <param name="gameDir">游戏目录，即官方启动器安装根目录下的 Wuthering Waves Game</param>
    public static IReadOnlyList<string> GetInstalledTiers(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return [];
        }
        var tiers = new List<string>(All.Count);
        foreach (string tier in All)
        {
            try
            {
                string dir = Path.Combine(gameDir, GetContentFolder(tier));
                if (Directory.Exists(dir)
                    && Directory.EnumerateFiles(dir, "*.pak").Any()
                    && !File.Exists(Path.Combine(dir, IncompleteMarkerFileName)))
                {
                    tiers.Add(tier);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 读不到就当没装，最坏是退回默认的 HD，与分级功能上线前一样
            }
        }
        return tiers.AsReadOnly();
    }


    /// <summary>
    /// Starward 开始加装、还没装完的分级（目录里有 <see cref="IncompleteMarkerFileName"/>），画质从高到低
    /// </summary>
    public static IReadOnlyList<string> GetIncompleteTiers(string? gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return [];
        }
        var tiers = new List<string>(All.Count);
        foreach (string tier in All)
        {
            try
            {
                if (File.Exists(Path.Combine(gameDir, GetContentFolder(tier), IncompleteMarkerFileName)))
                {
                    tiers.Add(tier);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return tiers.AsReadOnly();
    }


    /// <summary>
    /// 清单里的一个文件属于哪一档，共用的文件返回 null
    /// </summary>
    /// <param name="dest">文件相对于游戏目录的路径，例如 <c>Client/Content/HD/pakchunk1-HD-WindowsNoEditor.pak</c></param>
    public static string? GetTierOfPath(string dest)
    {
        string[] parts = dest.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4
            && string.Equals(parts[0], "Client", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "Content", StringComparison.OrdinalIgnoreCase))
        {
            return Normalize(parts[2]);
        }
        return null;
    }


    /// <summary>
    /// 一份文件清单涵盖了哪几档，画质从高到低
    /// </summary>
    public static IReadOnlyList<string> GetTiersInIndex(IEnumerable<string> dests)
    {
        var found = new HashSet<string>(dests.Select(GetTierOfPath).OfType<string>());
        return All.Where(found.Contains).ToList().AsReadOnly();
    }



    /// <summary>
    /// 玩家自己的启动参数里写的分级。没写时返回 null；写了但不认得时 <paramref name="specified"/> 仍为 true。
    /// </summary>
    public static string? GetTierInArguments(string? arguments, out bool specified)
    {
        specified = false;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }
        Match match = LaunchArgumentRegex.Match(arguments);
        if (!match.Success)
        {
            return null;
        }
        specified = true;
        return Normalize(match.Groups[1].Value);
    }


    /// <summary>
    /// 删掉启动参数里所有的分级参数，删完是空的返回 null
    /// </summary>
    public static string? RemoveTierArgument(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }
        string result = LaunchArgumentWithLeadingSpaceRegex.Replace(arguments, "").Trim();
        return result.Length > 0 ? result : null;
    }



    /// <summary>
    /// 没有其他指示时用哪一档：选过的那档还在就用它，否则 HD，没有 HD 就用装了的最高一档。
    /// 什么都没装（或读不到）时是 HD，与分级功能上线前一样。
    /// </summary>
    /// <param name="installed">装好的分级，见 <see cref="GetInstalledTiers"/></param>
    /// <param name="preferred">启动页上选的分级</param>
    public static string ResolveLaunchTier(IReadOnlyList<string> installed, string? preferred)
    {
        if (Normalize(preferred) is string tier && installed.Contains(tier))
        {
            return tier;
        }
        if (installed.Count == 0 || installed.Contains(Default))
        {
            return Default;
        }
        return installed[0];
    }


    /// <summary>
    /// 决定这次启动用哪一档，并整理玩家自己的启动参数。
    /// <para/>
    /// 优先顺序：启动页上选的 → 玩家在启动参数里写的 → <see cref="ResolveLaunchTier"/>。
    /// 前两者都必须是装好的那几档，否则游戏一定出错：3.7.0 修好崩溃时的更新说明请玩家自己加过
    /// <c>-krqlv=hd</c>，只装了流畅的人照它启动会找不到 HD 目录。读不到装了哪几档时照指示走。
    /// <para/>
    /// 用的不是玩家写的那一档时，把他写的分级参数拿掉，保证命令行上只有一个 <c>-krqlv</c>：
    /// 虚幻的 <c>FParse::Value</c> 只认第一个，但不必依赖这一点。
    /// </summary>
    /// <param name="installed">装好的分级，见 <see cref="GetInstalledTiers"/></param>
    /// <param name="preferred">启动页上选的分级</param>
    /// <param name="startArgument">玩家自己的启动参数</param>
    public static KuroLaunchTierDecision DecideLaunchTier(IReadOnlyList<string> installed, string? preferred, string? startArgument)
    {
        bool Usable(string tier) => installed.Count == 0 || installed.Contains(tier);

        if (Normalize(preferred) is string selected && Usable(selected))
        {
            return new KuroLaunchTierDecision(selected, false, RemoveTierArgument(startArgument));
        }
        if (GetTierInArguments(startArgument, out _) is string written && Usable(written))
        {
            return new KuroLaunchTierDecision(written, true, string.IsNullOrWhiteSpace(startArgument) ? null : startArgument.Trim());
        }
        return new KuroLaunchTierDecision(ResolveLaunchTier(installed, null), false, RemoveTierArgument(startArgument));
    }

}



/// <summary>
/// <see cref="KuroResourceTier.DecideLaunchTier"/> 的结果
/// </summary>
/// <param name="Tier">这次启动用的分级</param>
/// <param name="FromStartArgument">分级写在玩家自己的启动参数里，不必再加</param>
/// <param name="StartArgument">整理过的玩家启动参数：用的不是其中写的那一档时，已经把它拿掉</param>
public sealed record KuroLaunchTierDecision(string Tier, bool FromStartArgument, string? StartArgument)
{

    /// <summary>
    /// 要加在固定参数里的分级参数，玩家的启动参数里已经有了时为 null
    /// </summary>
    public string? TierArgument => FromStartArgument ? null : KuroResourceTier.GetLaunchArgument(Tier);

}
