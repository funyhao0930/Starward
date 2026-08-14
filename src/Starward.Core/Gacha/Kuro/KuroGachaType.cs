namespace Starward.Core.Gacha.Kuro;

/// <summary>
/// 鸣潮的唤取卡池，接口里的 cardPoolType 就是这些值。
/// </summary>
public readonly record struct KuroGachaType(int Value) : IGachaType
{


    /// <summary>
    /// 角色活动唤取
    /// </summary>
    public const int FeaturedResonator = 1;

    /// <summary>
    /// 武器活动唤取
    /// </summary>
    public const int FeaturedWeapon = 2;

    /// <summary>
    /// 角色常驻唤取
    /// </summary>
    public const int StandardResonator = 3;

    /// <summary>
    /// 武器常驻唤取
    /// </summary>
    public const int StandardWeapon = 4;

    /// <summary>
    /// 新手唤取
    /// </summary>
    public const int Beginner = 5;

    /// <summary>
    /// 新手自选唤取
    /// </summary>
    public const int BeginnerChoice = 6;

    /// <summary>
    /// 感恩定向唤取
    /// </summary>
    public const int ThankYou = 7;


    public string ToLocalization() => Value switch
    {
        FeaturedResonator => CoreLang.GachaType_FeaturedResonatorConvene,
        FeaturedWeapon => CoreLang.GachaType_FeaturedWeaponConvene,
        StandardResonator => CoreLang.GachaType_StandardResonatorConvene,
        StandardWeapon => CoreLang.GachaType_StandardWeaponConvene,
        Beginner => CoreLang.GachaType_BeginnerConvene,
        BeginnerChoice => CoreLang.GachaType_BeginnerChoiceConvene,
        ThankYou => CoreLang.GachaType_ThankYouConvene,
        _ => "",
    };


    public override string ToString() => Value.ToString();
    public static implicit operator KuroGachaType(int value) => new(value);
    public static implicit operator int(KuroGachaType gachaType) => gachaType.Value;


}
