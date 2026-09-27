using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Providers.HoYo;

namespace Starward.Features.GameInstall;

/// <summary>
/// 安装器（RPC 进程）用来区分任务的游戏标识。
/// <para/>
/// RPC 的协议沿用米哈游的 <see cref="GameId"/>（GameBiz + Id），改协议会牵动排队、暂停、进度回报整条链路；
/// 其他供应商没有 HoYoPlay 的 GameId，就把 <see cref="GameKey"/> 的正规字符串（例如 <c>kuro:wuwa:global</c>）
/// 同时放进 GameBiz 与 Id。RPC 那边用 <see cref="GameKeyResolver"/> 分辨是哪一种，交给对应的安装器。
/// </summary>
internal static class InstallGameIds
{

    /// <summary>
    /// 米哈游游戏返回 HoYoPlay 的 GameId，其他游戏返回合成的标识。
    /// 无效的键、以及不能由 Starward 安装的游戏（例如只支持启动的异环）返回 null，
    /// 免得调用方把任务送进 RPC 之后才因为没有安装器而失败。
    /// </summary>
    public static GameId? Resolve(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (key.IsProvider(GameProviderIds.HoYo))
        {
            return HoYoGameIds.Resolve(key);
        }
        if (!AppConfig.GetService<IGameProviderRegistry>().SupportsCapability(key, GameCapability.Install))
        {
            return null;
        }
        string settingsKey = GameKeyResolver.ToSettingsKey(key);
        return new GameId { GameBiz = settingsKey, Id = settingsKey };
    }


    /// <summary>
    /// 是否为米哈游游戏的标识。HoYoPlay 的接口只能对这种标识调用。
    /// </summary>
    public static bool IsHoYo(GameId gameId)
    {
        return LauncherId.FromGameId(gameId) is not null;
    }

}
