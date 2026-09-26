using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;


[JsonSerializable(typeof(List<KuroSdkAccount>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(KuroPlayerResponse))]
[JsonSerializable(typeof(KuroPlayerSummary))]
[JsonSerializable(typeof(KuroRoleData))]
// 缓存文件里的 id 是 507752627.0 这种浮点写法，没有建模；其余数字都是整数
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
internal partial class KuroPlayerJsonContext : JsonSerializerContext
{

}
