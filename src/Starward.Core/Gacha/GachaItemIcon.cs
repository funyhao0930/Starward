namespace Starward.Core.Gacha;

/// <summary>
/// 一个抽卡物品的图示。
/// <para/>
/// 米哈游以外的记录接口都不给图，图示另外从社群的数据站取，
/// 按 <paramref name="ItemId"/> 对上记录。
/// </summary>
/// <param name="ItemId">与记录里的 ItemId 相同；物品 ID 是字符串的游戏，这里是 <see cref="GachaSyntheticId.ToItemId"/> 散列后的值</param>
/// <param name="Key">游戏内的原始 ID，如 <c>chr_0005_chen</c>、<c>fork_Rose</c>、<c>1203</c></param>
/// <param name="Icon">图片 URL</param>
public record GachaItemIcon(int ItemId, string Key, string Icon);
