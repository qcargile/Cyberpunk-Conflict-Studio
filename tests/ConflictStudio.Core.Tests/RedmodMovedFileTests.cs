using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RedmodMovedFileTests
{
    [TestMethod]
    public void ScanReadsMovedRedmodDescriptorArchiveAndOrderFromTheirDeployedPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-moved-redmod-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string providerRoot = Path.Combine(staging, "Provider");
            string info = "mods\\Alpha\\info.json";
            string archive = "mods\\Alpha\\archives\\Alpha.archive";
            string order = "r6\\cache\\modded\\MO_REDmod_load_order.txt";
            Move(providerRoot, game, info, "{\"name\":\"Alpha\",\"version\":\"1.0.0\"}");
            Move(providerRoot, game, archive, "REDmod archive contents");
            Move(providerRoot, game, order, "Alpha\n");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { [info] = "provider", [archive] = "provider", [order] = "provider" };
            VortexManagerContext context = Context(game, staging, providerRoot, winners);
            VortexMovedFiles movedFiles = VortexMovedFiles.Capture(context);

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", providerRoot, null, "provider")], winners, null, movedFiles);

            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, profile.OrderEvidence.Kind);
            Assert.AreEqual(Path.Combine(game, order), profile.OrderEvidence.SourcePath);
            Assert.HasCount(1, profile.Archives);
            Assert.AreEqual("Provider", profile.Archives[0].Provider);
            Assert.AreEqual("REDmod/Alpha/Alpha.archive", profile.Archives[0].ArchiveName);
            Assert.AreEqual(Path.Combine(game, archive), profile.Archives[0].PhysicalPath);
            Assert.IsEmpty(profile.Failures);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanKeepsMissingOrMismatchedMovedInputsUnresolved()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-moved-redmod-rejected-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string providerRoot = Path.Combine(staging, "Provider");
            string info = "mods\\Alpha\\info.json";
            string archive = "mods\\Alpha\\archives\\Alpha.archive";
            Move(providerRoot, game, info, "{\"name\":\"Alpha\",\"version\":\"1.0.0\"}");
            Write(game, archive, "REDmod archive contents");
            Write(providerRoot, archive + ".vortex_lnk", JsonSerializer.Serialize(new { target = Path.Combine(game, "mods", "Other", "archives", "Alpha.archive") }));
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { [info] = "provider", [archive] = "provider" };
            VortexManagerContext context = Context(game, staging, providerRoot, winners);
            VortexMovedFiles movedFiles = VortexMovedFiles.Capture(context);

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", providerRoot, null, "provider")], winners, null, movedFiles);

            Assert.IsEmpty(profile.Archives);
            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence.Kind);
            Assert.IsTrue(profile.Failures.Any(value => value.Surface == "REDmod archive winner" && value.FilePath.EndsWith("Alpha.archive", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsADeployedDescriptorWithoutItsStagingMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-moved-redmod-no-marker-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string providerRoot = Path.Combine(staging, "Provider");
            string info = "mods\\Alpha\\info.json";
            Write(game, info, "{\"name\":\"Alpha\",\"version\":\"1.0.0\"}");
            Directory.CreateDirectory(providerRoot);
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { [info] = "provider" };
            VortexManagerContext context = Context(game, staging, providerRoot, winners);
            VortexMovedFiles movedFiles = VortexMovedFiles.Capture(context);

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", providerRoot, null, "provider")], winners, null, movedFiles);

            Assert.IsEmpty(profile.Archives);
            Assert.IsTrue(profile.Failures.Any(value => value.Surface == "REDmod descriptor winner"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static VortexManagerContext Context(string game, string staging, string providerRoot, Dictionary<string, string> winners)
    {
        VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Default", game, staging, true, [new VortexProviderContext("provider", "Provider", providerRoot, 0)], winners, [], null, DeploymentInventoryComplete: true, DeploymentFileCount: winners.Count, RelevantDeploymentFileCount: winners.Count);
        return context with { ContextId = VortexManagerContextStore.ComputeContextId(context) };
    }

    private static void Move(string providerRoot, string game, string relative, string content)
    {
        Write(game, relative, content);
        Write(providerRoot, relative + ".vortex_lnk", JsonSerializer.Serialize(new { target = Path.Combine(game, relative) }));
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
