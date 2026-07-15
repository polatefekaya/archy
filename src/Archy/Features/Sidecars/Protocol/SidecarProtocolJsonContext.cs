using System.Text.Json.Serialization;

namespace Archy.Features.Sidecars.Protocol;

[JsonSerializable(typeof(SidecarRequestMessage))]
[JsonSerializable(typeof(SidecarResponseMessage))]
internal sealed partial class SidecarProtocolJsonContext : JsonSerializerContext;
