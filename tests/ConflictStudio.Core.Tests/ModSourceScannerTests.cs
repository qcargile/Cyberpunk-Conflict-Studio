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

    [TestMethod]
    public void ScanProvidersUsesTheAuthoritativeVortexRedTweakWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-red-tweak-source-" + Guid.NewGuid().ToString("N"));
        string alpha = Path.Combine(root, "Alpha");
        string beta = Path.Combine(root, "Beta");
        try
        {
            WriteRoot(alpha, "r6\\tweaks\\shared.tweak", "alpha");
            WriteRoot(beta, "r6\\tweaks\\shared.tweak", "beta");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\tweaks\\shared.tweak"] = "beta" };

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Alpha", alpha, null, "alpha"), new DeploymentProvider("Beta", beta, null, "beta")], winners);

            Assert.AreEqual(0, inventory.TweakSources.Length);
            SourceAnalysisFailure failure = inventory.Failures.Single(value => value.Surface == "TweakXL RED");
            Assert.AreEqual("Beta", failure.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersExcludesAnUnboundWinnerWithoutFallingBackToALoser()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-excluded-source-" + Guid.NewGuid().ToString("N"));
        string alpha = Path.Combine(root, "Alpha");
        string beta = Path.Combine(root, "Beta");
        try
        {
            WriteRoot(alpha, "r6\\scripts\\shared.reds", "alpha");
            WriteRoot(beta, "r6\\scripts\\shared.reds", "beta");
            WriteRoot(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\other\\init.lua", "other");
            string excluded = Path.Combine(alpha, "r6", "scripts");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Alpha", alpha), new DeploymentProvider("Beta", beta)], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excluded });

            Assert.AreEqual("other", inventory.LuaSources.Single().Text);
            Assert.IsFalse(inventory.RedScripts.Any(value => value.FilePath == "r6\\scripts\\shared.reds"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersReservesTheIdentityOfADisappearedHighPrioritySource()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-missing-source-" + Guid.NewGuid().ToString("N"));
        string high = Path.Combine(root, "High");
        string middle = Path.Combine(root, "Middle");
        string low = Path.Combine(root, "Low");
        try
        {
            WriteRoot(middle, "r6\\scripts\\shared.reds", "middle");
            WriteRoot(low, "r6\\scripts\\shared.reds", "low");
            WriteRoot(low, "r6\\scripts\\other.reds", "other");
            string disappeared = Path.Combine(high, "r6", "scripts", "shared.reds");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("High", high), new DeploymentProvider("Middle", middle), new DeploymentProvider("Low", low)], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { disappeared });

            Assert.AreEqual("other", inventory.RedScripts.Single().Text);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersDoesNotPromoteTwoLosersWhenTheDeployedWinnerIsLocked()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-locked-source-winner-" + Guid.NewGuid().ToString("N"));
        string high = Path.Combine(root, "High");
        string middle = Path.Combine(root, "Middle");
        string low = Path.Combine(root, "Low");
        try
        {
            WriteRoot(high, "r6\\scripts\\shared.reds", "high");
            WriteRoot(middle, "r6\\scripts\\shared.reds", "middle");
            WriteRoot(low, "r6\\scripts\\shared.reds", "low");
            WriteRoot(low, "r6\\scripts\\other.reds", "other");
            string lockedPath = Path.Combine(high, "r6", "scripts", "shared.reds");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "high", ["r6\\scripts\\other.reds"] = "low" };
            using FileStream locked = File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("High", high, null, "high"), new DeploymentProvider("Middle", middle, null, "middle"), new DeploymentProvider("Low", low, null, "low")], winners);

            Assert.AreEqual("other", inventory.RedScripts.Single().Text);
            Assert.AreEqual("High", inventory.Failures.Single().Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersReportsOnlyTheEffectiveRedTweakWithoutTreatingItAsYaml()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-red-tweak-source-" + Guid.NewGuid().ToString("N"));
        string high = Path.Combine(root, "High");
        string low = Path.Combine(root, "Low");
        try
        {
            WriteRoot(high, "r6\\tweaks\\shared.tweak", "RED4ext.TweakDB:SetFlat('Items.Test.Value', 2)");
            WriteRoot(low, "r6\\tweaks\\shared.tweak", "RED4ext.TweakDB:SetFlat('Items.Test.Value', 1)");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("High", high), new DeploymentProvider("Low", low)]);

            Assert.AreEqual(0, inventory.TweakSources.Length);
            SourceAnalysisFailure failure = inventory.Failures.Single(value => value.Surface == "TweakXL RED");
            Assert.AreEqual("High", failure.Provider);
            Assert.AreEqual("r6\\tweaks\\shared.tweak", failure.FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersRequiresAnEffectiveInitLuaForEachCetModRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-cet-activation-" + Guid.NewGuid().ToString("N"));
        string provider = Path.Combine(root, "Provider");
        try
        {
            WriteRoot(provider, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\empty\\helper.lua", "empty");
            WriteRoot(provider, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\active\\init.lua", "require('helper'); return 'active init'");
            WriteRoot(provider, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\active\\helper.lua", "active helper");
            WriteRoot(provider, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\merged\\helper.lua", "merged helper");
            WriteRoot(Path.Combine(root, "InitProvider"), "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\merged\\init.lua", "require('helper'); return 'merged init'");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Provider", provider), new DeploymentProvider("InitProvider", Path.Combine(root, "InitProvider"))]);

            string[] expected = ["require('helper'); return 'active init'", "active helper", "merged helper", "require('helper'); return 'merged init'"];
            CollectionAssert.AreEquivalent(expected, inventory.LuaSources.Select(value => value.Text).ToArray());
            SourceAnalysisFailure failure = inventory.Failures.Single(value => value.Surface == "CET Lua activation");
            Assert.AreEqual("bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\empty", failure.FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersDoesNotResurrectALoserInitLuaExcludedFromTheEffectiveCetModRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-cet-excluded-init-" + Guid.NewGuid().ToString("N"));
        string high = Path.Combine(root, "High");
        string low = Path.Combine(root, "Low");
        try
        {
            WriteRoot(high, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\init.lua", "high init");
            WriteRoot(low, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\init.lua", "low init");
            WriteRoot(low, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\helper.lua", "low helper");
            string excluded = Path.Combine(high, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "shared", "init.lua");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("High", high), new DeploymentProvider("Low", low)], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excluded });

            Assert.AreEqual(0, inventory.LuaSources.Length);
            Assert.AreEqual(1, inventory.Failures.Count(value => value.Surface == "CET Lua activation"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersCombinesCetRootCandidatesWithoutCaseSensitiveVirtualPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-cet-root-case-" + Guid.NewGuid().ToString("N"));
        string helper = Path.Combine(root, "Helper");
        string initializer = Path.Combine(root, "Initializer");
        try
        {
            WriteRoot(helper, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Shared\\helper.lua", "helper");
            WriteRoot(initializer, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\init.lua", "require('HELPER')");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Helper", helper), new DeploymentProvider("Initializer", initializer)]);

            string[] expected = ["helper", "require('HELPER')"];
            CollectionAssert.AreEquivalent(expected, inventory.LuaSources.Select(value => value.Text).ToArray());
            Assert.IsFalse(inventory.Failures.Any(value => value.Surface == "CET Lua activation"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersExcludesLooseCetLuaWithoutAnActivatedModRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-cet-loose-lua-" + Guid.NewGuid().ToString("N"));
        string provider = Path.Combine(root, "Provider");
        try
        {
            WriteRoot(provider, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\loose.lua", "loose");

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Provider", provider)]);

            Assert.AreEqual(0, inventory.LuaSources.Length);
            SourceAnalysisFailure failure = inventory.Failures.Single(value => value.Surface == "CET Lua activation");
            Assert.AreEqual("bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\loose.lua", failure.FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanProvidersSkipsAVortexCetModRootWhoseEffectiveInitLuaIsMissingWithoutReportingItsCapturedHelperAsMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-cet-activation-" + Guid.NewGuid().ToString("N"));
        string alpha = Path.Combine(root, "Alpha");
        string beta = Path.Combine(root, "Beta");
        try
        {
            WriteRoot(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\helper.lua", "alpha helper");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase)
            {
                ["bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\helper.lua"] = "alpha",
                ["bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\shared\\init.lua"] = "alpha"
            };

            ModSourceInventory inventory = ModSourceScanner.ScanProviders([new DeploymentProvider("Alpha", alpha, null, "alpha"), new DeploymentProvider("Beta", beta, null, "beta")], winners);

            Assert.AreEqual(0, inventory.LuaSources.Length);
            Assert.AreEqual(1, inventory.Failures.Count(value => value.Surface == "CET Lua activation"));
            Assert.IsFalse(inventory.Failures.Any(value => value.Surface == "CET Lua" && value.FilePath.EndsWith("\\helper.lua", StringComparison.OrdinalIgnoreCase)));
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
