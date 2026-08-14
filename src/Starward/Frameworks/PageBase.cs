using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Providers.HoYo;

namespace Starward.Frameworks;

[INotifyPropertyChanged]
public abstract partial class PageBase : Page
{


    /// <summary>
    /// 当前游戏，页面的唯一身份来源。
    /// </summary>
    public GameKey CurrentGameKey
    {
        get;
        protected set
        {
            if (SetProperty(ref field, value))
            {
                CurrentGameBiz = new GameBiz(value.IsValid ? GameKeyResolver.ToSettingsKey(value) : "");
                OnPropertyChanged(nameof(CurrentGameId));
            }
        }
    }


    /// <summary>
    /// 兼容层：HoYoPlay 的游戏标识，由 <see cref="CurrentGameKey"/> 推导。
    /// 只有米哈游专属功能（抽卡、实时便笺、云游戏、安装器）需要它，
    /// 其他游戏为 null。
    /// </summary>
    public GameId? CurrentGameId => HoYoGameIds.Resolve(CurrentGameKey);


    /// <summary>
    /// 已确定是米哈游游戏时使用，例如抽卡、实时便笺等只在对应能力存在时才可达的功能，
    /// 或已经通过能力判断的代码分支。
    /// <para/>
    /// 它把「这里一定是米哈游游戏」这个假设写明，并在假设不成立时立刻失败，
    /// 而不是留下一个空引用等着在更深的地方炸开。
    /// </summary>
    protected GameId RequiredGameId => CurrentGameId
        ?? throw new GameCapabilityNotSupportedException(CurrentGameKey, GameCapability.Install,
                                                        $"Game '{CurrentGameKey}' has no HoYoPlay game id.");


    /// <summary>
    /// 应用配置、数据库与注册表使用的键，随 <see cref="CurrentGameKey"/> 自动更新。
    /// 米哈游游戏仍是旧的 GameBiz 字符串，其他供应商是 GameKey 的正规字符串。
    /// <para/>
    /// 它是存储键而不是身份，因此允许页面覆写成更精确的值，
    /// 例如游戏设置页把崩坏三国际服细分成日服、韩服，
    /// 或游戏记录页把 Bilibili 渠道归到国服。
    /// </summary>
    public GameBiz CurrentGameBiz { get; protected set => SetProperty(ref field, value); }



    public PageBase()
    {
        Loaded += PageEx_Loaded;
        Unloaded += PageEx_Unloaded;
    }



    private void PageEx_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        OnLoaded();
    }


    private void PageEx_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= PageEx_Loaded;
        Unloaded -= PageEx_Unloaded;
        OnUnloaded();
    }



    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        switch (e.Parameter)
        {
            case GameKey key:
                CurrentGameKey = key;
                break;
            // 兼容尚未迁移的调用方
            case GameId id when GameKeyResolver.Resolve(id.GameBiz.Value) is GameKey fromId:
                CurrentGameKey = fromId;
                break;
            case GameBiz biz when GameKeyResolver.Resolve(biz.Value) is GameKey fromBiz:
                CurrentGameKey = fromBiz;
                break;
        }
    }



    protected virtual void OnLoaded()
    {

    }



    protected virtual void OnUnloaded()
    {

    }


}
