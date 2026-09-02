using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeCoveragePresentationTests
{
    [TestMethod]
    public void LegacyReceiptDoesNotPresentZeroAsFullCoverage()
    {
        StringAssert.Contains(CodeCoveragePresentation.Summary(null), "not recorded");
        StringAssert.Contains(CodeCoveragePresentation.Details(null), "Rescan");
    }

    [TestMethod]
    public void CoverageShowsStaticSourceAndCallbackCountsWithoutFrameworkLogContent()
    {
        CodeCoverageReceipt coverage = new(1, [new("RedScript", 3), new("CET Lua", 2)], 1, 2, 7, 4, ["Native internals unexamined."]);
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, [], [], [], [], [], [], [], [], [], [], [], [], CodeCoverage: coverage);
        StringAssert.Contains(CodeCoveragePresentation.Summary(receipt), "5");
        string details = CodeCoveragePresentation.Details(receipt);
        StringAssert.Contains(details, "7 literal");
        StringAssert.Contains(details, "4 dynamic");
        StringAssert.Contains(details, "Native internals");
        Assert.IsFalse(details.Contains("Framework observations", StringComparison.Ordinal));
        Assert.IsFalse(details.Contains("scripting.log", StringComparison.Ordinal));
    }
}
