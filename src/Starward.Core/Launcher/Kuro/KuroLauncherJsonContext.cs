using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;


[JsonSerializable(typeof(KuroLauncherBackground))]
[JsonSerializable(typeof(KuroLauncherConfig))]
[JsonSerializable(typeof(KuroLauncherGameIndex))]
[JsonSerializable(typeof(KuroOfficialGameIndex))]
[JsonSerializable(typeof(KuroOfficialPredownload))]
[JsonSerializable(typeof(KuroLauncherInformation))]
[JsonSerializable(typeof(KuroResourceIndex))]
[JsonSerializable(typeof(KuroLauncherDownloadConfig))]
[JsonSerializable(typeof(KuroPredownloadMarker))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<KuroDirectoryIntegrityCheck>))]

internal partial class KuroLauncherJsonContext : JsonSerializerContext
{

}
