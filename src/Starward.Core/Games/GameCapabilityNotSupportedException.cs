namespace Starward.Core.Games;

/// <summary>
/// 对不支持该功能的游戏调用了只有部分供应商才有的接口。
/// <para/>
/// 例如只支持启动的游戏没有 HoYoPlay 那样的在线安装包接口，
/// 以前这种情况会得到难以理解的 <c>Unknown launcher id</c>，
/// 现在会明确指出是哪款游戏缺少哪项能力。
/// </summary>
public class GameCapabilityNotSupportedException : NotSupportedException
{

    /// <summary>
    /// 被调用的游戏
    /// </summary>
    public GameKey Key { get; }


    /// <summary>
    /// 缺少的能力
    /// </summary>
    public GameCapability Capability { get; }


    public GameCapabilityNotSupportedException(GameKey key, GameCapability capability)
        : base($"Game '{key}' does not support {capability}.")
    {
        Key = key;
        Capability = capability;
    }


    public GameCapabilityNotSupportedException(GameKey key, GameCapability capability, string message) : base(message)
    {
        Key = key;
        Capability = capability;
    }

}
