namespace Starward.Core.Gacha.Hotta;

public class HottaGachaItem : GachaLogItem
{

    public override IGachaType GetGachaType() => new HottaGachaType(GachaType);

    public override HottaGachaItem Clone() => (HottaGachaItem)MemberwiseClone();

}
