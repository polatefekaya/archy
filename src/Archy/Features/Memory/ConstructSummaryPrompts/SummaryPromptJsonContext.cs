using System.Text.Json.Serialization;

namespace Archy.Features.Memory.ConstructSummaryPrompts;

[JsonSerializable(typeof(SummaryPromptBuilder.PromptData))]
internal sealed partial class SummaryPromptJsonContext : JsonSerializerContext;
