using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.Features.Analysis.AnalyzeLanguageServerSemantics;

public sealed record ConfiguredLspSemanticAnalysisRequest(
    SemanticAnalysisRequest SemanticRequest,
    LanguageServerProfileConfiguration Profile,
    string ConfigurationFingerprint);
