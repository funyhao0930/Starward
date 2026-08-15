using Microsoft.UI.Xaml.Navigation;
using Starward.Frameworks;

namespace Starward.Features.Gacha;

/// <summary>
/// 声明了抽卡能力、但还没有实现协议的游戏。
/// <para/>
/// 「这款游戏有抽卡记录」与「Starward 已经抓得到」是两件事：
/// 前者由 <see cref="Starward.Core.Games.GameCapability.Gacha"/> 声明，决定导航里有没有这一项；
/// 后者由 <see cref="GachaProviderRegistry"/> 有没有对应的服务决定。
/// 两者不一致时导航到本页，而不是让入口消失或让页面空引用。
/// </summary>
public sealed partial class GachaLogPlaceholderPage : PageBase
{

    public GachaLogPlaceholderPage()
    {
        this.InitializeComponent();
    }


    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 标题仍是这款游戏自己对抽卡的叫法，避免点进来像是走错页面
        GachaTypeText = GachaLogService.GetGachaLogText(CurrentGameKey);
    }


    public string GachaTypeText { get; set => SetProperty(ref field, value); } = "";

}
