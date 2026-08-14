using Starward.Core.Games;
using System.Collections.Generic;


namespace Starward.Features.GameSelector;

/// <summary>
/// 游戏选择器中的一款游戏（不含渠道），以及它的所有渠道。
/// </summary>
public class GameBizDisplay
{

    /// <summary>
    /// 代表渠道的游戏标识，用于取得展示用的图片
    /// </summary>
    public GameKey GameKey { get; set; }


    /// <summary>
    /// 小缩略背景图
    /// </summary>
    public string? ThumbnailUri { get; set; }


    /// <summary>
    /// 游戏 Logo
    /// </summary>
    public string? LogoUri { get; set; }


    /// <summary>
    /// 游戏图标
    /// </summary>
    public string? IconUri { get; set; }


    /// <summary>
    /// 该游戏的所有渠道
    /// </summary>
    public List<GameBizIcon> Servers { get; set; } = new();

}
