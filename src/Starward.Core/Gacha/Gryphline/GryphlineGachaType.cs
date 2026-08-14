namespace Starward.Core.Gacha.Gryphline;

/// <summary>
/// 终末地的寻访卡池。
/// <para/>
/// 接口用的是字符串（角色池 <c>E_CharacterGachaPoolType_*</c>，武器另有接口），
/// Starward 的记录以整数区分卡池，因此这里给每个池子编号，
/// <see cref="ToPoolTypeParameter"/> 负责翻译回接口认识的写法。
/// </summary>
public readonly record struct GryphlineGachaType(int Value) : IGachaType
{


    /// <summary>
    /// 特许寻访
    /// </summary>
    public const int Special = 1;

    /// <summary>
    /// 辉光庆典
    /// </summary>
    public const int Joint = 2;

    /// <summary>
    /// 基础寻访
    /// </summary>
    public const int Standard = 3;

    /// <summary>
    /// 启程寻访
    /// </summary>
    public const int Beginner = 4;

    /// <summary>
    /// 武器申领，走单独的接口，没有 pool_type 参数
    /// </summary>
    public const int Weapon = 5;


    public string ToLocalization() => Value switch
    {
        Special => CoreLang.GachaType_SpecialRecruitment,
        Joint => CoreLang.GachaType_JointRecruitment,
        Standard => CoreLang.GachaType_StandardRecruitment,
        Beginner => CoreLang.GachaType_BeginnerRecruitment,
        Weapon => CoreLang.GachaType_WeaponRecruitment,
        _ => "",
    };


    /// <summary>
    /// 角色池在接口里的 pool_type，武器池返回 null（该接口不接受这个参数）
    /// </summary>
    public string? ToPoolTypeParameter() => Value switch
    {
        Special => "E_CharacterGachaPoolType_Special",
        Joint => "E_CharacterGachaPoolType_Joint",
        Standard => "E_CharacterGachaPoolType_Standard",
        Beginner => "E_CharacterGachaPoolType_Beginner",
        _ => null,
    };


    /// <summary>
    /// 是否是武器池
    /// </summary>
    public bool IsWeapon => Value == Weapon;


    public override string ToString() => Value.ToString();
    public static implicit operator GryphlineGachaType(int value) => new(value);
    public static implicit operator int(GryphlineGachaType gachaType) => gachaType.Value;


}
