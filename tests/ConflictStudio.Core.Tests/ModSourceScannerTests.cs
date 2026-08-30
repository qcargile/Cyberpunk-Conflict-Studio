using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ModSourceScannerTests
{
    [TestMethod]
    public void ScanCollectsRedScriptLuaAndTweakSourcesWithProviderProvenance()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Alpha", "r6\\scripts\\alpha.reds", "@wrapMethod(DamageSystem)");
            Write(root, "Beta", "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\beta\\init.lua", "Observe('PlayerPuppet', 'OnAction', function() end)");
            Write(root, "Gamma", "r6\\tweaks\\gamma.yaml", "Items.Base_HMG:\n  value: 1.0\n");

            ModSourceInventory inventory = ModSourceScanner.Scan(root, ["Alpha", "Beta", "Gamma"]);

            Assert.AreEqual("Alpha", inventory.RedScripts.Single().Provider);
            Assert.AreEqual("Beta", inventory.LuaSources.Single().Provider);
            Assert.AreEqual("Gamma", inventory.TweakSources.Single().Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanIgnoresMatchingExtensionsOutsideRuntimeSourceLanes()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Write(root, "Alpha", "docs\\example.reds", "@replaceMethod(DamageSystem)");
            Write(root, "Alpha", "tools\\settings.lua", "Override('DamageSystem', 'ProcessHit', function() end)");
            Write(root, "Alpha", "bin\\x64\\plugins\\audioware\\config.yaml", "Items.Base_HMG:\n  value: 1.0\n");

            ModSourceInventory inventory = ModSourceScanner.Scan(root, ["Alpha"]);

            Assert.AreEqual(0, inventory.RedScripts.Length);
            Assert.AreEqual(0, inventory.LuaSources.Length);
            Assert.AreEqual(0, inventory.TweakSources.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersUsesTheEffectiveManualModAndOverwriteWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-source-topology-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "game");
        string mod = Path.Combine(root, "mod");
        string overwrite = Path.Combine(root, "overwrite");
        try
        {
            WriteRoot(game, "r6\\tweaks\\shared.yaml", "Items.Test:\n  value: 1\n");
            WriteRoot(mod, "r6\\tweaks\\shared.yaml", "Items.Test:\n  value: 2\n");
            WriteRoot(overwrite, "r6\\tweaks\\shared.yaml", "Items.Test:\n  value: 3\n");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Overwrite", overwrite), new DeploymentProvider("Alpha", mod), new DeploymentProvider("Game directory", game)]);

            Assert.AreEqual("Overwrite", inventory.TweakSources.Single().Provider);
            Assert.IsTrue(inventory.TweakSources.Single().Text.Contains("value: 3", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersUsesTheAuthoritativeVortexDeploymentWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-source-" + Guid.NewGuid().ToString("N"));
        string alpha = Path.Combine(root, "Alpha");
        string beta = Path.Combine(root, "Beta");
        try
        {
            WriteRoot(alpha, "r6\\scripts\\shared.reds", "alpha");
            WriteRoot(beta, "r6\\scripts\\shared.reds", "beta");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "beta" };

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Alpha", alpha, null, "alpha"), new DeploymentProvider("Beta", beta, null, "beta")], winners);

            Assert.AreEqual("Beta", inventory.RedScripts.Single().Provider);
            Assert.AreEqual("beta", inventory.RedScripts.Single().Text);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Write(string root, string provider, string relativePath, string text)
    {
        string path = Path.Combine(root, provider, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteRoot(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
