using Starward.Core.Games.Kuro;
using System.Text.Json;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 把新启动器的分级配置（<see cref="KuroOfficialGameIndex"/>）整理成要处理的资源包，
/// 并决定一次下载走旧版配置还是分级配置。
/// <para/>
/// 与 <see cref="KuroDownloadPlanner"/> 一样全是纯计算，不碰网络与磁盘。
/// </summary>
public static class KuroResourcePackPlanner
{

    /// <summary>
    /// 某几档资源要用到的资源包名，共用包在前，不重复。
    /// 其中任何一档在 bundles 里找不到时返回空，表示这份配置给不了。
    /// </summary>
    public static IReadOnlyList<string> GetPackNames(IReadOnlyDictionary<string, KuroResourceBundle>? bundles, IEnumerable<string> tiers)
    {
        var wanted = new HashSet<string>(tiers.Select(KuroResourceTier.Normalize).OfType<string>());
        if (bundles is null || wanted.Count == 0)
        {
            return [];
        }
        var names = new List<string>();
        foreach (string tier in KuroResourceTier.All.Where(wanted.Contains))
        {
            if (Find(bundles, tier)?.ResourcePacks is not { Count: > 0 } packs)
            {
                return [];
            }
            foreach (string pack in packs)
            {
                if (!string.IsNullOrWhiteSpace(pack) && !names.Contains(pack, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(pack);
                }
            }
        }
        return names.AsReadOnly();
    }


    /// <summary>
    /// 某几档资源要用到的资源包与各自的下载配置。缺了任何一个、或配置里没有文件清单时返回 null。
    /// </summary>
    public static IReadOnlyList<KuroResourcePack>? GetPacks(IReadOnlyDictionary<string, KuroLauncherGameConfig>? packs,
                                                            IReadOnlyDictionary<string, KuroResourceBundle>? bundles,
                                                            IEnumerable<string> tiers)
    {
        IReadOnlyList<string> names = GetPackNames(bundles, tiers);
        if (names.Count == 0 || packs is null)
        {
            return null;
        }
        var list = new List<KuroResourcePack>(names.Count);
        foreach (string name in names)
        {
            if (Find(packs, name) is not KuroLauncherGameConfig config
                || string.IsNullOrWhiteSpace(config.IndexFile)
                || string.IsNullOrWhiteSpace(config.BaseUrl))
            {
                return null;
            }
            list.Add(new KuroResourcePack(name, config));
        }
        return list.AsReadOnly();
    }


    /// <summary>
    /// 线上版本。各资源包的版本一样，取共用包的，没有就取第一个有版本号的。
    /// </summary>
    public static string? GetVersion(IReadOnlyDictionary<string, KuroLauncherGameConfig>? packs)
    {
        if (packs is null)
        {
            return null;
        }
        if (Find(packs, "common")?.Version is string common && !string.IsNullOrWhiteSpace(common))
        {
            return common;
        }
        return packs.Values.Select(x => x.Version).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }


    /// <summary>
    /// 每一档的大小，画质从高到低，给安装与切换分级的界面用。配置里没有的分级不列出。
    /// </summary>
    public static IReadOnlyList<KuroResourceTierSize> GetTierSizes(KuroOfficialGameIndex index)
    {
        var result = new List<KuroResourceTierSize>();
        foreach (string tier in KuroResourceTier.All)
        {
            if (GetPacks(index.ResourcePacks, index.Bundles, [tier]) is not { } packs)
            {
                continue;
            }
            // 几档都用到的包（common）是共用的，只属于这一档的才是加装或删除这一档时的大小
            IReadOnlyList<string> shared = KuroResourceTier.All.Where(x => x != tier)
                                                               .SelectMany(x => GetPackNames(index.Bundles, [x]))
                                                               .ToList();
            long total = packs.Sum(x => x.Config.Size);
            long own = packs.Where(x => !shared.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).Sum(x => x.Config.Size);
            IReadOnlyDictionary<string, string>? names = Find(index.Bundles!, tier)?.Config?.DisplayName;
            result.Add(new KuroResourceTierSize(tier, total, own, names));
        }
        return result.AsReadOnly();
    }


    /// <summary>
    /// 分级配置里的预下载。3.7.0 时是 null；猜它与顶层同形（cdnList + resourcePacks，bundles 可有可无），
    /// 对不上就当没有：宁可不开放预下载，也不照着猜错的清单下载。
    /// </summary>
    public static KuroOfficialPredownload? GetPredownload(KuroOfficialGameIndex index)
    {
        if (index.Predownload is not JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return null;
        }
        try
        {
            KuroOfficialPredownload? predownload = element.Deserialize(KuroLauncherJsonContext.Default.KuroOfficialPredownload);
            if (predownload?.ResourcePacks is not { Count: > 0 })
            {
                return null;
            }
            predownload.Bundles ??= index.Bundles;
            predownload.CdnList ??= index.CdnList;
            return predownload;
        }
        catch (JsonException)
        {
            return null;
        }
    }



    /// <summary>
    /// 一次下载走旧版配置还是分级配置，两份都给不了时返回 null。
    /// <para/>
    /// 只装 HD 时优先旧版：文件与分级配置的 common + hd 逐个相同，但从 3.6.x 更新上来时旧版的补丁小一半以上
    /// （3.6.1 → 3.7.0 旧版 26.9 GB；分级配置的 common 13.1 GB，hd 没有补丁要整包 45.7 GB），
    /// 它也是目前没拿到测试资格的玩家在用、验证最久的那一份。
    /// 旧版的版本落后（官方不再更新它）或读不到时改走分级配置；牵涉极致或流畅时只能走分级配置。
    /// </summary>
    /// <param name="tiers">要处理的分级</param>
    /// <param name="legacyVersion">旧版配置的版本，读不到时为 null</param>
    /// <param name="tieredVersion">分级配置的版本，读不到、或给不了这几档时为 null</param>
    public static KuroDownloadSource? ChooseSource(IEnumerable<string> tiers, string? legacyVersion, string? tieredVersion)
    {
        bool hdOnly = tiers.All(x => KuroResourceTier.Normalize(x) == KuroResourceTier.HD);
        bool legacy = !string.IsNullOrWhiteSpace(legacyVersion);
        bool tiered = !string.IsNullOrWhiteSpace(tieredVersion);
        if (hdOnly && legacy && (!tiered || CompareVersion(legacyVersion!, tieredVersion!) >= 0))
        {
            return KuroDownloadSource.Legacy;
        }
        if (tiered)
        {
            return KuroDownloadSource.Tiered;
        }
        return null;
    }


    /// <summary>
    /// 比较版本号，认不得的写法按字符串比
    /// </summary>
    public static int CompareVersion(string a, string b)
    {
        if (Version.TryParse(a, out Version? va) && Version.TryParse(b, out Version? vb))
        {
            return va.CompareTo(vb);
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>
    /// 不分大小写地取值：bundles 的键是大写（HD），资源包名是小写（hd），不能指望一直如此
    /// </summary>
    private static T? Find<T>(IReadOnlyDictionary<string, T> dictionary, string key) where T : class
    {
        if (dictionary.TryGetValue(key, out T? value))
        {
            return value;
        }
        foreach ((string k, T v) in dictionary)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }
        return null;
    }

}



/// <summary>
/// 走哪一份配置下载
/// </summary>
public enum KuroDownloadSource
{

    /// <summary>
    /// 旧启动器的配置（<see cref="KuroLauncherGameIndex"/>），只有 HD
    /// </summary>
    Legacy,

    /// <summary>
    /// 新启动器的分级配置（<see cref="KuroOfficialGameIndex"/>）
    /// </summary>
    Tiered,

}



/// <summary>
/// 一个资源包
/// </summary>
/// <param name="Name">包名，例如 common、hd</param>
/// <param name="Config">下载配置</param>
public sealed record KuroResourcePack(string Name, KuroLauncherGameConfig Config);



/// <summary>
/// 一档资源的大小
/// </summary>
/// <param name="Tier">分级</param>
/// <param name="TotalBytes">全新安装这一档要下载的字节数（含共用包）</param>
/// <param name="TierBytes">只属于这一档的字节数，加装或删除这一档时的大小</param>
/// <param name="DisplayName">官方各语言的名称</param>
public sealed record KuroResourceTierSize(string Tier, long TotalBytes, long TierBytes, IReadOnlyDictionary<string, string>? DisplayName);
