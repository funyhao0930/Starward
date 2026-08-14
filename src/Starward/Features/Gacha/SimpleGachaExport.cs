using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Starward.Features.Gacha;

/// <summary>
/// 鸣潮与终末地的导出文件。
/// <para/>
/// UIGF 是米哈游生态的标准，这两款游戏没有对应格式，
/// 所以只是把库里的记录原样写出来，够用来备份与自行处理。
/// <para/>
/// 没有用源生成器序列化：记录类型上的转换器（<c>GachaItemIdJsonConverter</c> 等）
/// 生成器认不出来，会换来更多告警。与既有的四个导出实现一样用反射序列化。
/// </summary>
internal class SimpleGachaExportFile<T>
{

    [JsonPropertyName("uid")]
    public long Uid { get; set; }

    [JsonPropertyName("list")]
    public List<T> List { get; set; } = [];

}
