using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core.Games;
using Starward.Core.Games.Manjuu;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Manjuu;

/// <summary>
/// 搜索已安装的蓝色星原：旅谣。
/// <para/>
/// 官方启动器在 HKLM 卸载项写入 InstallLocation（启动器根目录），游戏本体在其下的子目录；
/// 本地版本号另外记在 HKCU 的启动器键里。
/// 最新版本要走签名过的官方接口，理由见 <see cref="ManjuuGameMapping"/>，因此不查。
/// </summary>
internal class ManjuuDiscoveryProvider : UninstallRegistryDiscoveryProvider
{

    public ManjuuDiscoveryProvider(ILogger<ManjuuDiscoveryProvider> logger)
        : base(logger, new SimpleGameCatalogProvider(ManjuuGameMapping.ProviderId, ManjuuGameMapping.GetDescriptors))
    {

    }


    public override string ProviderId => ManjuuGameMapping.ProviderId;


    protected override IReadOnlyList<GameKey> SupportedGameKeys => ManjuuGameMapping.SupportedGameKeys;


    protected override string? GetUninstallKeyName(GameKey key)
    {
        return key == ManjuuGameMapping.AzurPromiliaTC ? ManjuuGameMapping.TCUninstallKey : null;
    }


    /// <summary>
    /// 官方启动器把本地版本号记在 HKCU\Software\Azurpromilia_TC\43_C500 的 Version（如 1.0.0.2646520）。
    /// 每次更新完都会重写，比游戏目录里的 _index 好认。
    /// </summary>
    public override ValueTask<Version?> GetLocalVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        if (key != ManjuuGameMapping.AzurPromiliaTC)
        {
            return ValueTask.FromResult<Version?>(null);
        }
        try
        {
            using RegistryKey? launcher = Registry.CurrentUser.OpenSubKey(ManjuuGameMapping.TCLauncherRegistryKey);
            string? text = launcher?.GetValue(ManjuuGameMapping.LauncherVersionValueName) as string;
            return ValueTask.FromResult(ManjuuGameMapping.ParseVersion(text));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Read Azur Promilia local version");
        }
        return ValueTask.FromResult<Version?>(null);
    }

}
