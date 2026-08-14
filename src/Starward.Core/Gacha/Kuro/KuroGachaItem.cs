namespace Starward.Core.Gacha.Kuro;

public class KuroGachaItem : GachaLogItem
{

    public override IGachaType GetGachaType() => new KuroGachaType(GachaType);

    public override KuroGachaItem Clone() => (KuroGachaItem)MemberwiseClone();

}
