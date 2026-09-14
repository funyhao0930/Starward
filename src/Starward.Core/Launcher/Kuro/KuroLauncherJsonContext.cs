using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;


[JsonSerializable(typeof(KuroLauncherBackground))]
[JsonSerializable(typeof(KuroLauncherGameIndex))]

internal partial class KuroLauncherJsonContext : JsonSerializerContext
{

}
