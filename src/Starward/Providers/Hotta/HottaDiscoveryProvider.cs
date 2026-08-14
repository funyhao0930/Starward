using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 搜索已安装的异环。官方启动器在卸载项中写入了 InstallLocation，
/// 键名由启动器 Config.ini 的 [Global] reg 指定。
/// </summary>
internal partial class HottaDiscoveryProvider : UninstallRegistryDiscoveryProvider
{

    public HottaDiscoveryProvider(ILogger<HottaDiscoveryProvider> logger)
        : base(logger, new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors))
    {
    }


    public override string ProviderId => HottaGameMapping.ProviderId;


    protected override IReadOnlyList<GameKey> SupportedGameKeys => HottaGameMapping.SupportedGameKeys;


    protected override string? GetUninstallKeyName(GameKey key)
    {
        return key == HottaGameMapping.NevernessToEvernessTaiwan ? HottaGameMapping.TaiwanUninstallKey : null;
    }


    /// <summary>
    /// 异环把本地版本号记录在官方启动器的 Config.ini 中：[VERSION] Version=1.0.8.0727
    /// </summary>
    public async ValueTask<Version?> GetLocalVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            string path = Path.Combine(installPath, HottaGameMapping.ConfigFileRelativePath);
            if (!File.Exists(path))
            {
                return null;
            }
            string text = await File.ReadAllTextAsync(path, cancellationToken);
            Match match = VersionRegex().Match(text);
            if (match.Success && Version.TryParse(match.Groups[1].Value.Trim(), out Version? version))
            {
                return version;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Read Neverness to Everness local version");
        }
        return null;
    }


    [GeneratedRegex(@"(?m)^\s*Version\s*=\s*(.+)$")]
    private static partial Regex VersionRegex();

}
