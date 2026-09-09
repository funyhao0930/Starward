namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环「斯卡布罗集市」的卡池。
/// <para/>
/// 异环没有抽卡接口，记录只能从 nte-exporter 导出的文件读入，
/// 因此这里的编号是 Starward 自己定的，与游戏无关，但一旦定下就不能再改：
/// 它会写进数据库的 GachaType 列。文件里对应的是字符串 pool id，
/// 两者的映射见 <see cref="FromPoolId"/>。
/// </summary>
public readonly record struct HottaGachaType(int Value) : IGachaType
{


    /// <summary>
    /// 常驻棋盘 Standard Board
    /// </summary>
    public const int StandardBoard = 1;

    /// <summary>
    /// 限定角色棋盘 Limited Character Board
    /// </summary>
    public const int LimitedCharacterBoard = 2;

    /// <summary>
    /// 弧盘奇迹盒 Arc Miracle Box，异环的「武器」池
    /// </summary>
    public const int ArcMiracleBox = 3;

    /// <summary>
    /// 神秘盒子 Mystery Box，按轮次开放的单抽池
    /// </summary>
    public const int MysteryBox = 4;


    /// <summary>
    /// nte-exporter 文件里的 pool id
    /// </summary>
    public const string PoolIdStandardBoard = "Lottery_Permanent";
    public const string PoolIdLimitedCharacterBoard = "Lottery_LimitedCharacter";
    public const string PoolIdArcMiracleBox = "Arc_MiracleBox";
    public const string PoolIdMysteryBox = "Gashapon_MysteryBox";


    /// <summary>
    /// 把导出文件里的 pool id 换成本地编号，认不出来时返回 0。
    /// <para/>
    /// 导出文件里每条记录都自带 pool_group_id，顶层的 banner 只是取第一条记录推断出来的，
    /// 记录为空时还会谎报成常驻池，因此只认记录自己的那个字段。
    /// </summary>
    public static HottaGachaType FromPoolId(string? poolId) => poolId switch
    {
        PoolIdStandardBoard => StandardBoard,
        PoolIdLimitedCharacterBoard => LimitedCharacterBoard,
        PoolIdArcMiracleBox => ArcMiracleBox,
        PoolIdMysteryBox => MysteryBox,
        _ => new HottaGachaType(0),
    };


    public string ToLocalization() => Value switch
    {
        StandardBoard => CoreLang.GachaType_StandardBoard,
        LimitedCharacterBoard => CoreLang.GachaType_LimitedCharacterBoard,
        ArcMiracleBox => CoreLang.GachaType_ArcMiracleBox,
        MysteryBox => CoreLang.GachaType_MysteryBox,
        _ => "",
    };


    public override string ToString() => Value.ToString();
    public static implicit operator HottaGachaType(int value) => new(value);
    public static implicit operator int(HottaGachaType gachaType) => gachaType.Value;


}
