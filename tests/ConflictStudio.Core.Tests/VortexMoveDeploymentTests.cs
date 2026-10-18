using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VortexMoveDeploymentTests
{
    [TestMethod]
    public void MovedWinnersKeepTheirOwnersAndStagedCompetitors()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-move-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            string script = "r6\\scripts\\shared.reds";
            string archive = "archive\\pc\\mod\\Beta.archive";
            string xl = "archive\\pc\\mod\\Beta.archive.xl";
            string lua = "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua";
            string yaml = "r6\\tweaks\\Beta.yaml";
            string index = "bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark";
            Write(alpha, script, "@replaceMethod(PlayerPuppet)\npublic func Example() -> Void {}");
            Move(beta, game, script, "@wrapMethod(PlayerPuppet)\npublic func Example() -> Void { wrappedMethod(); }");
            Move(beta, game, archive, "archive fixture");
            WriteArchive(Path.Combine(game, archive), 'b');
            Move(alpha, game, "archive\\pc\\mod\\Alpha.archive", "archive fixture");
            WriteArchive(Path.Combine(game, "archive\\pc\\mod\\Alpha.archive"), 'a');
            Move(beta, game, xl, "localization:\n  onscreens:\n    en-us: example.json");
            Move(beta, game, lua, "TweakDB:SetFlat('Items.Test.value', 2)");
            Move(beta, game, yaml, "Items.Test.value: 1");
            Move(beta, game, index, "index fixture");
            Write(game, "r6\\scripts\\manual.reds", "@wrapMethod(PlayerPuppet)\npublic func Manual() -> Void { wrappedMethod(); }");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { [script] = "beta", [archive] = "beta", ["archive\\pc\\mod\\Alpha.archive"] = "alpha", [xl] = "beta", [lua] = "beta", [yaml] = "beta", [index] = "beta" };
            string contextPath = Context(root, game, staging, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], winners);

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.AreEqual("Beta", receipt.ArchiveInventory!.Single(value => value.ArchiveName == "Beta.archive").Provider);
            Assert.AreEqual(Path.Combine(game, archive), receipt.ArchiveInventory!.Single(value => value.ArchiveName == "Beta.archive").PhysicalPath);
            Assert.HasCount(1, receipt.ResourceConflicts);
            Assert.IsEmpty(receipt.ArchiveFailures);
            Assert.AreEqual("Beta", receipt.ResourcePathIndexEvidence!.Provider);
            Assert.AreEqual(Path.Combine(game, index), receipt.ResourcePathIndexEvidence.SourcePath);
            VirtualFileShadow shadow = receipt.VirtualFileShadows.Single(value => value.RelativePath == script);
            Assert.AreEqual("Beta", shadow.WinnerProvider);
            Assert.HasCount(2, shadow.Providers);
            Assert.IsTrue(receipt.RedScriptFlows.Any(value => value.Provider == "Beta"));
            Assert.IsTrue(receipt.RedScriptFlows.Any(value => value.Provider == "Game directory"));
            Assert.IsFalse(receipt.SourceFailures!.Any(value => value.Message.Contains("absent", StringComparison.Ordinal) || value.Message.Contains("missing", StringComparison.Ordinal)));
            Assert.IsEmpty(receipt.ArchiveXlFailures);
            Assert.AreEqual(1, receipt.CodeCoverage!.Sources.Single(value => value.Surface == "ArchiveXL").AnalyzedFiles);
            Assert.AreEqual(1, receipt.CodeCoverage.Sources.Single(value => value.Surface == "CET Lua").AnalyzedFiles);
            Assert.AreEqual(1, receipt.CodeCoverage.Sources.Single(value => value.Surface == "TweakXL YAML").AnalyzedFiles);
            ProfileScanReceipt cached = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);
            Assert.AreEqual(1, cached.Metrics!.CodeCacheHits);
            Assert.AreEqual("Beta", cached.VirtualFileShadows.Single(value => value.RelativePath == script).WinnerProvider);
            Mo2ArchiveProfile refreshed = VortexArchiveProfileScanner.Scan(VortexManagerContextStore.Read(contextPath));
            Assert.HasCount(2, refreshed.Archives);
            Assert.IsEmpty(refreshed.Failures);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("malformed")]
    [DataRow("wrong-target")]
    [DataRow("missing-target")]
    public void UnverifiedMarkersDoNotPromoteAStagedLoser(string failure)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-move-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            string script = "r6\\scripts\\shared.reds";
            Write(alpha, script, "@replaceMethod(PlayerPuppet)\npublic func Example() -> Void {}");
            Move(beta, game, script, "@wrapMethod(PlayerPuppet)\npublic func Example() -> Void { wrappedMethod(); }");
            string marker = Path.Combine(beta, script + ".vortex_lnk");
            if (failure == "missing") File.Delete(marker);
            if (failure == "malformed") File.WriteAllText(marker, "{");
            if (failure == "wrong-target") File.WriteAllText(marker, JsonSerializer.Serialize(new { target = Path.Combine(alpha, script) }));
            if (failure == "missing-target") File.Delete(Path.Combine(game, script));
            string contextPath = Context(root, game, staging, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], new(StringComparer.OrdinalIgnoreCase) { [script] = "beta" });

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.IsEmpty(receipt.RedScriptFlows);
            Assert.IsEmpty(receipt.VirtualFileShadows);
            Assert.IsTrue(receipt.SourceFailures!.Any(value => value.Provider == "Beta" && value.Surface == "RedScript"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void MarkerChangeDuringScanRejectsTheResult()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-move-change-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string beta = Path.Combine(staging, "Beta");
            string script = "r6\\scripts\\shared.reds";
            Move(beta, game, script, "@wrapMethod(PlayerPuppet)\npublic func Example() -> Void { wrappedMethod(); }");
            string contextPath = Context(root, game, staging, [new("beta", "Beta", beta, 0)], new(StringComparer.OrdinalIgnoreCase) { [script] = "beta" });

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, () => File.WriteAllText(Path.Combine(beta, script + ".vortex_lnk"), "{}"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("Root.xl", false, 1)]
    [DataRow("custom\\Nested.xl", false, 1)]
    [DataRow("Root.xl", true, 0)]
    [DataRow("custom\\Nested.xl", true, 0)]
    public void MovedArchiveXlPreservesProviderRootFallback(string relative, bool hasArchiveDirectory, int expectedSources)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-move-root-xl-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string beta = Path.Combine(staging, "Beta");
            Move(beta, game, relative, "localization:\n  onscreens:\n    en-us: example.json");
            if (hasArchiveDirectory) Directory.CreateDirectory(Path.Combine(beta, "archive", "pc", "mod"));
            string contextPath = Context(root, game, staging, [new("beta", "Beta", beta, 0)], new(StringComparer.OrdinalIgnoreCase) { [relative] = "beta" });

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.AreEqual(expectedSources, receipt.CodeCoverage!.Sources.Single(value => value.Surface == "ArchiveXL").AnalyzedFiles);
            Assert.IsEmpty(receipt.ArchiveXlFailures);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Context(string root, string game, string staging, VortexProviderContext[] providers, Dictionary<string, string> winners)
    {
        VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Default", game, staging, true, providers, winners, [], null, DeploymentInventoryComplete: true, DeploymentFileCount: winners.Count, RelevantDeploymentFileCount: winners.Count);
        context = context with { ContextId = VortexManagerContextStore.ComputeContextId(context) };
        string path = Path.Combine(root, "context.json");
        File.WriteAllText(path, JsonSerializer.Serialize(context));
        return path;
    }

    private static void Move(string provider, string game, string relative, string content)
    {
        Write(game, relative, content);
        Write(provider, relative + ".vortex_lnk", JsonSerializer.Serialize(new { target = Path.Combine(game, relative) }));
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteArchive(string path, char payload)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, System.Text.Encoding.ASCII, true);
        writer.Write("RDAR"u8);
        writer.Write(12U);
        writer.Write(44UL);
        writer.Write(84U);
        writer.Write(0UL);
        writer.Write(0U);
        writer.Write(128UL);
        writer.Write(0U);
        writer.Write(8U);
        writer.Write(56U);
        writer.Write(0UL);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(42UL);
        writer.Write(0L);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(Enumerable.Repeat((byte)payload, 20).ToArray());
    }
}
