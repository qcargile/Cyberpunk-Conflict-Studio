using ConflictStudio.App;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeSourceSearchTests
{
    [TestMethod]
    public void FindsLiteralTextWithExactLineAndColumn()
    {
        CodeSourceSearchResult result = CodeSourceSearch.Find(["unrelated", "Armor + armor", "[value]"], "armor");
        Assert.AreEqual(2, result.Matches.Length);
        Assert.AreEqual(new CodeSourceMatch(2, 1, 5), result.Matches[0]);
        Assert.AreEqual(new CodeSourceMatch(2, 9, 5), result.Matches[1]);
        Assert.AreEqual(new CodeSourceMatch(3, 1, 7), CodeSourceSearch.Find(["unrelated", "Armor + armor", "[value]"], "[value]").Matches.Single());
    }

    [TestMethod]
    public void LimitsResultsAndRetainsMatchesBeyondPreviewWidth()
    {
        CodeSourceSearchResult limited = CodeSourceSearch.Find(Enumerable.Repeat("match", 501).ToArray(), "match");
        Assert.AreEqual(500, limited.Matches.Length);
        Assert.IsTrue(limited.IsLimited);
        Assert.AreEqual(2001, CodeSourceSearch.Find([new string('x', 2000) + "match"], "match").Matches.Single().Column);
        Assert.IsEmpty(CodeSourceSearch.Find(["anything"], string.Empty).Matches);
    }

    [TestMethod]
    public void HonorsCancellationAndRejectsOversizedQueries()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => CodeSourceSearch.Find(["line"], "line", cancellation.Token));
        Assert.ThrowsExactly<ArgumentException>(() => CodeSourceSearch.Find(["line"], new string('x', 513)));
    }
}
