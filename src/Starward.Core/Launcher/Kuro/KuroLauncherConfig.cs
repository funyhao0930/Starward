using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器自身的配置（<c>launcher/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}/index.json</c>）。
/// <para/>
/// 只取用得上的字段：背景图路径里那段令牌就放在这里，官方会随版本轮换。
/// </summary>
public class KuroLauncherConfig
{

    [JsonPropertyName("functionCode")]
    public KuroLauncherFunctionCode? FunctionCode { get; set; }

}


public class KuroLauncherFunctionCode
{

    /// <summary>
    /// 背景图路径 <c>background/{令牌}/{语言}.json</c> 中的令牌，
    /// 官方前端里叫 <c>backgroundRandomPath</c>
    /// </summary>
    [JsonPropertyName("background")]
    public string? Background { get; set; }

}
