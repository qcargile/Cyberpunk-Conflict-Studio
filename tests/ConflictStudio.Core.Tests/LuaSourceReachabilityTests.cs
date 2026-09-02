using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class LuaSourceReachabilityTests
{
    [TestMethod]
    [DataRow("require", false)]
    [DataRow("dofile", false)]
    [DataRow("loadfile", false)]
    [DataRow("require", true)]
    public void ConcatenationDoesNotTurnAGlobalImportIntoAMemberCall(string loader, bool sameExpression)
    {
        using LuaImportFixture fixture = new();
        string prefix = sameExpression ? "local label = 'Example' .. " : "local label = 'Example' .. ' mod'\n";
        fixture.Write("Base", "Example/init.lua", prefix + loader + "('module')");
        fixture.Write("Base", "Example/module.lua", "Override('PlayerPuppet', 'Test', function() end)");
        fixture.Write("Other", "Other/init.lua", "Observe('PlayerPuppet', 'Test', function() end)");

        ModSourceInventory inventory = fixture.Scan();

        Assert.AreEqual(3, inventory.LuaSources.Length);
        Assert.IsTrue(InteractionReportBuilder.Build(inventory).Any(value => value.Target == "PlayerPuppet.Test"));
    }

    [TestMethod]
    public void OnlyLiteralImportCandidatesParticipateInConflicts()
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", "require('modules/one')");
        fixture.Write("Patch", "Example/modules/one.lua", "require('package')\nOverride('PlayerPuppet', 'Real', function() end)");
        fixture.Write("Base", "Example/package/init.lua", "require('modules/one')");
        fixture.Write("Base", "Example/unused.lua", "Override('PlayerPuppet', 'Phantom', function() end)");
        fixture.Write("Other", "Other/init.lua", "Override('PlayerPuppet', 'Phantom', function() end)\nObserve('PlayerPuppet', 'Real', function() end)");

        ModSourceInventory inventory = fixture.Scan();
        InteractionFinding[] findings = InteractionReportBuilder.Build(inventory);

        Assert.AreEqual(4, inventory.LuaSources.Length);
        Assert.AreEqual("Patch", inventory.LuaSources.Single(value => value.FilePath.EndsWith("one.lua", StringComparison.Ordinal)).Provider);
        Assert.IsTrue(findings.Any(value => value.Target == "PlayerPuppet.Real"));
        Assert.IsFalse(findings.Any(value => value.Target == "PlayerPuppet.Phantom"));
    }

    [TestMethod]
    [DataRow("require 'modules/one'")]
    [DataRow("require(\"./modules/one.lua\")")]
    [DataRow("if available then require('modules/one') end")]
    [DataRow("dofile('modules/one')")]
    [DataRow("loadfile('modules/one')")]
    public void LiteralImportsAreModRootRelativeCandidates(string init)
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", init);
        fixture.Write("Base", "Example/modules/one.lua", "return {}");
        fixture.Write("Base", "Example/unreferenced.lua", "return {}");

        ModSourceInventory inventory = fixture.Scan();

        Assert.AreEqual(2, inventory.LuaSources.Length);
        Assert.IsTrue(inventory.LuaSources.Any(value => value.FilePath.EndsWith("one.lua", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("require(moduleName)")]
    [DataRow("require('modules/' .. name)")]
    [DataRow("local import = require; import('module')")]
    [DataRow("loadstring(source)()")]
    [DataRow("require [[module]]")]
    public void UnresolvedLoadingRetainsCandidatesAndNamesTheBoundary(string init)
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", init);
        fixture.Write("Base", "Example/module.lua", "return {}");

        ModSourceInventory inventory = fixture.Scan();

        Assert.AreEqual(2, inventory.LuaSources.Length);
        Assert.IsTrue(inventory.Failures.Any(value => value.Surface == "CET Lua reachability"));
    }

    [TestMethod]
    public void QuotedAndCommentedImportsDoNotAdmitUnreferencedFiles()
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", "local example = [[require('unused')]]\n-- require('unused')\nother.require('unused')");
        fixture.Write("Base", "Example/unused.lua", "return {}");

        Assert.AreEqual(1, fixture.Scan().LuaSources.Length);
    }

    [TestMethod]
    public void DotNamesDoNotBecomeSlashPathsAndMissingImportsAreNotConflicts()
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", "require('modules.one')");
        fixture.Write("Base", "Example/modules/one.lua", "return {}");
        ModSourceInventory inventory = fixture.Scan();

        Assert.AreEqual(1, inventory.LuaSources.Length);
        SourceAnalysisFailure failure = inventory.Failures.Single(value => value.Surface == "CET Lua import");
        StringAssert.Contains(failure.Message, "modules.one");
        ProfileScanReceipt receipt = new(2, "Example", DateTimeOffset.UtcNow, [], [], [], [], [], [], [], [], [], [], [], [], SourceFailures: inventory.Failures);
        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.IsActionable));
    }

    [TestMethod]
    public void ExcludedModuleDoesNotSelectThePackageFallbackOrLowerProvider()
    {
        using LuaImportFixture fixture = new();
        fixture.Write("Base", "Example/init.lua", "require('module')");
        string excluded = fixture.Write("Patch", "Example/module.lua", "return 'patch'");
        fixture.Write("Base", "Example/module.lua", "return 'lower'");
        fixture.Write("Base", "Example/module/init.lua", "return 'fallback'");

        ModSourceInventory inventory = fixture.Scan(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excluded });

        Assert.AreEqual(1, inventory.LuaSources.Length);
        Assert.IsTrue(inventory.Failures.Any(value => value.Surface == "CET Lua import"));
    }
}

internal sealed class LuaImportFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "conflict-studio-lua-imports-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Providers = ["Patch", "Base", "Other"];

    public string Write(string provider, string relative, string text)
    {
        string path = Path.Combine(_root, provider, "bin/x64/plugins/cyber_engine_tweaks/mods", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public ModSourceInventory Scan(IReadOnlySet<string>? exclusions = null)
        => ModSourceScanner.ScanProviders(Providers.Where(provider => Directory.Exists(Path.Combine(_root, provider))).Select(provider => new DeploymentProvider(provider, Path.Combine(_root, provider))).ToArray(), null, exclusions);

    public void Dispose() => Directory.Delete(_root, true);
}
