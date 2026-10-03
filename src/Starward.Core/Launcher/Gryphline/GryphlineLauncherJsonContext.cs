using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;


[JsonSerializable(typeof(GryphlineBatchProxyRequest))]
[JsonSerializable(typeof(GryphlineBatchProxyResponse))]
[JsonSerializable(typeof(GryphlineBulletinResponse))]

internal partial class GryphlineLauncherJsonContext : JsonSerializerContext
{

}
