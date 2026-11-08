namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环物品的图示，另带稀有度与名字，都取自 NTE_Assets 的数据表与游戏的本地化文本。
/// <para/>
/// 稀有度与英文名只在导出文件缺的时候补上；中文名在界面是中文时优先使用，
/// 见 <see cref="HottaGachaClient.GetGachaIconsAsync"/> 与 <see cref="HottaGachaNames.Localize(string?, string, string?, string?, System.Globalization.CultureInfo?)"/>。
/// </summary>
/// <param name="Icon">图片 URL，数据表里没有图的物品是空串</param>
/// <param name="RankType">由物品品质换算的星数档位，认不出来是 0</param>
/// <param name="Name">英文名，本地化文件里查不到时为 null</param>
/// <param name="NameHant">繁体中文名，查不到时为 null</param>
/// <param name="NameHans">简体中文名，查不到时为 null</param>
public record HottaGachaItemInfo(int ItemId, string Key, string Icon, int RankType, string? Name, string? NameHant, string? NameHans) : GachaItemIcon(ItemId, Key, Icon);
