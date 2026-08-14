using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using System.Collections.Generic;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 搜索已安装的明日方舟：终末地。
/// <para/>
/// GRYPHLINK 启动器的卸载项键名是一段哈希，不能写死，只能按显示名称匹配。
/// 卸载项记录的是启动器的安装根目录，游戏在它下面的 games 子目录中。
/// </summary>
internal class GryphlineDiscoveryProvider : UninstallRegistryDiscoveryProvider
{

    public GryphlineDiscoveryProvider(ILogger<GryphlineDiscoveryProvider> logger)
        : base(logger, new SimpleGameCatalogProvider(GryphlineGameMapping.ProviderId, GryphlineGameMapping.GetDescriptors))
    {
    }


    public override string ProviderId => GryphlineGameMapping.ProviderId;


    protected override IReadOnlyList<GameKey> SupportedGameKeys => GryphlineGameMapping.SupportedGameKeys;


    protected override string? GetUninstallDisplayName(GameKey key) => GryphlineGameMapping.LauncherDisplayName;


    /// <summary>
    /// 卸载项记录的是启动器根目录，游戏的可执行文件路径已经包含 games 子目录，
    /// 因此安装目录就是启动器根目录，无需调整。
    /// </summary>
    protected override string? ResolveGameFolder(GameKey key, string installLocation) => installLocation;

}
