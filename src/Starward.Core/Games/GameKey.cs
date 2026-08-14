using System.Diagnostics.CodeAnalysis;

namespace Starward.Core.Games;

/// <summary>
/// 通用游戏标识，不依赖任何游戏公司或协议。
/// </summary>
/// <param name="ProviderId">游戏供应商或协议来源，例如 <see cref="GameProviderIds.HoYo"/></param>
/// <param name="GameId">游戏本身的标识，例如 hk4e</param>
/// <param name="ChannelId">渠道标识，例如 cn、global、bilibili</param>
public readonly record struct GameKey(string ProviderId, string GameId, string ChannelId)
{

    /// <summary>
    /// 正规字符串形式的分隔符
    /// </summary>
    public const char Separator = ':';


    /// <summary>
    /// 三个部分都不为空
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(ProviderId)
                        && !string.IsNullOrWhiteSpace(GameId)
                        && !string.IsNullOrWhiteSpace(ChannelId);


    /// <summary>
    /// 是否属于指定的供应商
    /// </summary>
    public bool IsProvider(string providerId) => string.Equals(ProviderId, providerId, StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// 同一款游戏（忽略渠道）
    /// </summary>
    public bool IsSameGame(GameKey other) => string.Equals(ProviderId, other.ProviderId, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(GameId, other.GameId, StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// 正规字符串形式，例如 hoyo:hk4e:cn
    /// </summary>
    public override string ToString() => $"{ProviderId}{Separator}{GameId}{Separator}{ChannelId}";


    /// <summary>
    /// 解析正规字符串形式，例如 hoyo:hk4e:cn
    /// </summary>
    public static bool TryParse(string? value, out GameKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        string[] parts = value.Split(Separator);
        if (parts.Length != 3)
        {
            return false;
        }
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }
        key = new GameKey(parts[0], parts[1], parts[2]);
        return true;
    }


    /// <summary>
    /// 解析正规字符串形式，失败时返回 null
    /// </summary>
    [return: NotNullIfNotNull(nameof(defaultValue))]
    public static GameKey? Parse(string? value, GameKey? defaultValue = null)
    {
        return TryParse(value, out GameKey key) ? key : defaultValue;
    }

}
