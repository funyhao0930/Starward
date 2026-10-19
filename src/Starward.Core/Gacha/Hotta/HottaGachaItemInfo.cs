namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环物品的图示，另带稀有度与英文名，都取自 NTE_Assets 的数据表。
/// <para/>
/// 稀有度与名字只在导出文件缺的时候补上，见 <see cref="HottaGachaClient.GetGachaIconsAsync"/>。
/// </summary>
/// <param name="Icon">图片 URL，数据表里没有图的物品是空串</param>
/// <param name="RankType">由物品品质换算的星数档位，认不出来是 0</param>
/// <param name="Name">英文名，本地化文件里查不到时为 null</param>
public record HottaGachaItemInfo(int ItemId, string Key, string Icon, int RankType, string? Name) : GachaItemIcon(ItemId, Key, Icon);
