
using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;

namespace Starward.Features.Gacha;

[INotifyPropertyChanged]
public partial class GachaLogItemEx : GachaLogItem
{

    /// <summary>
    /// 不要删除，导出 Excel 时有用
    /// </summary>
    public string IdText => Id.ToString();

    /// <summary>
    /// 相同保底卡池中的顺序
    /// </summary>
    public int Index { get; set; }

    public int Pity { get; set; }

    public string Icon { get; set; }


    /// <summary>
    /// 本条记录所在卡池的保底抽数，为 null 时按卡池编号推断。
    /// <para/>
    /// 推断那套规则只认米哈游三款的卡池编号，而各家的编号是各自从 1 开始数的，
    /// 撞号在所难免（例如绝区零的音擎频段是 3，别家的 3 未必是武器池）。
    /// 因此非米哈游的游戏由 <see cref="GachaLogService.GetPityRule"/> 明确给出，
    /// 不参与推断。
    /// </summary>
    public int? PityMax { get; set; }

    /// <summary>
    /// 软保底起点，进度条从这一抽起变红，为 null 时同样按卡池编号推断
    /// </summary>
    public int? SoftPity { get; set; }


    /// <summary>
    /// 异环专用，见 <see cref="Starward.Core.Gacha.Hotta.HottaGachaItem.ResultType"/>。
    /// 别的游戏一行就是一抽，这一列是空的。
    /// </summary>
    public string? ResultType { get; set; }

    /// <summary>
    /// 异环专用，导出文件里的 <c>reward_id</c> 原文，用来查中文名
    /// </summary>
    public string? RewardId { get; set; }


    public double Progress => (double)Pity / (PityMax ?? ((GachaType is GenshinGachaType.WeaponEventWish or StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp or ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel) ? 80 : 90)) * 100;


    public bool IsPointerIn { get; set => SetProperty(ref field, value); }

    public int ItemCount { get; set; }


    public bool HasUpItem { get; set; }

    public bool IsUp { get; set; }

    public double UpTextOpacity => IsUp ? 1 : 0;

}
