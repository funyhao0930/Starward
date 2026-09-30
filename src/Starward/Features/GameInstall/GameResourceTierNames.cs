namespace Starward.Features.GameInstall;

/// <summary>
/// 资源分级的显示名称，照官方启动器各语言的写法（繁中是極致、高畫質、流暢）。
/// 启动页、安装对话框与设置对话框共用。
/// </summary>
public static class GameResourceTierNames
{

    public static string Get(string tier) => tier switch
    {
        "uhd" => Lang.GameLauncherPage_ResourceTier_UHD,
        "hd" => Lang.GameLauncherPage_ResourceTier_HD,
        "sd" => Lang.GameLauncherPage_ResourceTier_SD,
        _ => tier.ToUpperInvariant(),
    };

}
