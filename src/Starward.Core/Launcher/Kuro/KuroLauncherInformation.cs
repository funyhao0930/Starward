using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器首页的轮播图与资讯配置。
/// <para/>
/// 与背景图同属一族路径，按语言分文件，不需要登录。
/// </summary>
public class KuroLauncherInformation
{

    /// <summary>
    /// 分组资讯。三组的键是固定的英文名，只有 <c>title</c> 随语言变，
    /// 因此分类不必猜，直接按键对应。
    /// </summary>
    [JsonPropertyName("guidance")]
    public KuroLauncherGuidance? Guidance { get; set; }


    /// <summary>
    /// 轮播图
    /// </summary>
    [JsonPropertyName("slideshow")]
    public List<KuroLauncherSlide>? Slideshow { get; set; }

}


public class KuroLauncherGuidance
{

    [JsonPropertyName("notice")]
    public KuroLauncherGuidanceGroup? Notice { get; set; }


    [JsonPropertyName("activity")]
    public KuroLauncherGuidanceGroup? Activity { get; set; }


    [JsonPropertyName("news")]
    public KuroLauncherGuidanceGroup? News { get; set; }

}


public class KuroLauncherGuidanceGroup
{

    /// <summary>
    /// 分组标题，已按语言本地化。Starward 用自己的文案，这里只作参考。
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }


    /// <summary>
    /// 功能开关，不是 <see cref="FUNCTION_ON"/> 时官方启动器不显示这一组。
    /// 目前「活动」长期为 0 且内容为空。
    /// </summary>
    [JsonPropertyName("functionSwitch")]
    public int FunctionSwitch { get; set; }


    [JsonPropertyName("contents")]
    public List<KuroLauncherGuidanceItem>? Contents { get; set; }


    public const int FUNCTION_ON = 1;

}


public class KuroLauncherGuidanceItem
{

    /// <summary>
    /// 标题
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }


    [JsonPropertyName("jumpUrl")]
    public string? JumpUrl { get; set; }


    /// <summary>
    /// 日期，接口返回 <c>MM-dd</c>，没有年份
    /// </summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

}


public class KuroLauncherSlide
{

    [JsonPropertyName("url")]
    public string? Url { get; set; }


    [JsonPropertyName("jumpUrl")]
    public string? JumpUrl { get; set; }


    /// <summary>
    /// <see cref="Url"/> 的校验值。接口没有轮播图 ID，用它当唯一标识。
    /// </summary>
    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }


    /// <summary>
    /// 运营自己写的备注，不展示
    /// </summary>
    [JsonPropertyName("carouselNotes")]
    public string? CarouselNotes { get; set; }

}
