using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;


[JsonSerializable(typeof(GryphlineBatchProxyRequest))]
[JsonSerializable(typeof(GryphlineBatchProxyResponse))]
[JsonSerializable(typeof(GryphlineBulletinResponse))]
[JsonSerializable(typeof(GryphlinePatchInfo))]
[JsonSerializable(typeof(GryphlineVerifyFiles))]
[JsonSerializable(typeof(GryphlinePredownloadMarker))]
[JsonSerializable(typeof(string[]))]

internal partial class GryphlineLauncherJsonContext : JsonSerializerContext
{

}
