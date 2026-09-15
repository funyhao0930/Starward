
using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System.Globalization;

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
    /// 有没有图标。
    /// <para/>
    /// 米哈游三款的图标链接随记录一起存在库里，其余三款一直是空的：
    /// 鸣潮与终末地的抽卡接口都不返回图，异环连抽卡接口都没有。
    /// 公开的数据表只给 ID 与名称，图本身在游戏的资源包里，没有可以长期依赖的图床，
    /// 因此这里不强行找图，改由界面画一个按稀有度配色的占位块。
    /// </summary>
    public bool HasIcon => !string.IsNullOrEmpty(Icon);


    public bool HasNoIcon => !HasIcon;


    /// <summary>
    /// 占位块上的字：品项名的第一个字。
    /// <para/>
    /// 只是视觉上的锚点，不用于分辨品项——完整的名称就显示在它右边。
    /// 按字素取而不是按 char 取，否则碰到代理对会截出半个字。
    /// </summary>
    public string IconPlaceholderText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "";
            }
            string name = Name.TrimStart();
            return StringInfo.GetNextTextElement(name, 0).ToUpperInvariant();
        }
    }


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


    /// <summary>
    /// 终末地专用，这一抽属于哪一期卡池，见
    /// <see cref="Starward.Core.Gacha.Gryphline.GryphlineGachaItem.PoolId"/>
    /// </summary>
    public string? PoolId { get; set; }

    /// <summary>
    /// 终末地专用，是不是赠送的一抽，见
    /// <see cref="Starward.Core.Gacha.Gryphline.GryphlineGachaItem.IsFree"/>
    /// </summary>
    public bool IsFree { get; set; }


    public double Progress => (double)Pity / (PityMax ?? ((GachaType is GenshinGachaType.WeaponEventWish or StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp or ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel) ? 80 : 90)) * 100;


    public bool IsPointerIn { get; set => SetProperty(ref field, value); }

    public int ItemCount { get; set; }


    public bool HasUpItem { get; set; }

    public bool IsUp { get; set; }

    public double UpTextOpacity => IsUp ? 1 : 0;

}
