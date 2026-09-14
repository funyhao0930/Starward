namespace Starward.Core.Gacha.Gryphline;

public class GryphlineGachaItem : GachaLogItem
{

    public override IGachaType GetGachaType() => new GryphlineGachaType(GachaType);

    public override GryphlineGachaItem Clone() => (GryphlineGachaItem)MemberwiseClone();

}
