using Archy.Features.Placement.AdviseSplitOrAppend;
namespace Archy.UnitTests.Features.Placement.AdviseSplitOrAppend;
public sealed class SplitAppendAdvisorTests { [Fact] public void AdviseReturnsOppositeAdviceForCohesiveAndFragmentedModules(){var a=new SplitAppendAdvisor(SplitAppendThresholds.Default);var append=a.Advise(new("core",8,.8,2,3,null));var split=a.Advise(new("core",30,.2,2,14,"InvoiceHandler.cs"));Assert.Equal(SplitAppendAdviceKind.Append,append.Kind);Assert.Equal(SplitAppendAdviceKind.CreateNewFile,split.Kind);} }
