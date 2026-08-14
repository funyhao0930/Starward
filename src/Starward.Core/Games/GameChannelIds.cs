namespace Starward.Core.Games;

/// <summary>
/// 常见的渠道标识。供应商可以使用自己的渠道标识，此处只是通用约定。
/// </summary>
public static class GameChannelIds
{

    /// <summary>
    /// 国服
    /// </summary>
    public const string China = "cn";


    /// <summary>
    /// 国际服
    /// </summary>
    public const string Global = "global";


    /// <summary>
    /// Bilibili 渠道服
    /// </summary>
    public const string Bilibili = "bilibili";


    /// <summary>
    /// 仅有单一渠道的游戏使用
    /// </summary>
    public const string Default = "default";

}
