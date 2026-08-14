namespace Starward.Core.Games;

/// <summary>
/// 所有游戏供应商接口的基础，标识自己属于哪个供应商。
/// </summary>
public interface IGameProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }

}
