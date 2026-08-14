namespace Starward.Core.Games;

/// <summary>
/// 一款游戏（含渠道）的展示信息与能力描述，由 <see cref="IGameCatalogProvider"/> 提供。
/// </summary>
public sealed record GameDescriptor
{

    /// <summary>
    /// 通用游戏标识
    /// </summary>
    public required GameKey Key { get; init; }


    /// <summary>
    /// 游戏名称，已本地化
    /// </summary>
    public required string DisplayName { get; init; }


    /// <summary>
    /// 渠道名称，已本地化，例如国服、国际服
    /// </summary>
    public string? ChannelName { get; init; }


    /// <summary>
    /// 游戏图标，可以是 ms-appx:/// 或 http(s):// 链接
    /// </summary>
    public string? IconUri { get; init; }


    /// <summary>
    /// 渠道图标，可以是 ms-appx:/// 或 http(s):// 链接
    /// </summary>
    public string? ChannelIconUri { get; init; }


    /// <summary>
    /// 游戏选择器中的缩略图
    /// </summary>
    public string? ThumbnailUri { get; init; }


    /// <summary>
    /// 游戏选择器中的 Logo
    /// </summary>
    public string? LogoUri { get; init; }


    /// <summary>
    /// 供应商内部的游戏标识，对通用代码是不透明的句柄，
    /// 只用于回传给同一个供应商（例如 HoYoPlay 的远程 GameId）。
    /// 通用代码不应解析其内容。
    /// </summary>
    public string? ProviderGameId { get; init; }


    /// <summary>
    /// 游戏进程名，带 .exe 扩展名。为空时需要供应商在线查询。
    /// </summary>
    public string? ExecutableName { get; init; }


    /// <summary>
    /// 支持的功能
    /// </summary>
    public GameCapability Capabilities { get; init; }


    /// <summary>
    /// 兼容层：对应的旧 GameBiz 字符串。
    /// 应用配置与数据库沿用此值作为键，不能更改，否则用户的既有数据会失效。
    /// 非米哈游游戏为 null。
    /// </summary>
    public string? LegacyGameBiz { get; init; }


    /// <summary>
    /// 是否支持指定的功能，可以同时判断多个标志
    /// </summary>
    public bool HasCapability(GameCapability capability)
    {
        return capability is not GameCapability.None && (Capabilities & capability) == capability;
    }

}
