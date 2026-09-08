using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;


[JsonSerializable(typeof(GryphlineBatchProxyRequest))]
[JsonSerializable(typeof(GryphlineBatchProxyResponse))]

internal partial class GryphlineLauncherJsonContext : JsonSerializerContext
{

}
