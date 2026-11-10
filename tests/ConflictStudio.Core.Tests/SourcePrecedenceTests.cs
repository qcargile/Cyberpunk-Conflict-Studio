using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class SourcePrecedenceTests
{
    private static RedScriptSource Script(string path, string body, string provider = "Mod") => new(provider, path, "@replaceMethod(Test)\nfunc Run() -> Int32 { return " + body + "; }");
    private static TweakSource Tweak(string path, int value, string provider = "Mod") => new(provider, path, "Items.Test.value: " + value);

    [TestMethod]
    public void ScriptTreeOrderKeepsDirectorySubtreeBeforeSiblingFile()
    {
        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze([Script("r6/scripts/A/Z.reds", "1", "A"), Script("r6/scripts/A.reds", "2", "B")]).Single();
        Assert.AreEqual(PrecedenceStatus.Predicted, overlap.Precedence!.Status);
        Assert.AreEqual("r6/scripts/A.reds", overlap.Precedence.Winner!.FilePath);
    }

    [TestMethod]
    public void TweakPriorityUsesLeafNameAndKeepsAllOverriddenWrites()
    {
        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([Tweak("r6/tweaks/^folder/normal.yaml", 1, "A"), Tweak("r6/tweaks/z/_early.yaml", 2, "B"), Tweak("r6/tweaks/a/^late.yaml", 3, "C")]).Single();
        Assert.AreEqual(PrecedenceStatus.Predicted, overlap.Precedence!.Status);
        Assert.AreEqual("3", overlap.Precedence.Winner!.Value);
        Assert.HasCount(2, overlap.Precedence.Overridden);
    }

    [TestMethod]
    public void ScriptReplacementOrderRetainsAllLosersAndDeclarationOrder()
    {
        RedScriptSource first = new("A", "r6/scripts/a.reds", "@replaceMethod(Test)\nfunc Run() -> Int32 { return 1; }\n@replaceMethod(Test)\nfunc Run() -> Int32 { return 2; }");
        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze([first, Script("r6/scripts/b.reds", "3", "B")]).Single();
        Assert.AreEqual("r6/scripts/b.reds", overlap.Precedence!.Winner!.FilePath);
        Assert.HasCount(2, overlap.Precedence.Overridden);
        Assert.IsTrue(overlap.Precedence.Overridden[0].LineNumber < overlap.Precedence.Overridden[1].LineNumber);
    }

    [TestMethod]
    public void RegisteredPluginFollowsScriptsButMultiplePluginReplacementsAreUnresolved()
    {
        RedScriptSource script = Script("r6/scripts/z.reds", "1", "A");
        RedScriptSource plugin = Script("red4ext/plugins/Codeware/Scripts/a.reds", "2", "B");
        Assert.AreEqual(plugin.FilePath, RedScriptInteractionAnalyzer.Analyze([script, plugin]).Single().Precedence!.Winner!.FilePath);
        RedScriptSource other = Script("red4ext/plugins/TweakXL/Scripts/b.reds", "3", "C");
        Assert.AreEqual(PrecedenceStatus.Unresolved, RedScriptInteractionAnalyzer.Analyze([script, plugin, other]).Single().Precedence!.Status);
    }

    [TestMethod]
    [DataRow("other/a.reds")]
    [DataRow("r6/scripts/é.reds")]
    [DataRow("red4ext/plugins/Unknown/Scripts/a.reds")]
    [DataRow("r6/scripts/../a.reds")]
    public void UnknownPathsAreUnresolved(string path)
    {
        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze([Script(path, "1", "A"), Script("r6/scripts/b.reds", "2", "B")]).Single();
        Assert.AreEqual(PrecedenceStatus.Unresolved, overlap.Precedence!.Status);
        Assert.IsNull(overlap.Precedence.Winner);
    }

    [TestMethod]
    public void ParseFailureLeavesTweakWinnerUnresolved()
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([Tweak("r6/tweaks/a.yaml", 1, "A"), Tweak("r6/tweaks/b.yaml", 2, "B"), new("C", "r6/tweaks/c.yaml", "Items.Test: [")]);
        Assert.IsNotEmpty(result.Failures);
        Assert.AreEqual(PrecedenceStatus.Unresolved, result.Overlaps.Single().Precedence!.Status);
    }
}
