using ConflictStudio.Core;
using System.IO;
using System.Text.Json;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VortexManagerContextTests
{
    [TestMethod]
    public void ReadAcceptsFreshProfileAndAuthoritativeDeploymentWinners()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-context-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(alpha);
            Directory.CreateDirectory(beta);
            VortexManagerContext expected = Context(game, staging, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "beta" });
            string path = Path.Combine(root, "context.json");
            File.WriteAllText(path, JsonSerializer.Serialize(expected));

            VortexManagerContext actual = VortexManagerContextStore.Read(path);

            Assert.AreEqual("Standard", actual.ProfileName);
            Assert.IsTrue(actual.DeploymentFresh);
            Assert.AreEqual("beta", actual.DeployedWinners["r6\\scripts\\shared.reds"]);
            Assert.AreEqual(2, actual.Providers.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadAcceptsDuplicateArchiveEntriesForRepair()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-context-repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = Context(game, staging, [], new Dictionary<string, string>()) with { ArchiveOrder = ["Alpha.archive", "Alpha.archive"] };
            string path = Path.Combine(root, "context.json");
            File.WriteAllText(path, JsonSerializer.Serialize(context));

            VortexManagerContext actual = VortexManagerContextStore.Read(path);

            Assert.HasCount(2, actual.ArchiveOrder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadRejectsProviderOutsideTheDeclaredStagingRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-escape-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(outside);
            VortexManagerContext context = Context(game, staging, [new("alpha", "Alpha", outside, 0)], new Dictionary<string, string>());
            string path = Path.Combine(root, "context.json");
            File.WriteAllText(path, JsonSerializer.Serialize(context));

            Assert.Throws<InvalidDataException>(() => VortexManagerContextStore.Read(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadRejectsWinnerWhoseProviderIsNotActive()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(alpha);
            VortexManagerContext context = Context(game, staging, [new("alpha", "Alpha", alpha, 0)], new Dictionary<string, string> { ["r6\\scripts\\shared.reds"] = "missing" });
            string path = Path.Combine(root, "context.json");
            File.WriteAllText(path, JsonSerializer.Serialize(context));

            Assert.Throws<InvalidDataException>(() => VortexManagerContextStore.Read(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadRejectsAmbiguousDuplicateProviderNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(alpha);
            Directory.CreateDirectory(beta);
            VortexManagerContext context = Context(game, staging, [new("alpha", "Same name", alpha, 0), new("beta", "Same name", beta, 1)], new Dictionary<string, string>());
            string path = Path.Combine(root, "context.json");
            File.WriteAllText(path, JsonSerializer.Serialize(context));

            Assert.Throws<InvalidDataException>(() => VortexManagerContextStore.Read(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2GuardRejectsAStillDeployedVortexProfileInTheSameGameRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-mo2-overlap-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string provider = Path.Combine(staging, "Alpha");
            string deployed = Path.Combine(game, "r6", "scripts", "shared.reds");
            Directory.CreateDirectory(provider);
            Directory.CreateDirectory(Path.GetDirectoryName(deployed)!);
            File.WriteAllText(deployed, "deployed");
            VortexManagerContext context = Context(game, staging, [new("alpha", "Alpha", provider, 0)], new Dictionary<string, string> { ["r6\\scripts\\shared.reds"] = "alpha", ["r6\\scripts\\absent.reds"] = "alpha" }) with { ProfileName = "Vortex Default" };
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, JsonSerializer.Serialize(context));

            CrossManagerDeploymentException exception = Assert.ThrowsExactly<CrossManagerDeploymentException>(() => VortexDeploymentGuard.RequireNoDeployment(game, contextPath));

            StringAssert.Contains(exception.Message, "Vortex Default");
            StringAssert.Contains(exception.Message, "1 deployed file");
            StringAssert.Contains(exception.Message, "Purge");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2GuardAllowsAContextForAnotherGameOrACompletedPurge()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-mo2-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string otherGame = Path.Combine(root, "other-game");
            string staging = Path.Combine(root, "staging");
            string provider = Path.Combine(staging, "Alpha");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(otherGame);
            Directory.CreateDirectory(provider);
            VortexManagerContext context = Context(otherGame, staging, [new("alpha", "Alpha", provider, 0)], new Dictionary<string, string> { ["r6\\scripts\\absent.reds"] = "alpha" });
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, JsonSerializer.Serialize(context));

            VortexDeploymentGuard.RequireNoDeployment(game, contextPath);
            VortexDeploymentGuard.RequireNoDeployment(otherGame, contextPath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static VortexManagerContext Context(string game, string staging, VortexProviderContext[] providers, IReadOnlyDictionary<string, string> winners)
        => new(1, new string('a', 64), new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), "profile-1", "Standard", game, staging, true, providers, new Dictionary<string, string>(winners, StringComparer.OrdinalIgnoreCase), ["Alpha.archive", "Beta.archive"], null);
}
