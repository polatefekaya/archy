using System.Text.Json.Serialization;

namespace Archy.Features.Workspaces.InitializeWorkspace;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceManifest))]
internal sealed partial class WorkspaceManifestJsonContext : JsonSerializerContext;
