using System.Text.Json;
using System.Text.Json.Nodes;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class CodeCoverageReceiptTests
{
    [TestMethod]
    public void UnreadableInputsDoNotCountKnownCoverageLimitationsAsReadFailures()
    {
        SourceAnalysisFailure[] failures = [
            new("Alpha", "blocked.reds", "RedScript", "Access denied"),
            new("Alpha", "blocked.reds", "RedScript", "Access denied"),
            new("Alpha", "r6/scripts", "Deployment evidence", "Access denied"),
            new("Alpha", "conditional.reds", "RedScript condition", "Unknown condition"),
            new("Alpha", "unsupported.tweak", "TweakXL RED", "Unsupported format")];
        CodeCoverageReceipt coverage = CodeCoverageReceipt.Build(new([], [], [], failures), [], failures, 0);
        Assert.AreEqual(2, coverage.UnreadableInputs);
        Assert.AreEqual(1, coverage.UnsupportedTweakFiles);
        Assert.IsTrue(coverage.Sources.All(value => value.AnalyzedFiles == 0));
    }

    [TestMethod]
    public void SupportRetainsBasePropertyEvidenceWithoutCreatingCases()
    {
        using CoverageFixture fixture = new();
        fixture.Write("r6/tweaks/base.yaml", "Items.Base.value: 1");
        fixture.Write("r6/tweaks/derived.yaml", "Items.Derived:\n  $base: Items.Base\n  value: 2");
        ProfileScanReceipt receipt = fixture.Scan();
        ModSourceInventory inventory = new([], [], [new("Alpha", "base.yaml", "Items.Base.value: 1"), new("Beta", "derived.yaml", "Items.Derived:\n  $base: Items.Base\n  value: 2")], []);
        TweakAnalysisResult analysis = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        receipt = receipt with { InteractionFindings = InteractionReportBuilder.Build(inventory), TweakOverlaps = analysis.Overlaps };
        SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, []);
        Assert.IsFalse(capsule.WorkQueue.Any(value => value.Surface == ConflictSurface.ScriptAndTweak));
        SupportCapsuleWriter.Write(Path.Combine(fixture.Root, "support"), capsule);
        string html = File.ReadAllText(Path.Combine(fixture.Root, "support", "conflict-casefile.html"));
        Assert.IsTrue(capsule.Evidence.TweakOverlaps.Any(value => value.Kind == TweakOverlapKind.BaseRecordDependency));
        StringAssert.Contains(html, "Items.Base.value");
        StringAssert.Contains(html, "Items.Derived.value");
    }

    [TestMethod]
    public void CoverageCountsEffectiveSourcesAndSeparatesDynamicCallbacksFromUnsupportedFiles()
    {
        using CoverageFixture fixture = new();
        fixture.Write("r6/scripts/example.reds", "public class Example {}");
        fixture.Write("r6/tweaks/example.tweak", "not YAML");
        fixture.Write("bin/x64/plugins/cyber_engine_tweaks/mods/Test/init.lua", "registerForEvent('onInit', function() end)\nregisterForEvent(eventName, function() end)");
        ProfileScanReceipt receipt = fixture.Scan();
        CodeCoverageReceipt coverage = receipt.CodeCoverage!;
        Assert.IsNotNull(coverage);
        Assert.AreEqual(1, coverage.Sources.Single(value => value.Surface == "RedScript").AnalyzedFiles);
        Assert.AreEqual(1, coverage.Sources.Single(value => value.Surface == "CET Lua").AnalyzedFiles);
        Assert.AreEqual(1, coverage.UnsupportedTweakFiles);
        Assert.AreEqual(1, coverage.LiteralCallbacks);
        Assert.AreEqual(1, coverage.DynamicCallbacks);
        StringAssert.Contains(string.Join(" ", coverage.Limitations), "annotation-only");
        StringAssert.Contains(string.Join(" ", coverage.Limitations), "reachability");
        StringAssert.Contains(string.Join(" ", coverage.Limitations), "Native");
    }

    [TestMethod]
    public void FrameworkLogContentsAreNotCapturedOrAppliedToSourceResults()
    {
        using CoverageFixture fixture = new();
        fixture.Write("r6/scripts/first.reds", "@replaceMethod(PlayerPuppet)\npublic func Test() -> Void {}");
        fixture.Write("r6/scripts/second.reds", "@replaceMethod(PlayerPuppet)\npublic func Test() -> Void {}");
        ProfileScanReceipt absent = fixture.Scan();
        fixture.Write("r6/logs/redscript_rCURRENT.log", "[INFO - Tue, 1 Sep 2026 18:27:49 -0600] Compiling files in D:\\Game\\r6\\scripts:\n[INFO - Tue, 1 Sep 2026 18:27:51 -0600] Compilation complete");
        ProfileScanReceipt present = fixture.Scan();
        Assert.IsNotEmpty(absent.InteractionFindings);
        Assert.AreEqual(JsonSerializer.Serialize(absent.InteractionFindings), JsonSerializer.Serialize(present.InteractionFindings));
        Assert.AreEqual(1, present.Metrics!.CodeCacheHits);
        Assert.IsFalse(JsonSerializer.Serialize(present).Contains("frameworkArtifacts", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ReleasedLegacyReceiptLoadsWithoutCoverageAndSupportExportsStaticCoverageOnly()
    {
        using CoverageFixture fixture = new();
        ProfileScanReceipt receipt = fixture.Scan();
        string receiptPath = Path.Combine(fixture.Root, "receipt.json");
        ProfileScanReceiptStore.Write(receiptPath, receipt);
        ProfileScanReceipt restored = ProfileScanReceiptStore.Read(receiptPath);
        Assert.AreEqual(JsonSerializer.Serialize(receipt.CodeCoverage), JsonSerializer.Serialize(restored.CodeCoverage));
        JsonObject legacy = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        legacy.Remove("codeCoverage");
        File.WriteAllText(receiptPath, legacy.ToJsonString());
        ProfileScanReceipt old = ProfileScanReceiptStore.Read(receiptPath);
        Assert.IsNull(old.CodeCoverage);
        SupportCapsuleWriter.Write(Path.Combine(fixture.Root, "old"), SupportCapsuleBuilder.Build(old, []));
        SupportCapsuleWriter.Write(Path.Combine(fixture.Root, "support"), SupportCapsuleBuilder.Build(restored, []));
        SupportCapsule support = SupportCapsuleWriter.Read(Path.Combine(fixture.Root, "support", "conflict-casefile.json"));
        Assert.AreEqual(JsonSerializer.Serialize(restored.CodeCoverage), JsonSerializer.Serialize(support.Evidence.CodeCoverage));
        string html = File.ReadAllText(Path.Combine(fixture.Root, "support", "conflict-casefile.html"));
        StringAssert.Contains(html, "Code coverage");
        StringAssert.Contains(html, "annotation-only");
        Assert.IsFalse(JsonSerializer.Serialize(support).Contains("frameworkArtifacts", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("Framework observations", StringComparison.Ordinal));
    }
}

internal sealed class CoverageFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "conflict-studio-coverage-" + Guid.NewGuid().ToString("N"));

    public CoverageFixture() => Write("bin/x64/Cyberpunk2077.exe", string.Empty);

    public void Write(string relative, string text)
    {
        string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public ProfileScanReceipt Scan() => ProfileScanCoordinator.ScanManual(Root, DateTimeOffset.UtcNow, null, CancellationToken.None);

    public void Dispose() => Directory.Delete(Root, true);
}
