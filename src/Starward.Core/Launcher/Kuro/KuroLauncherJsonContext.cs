using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;


[JsonSerializable(typeof(KuroLauncherBackground))]
[JsonSerializable(typeof(KuroLauncherGameIndex))]
[JsonSerializable(typeof(KuroLauncherInformation))]

internal partial class KuroLauncherJsonContext : JsonSerializerContext
{

}
