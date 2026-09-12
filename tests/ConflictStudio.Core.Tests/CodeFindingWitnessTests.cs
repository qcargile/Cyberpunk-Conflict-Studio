using System.Text.Json;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class CodeFindingWitnessTests
{
    private const string ChromeBallisticsRoot = @"D:\Chrome & Blood - Compiled\mods\Chrome Ballistics - Weapon Rebalance";
    private const string KvdPath = @"r6\tweaks\KV Inhibitor\KVD_Techtronika.yaml";
    private const string ArmorPath = @"r6\tweaks\^global_armor_penetration.yaml";
    private const string ArmorMember = "Items.kvsilentstats_armor";
    private static readonly int[] ArmorLines = [24, 120];
    private static readonly string[] OpposingMembers = ["Items.A", "Items.B"];
    private static readonly int[] RepeatedOccurrences = [1, 2];

    [TestMethod]
    public void ChromeBallisticsFullScanBindsArmorAdditionAndRemoval()
    {
        string root = TempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            Copy(ChromeBallisticsRoot, root, KvdPath);
            Copy(ChromeBallisticsRoot, root, ArmorPath);

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);
            ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "Items.KVD_Techtronika.statModifiers");
            CodeFindingWitness witness = item.Comparisons.Single();

            Assert.AreEqual(CodeFindingWitnessKind.OpposingArrayMutation, witness.Kind);
            Assert.AreEqual(ArmorMember, witness.Member);
            Assert.HasCount(2, witness.Participants);
            CollectionAssert.AreEquivalent(ArmorLines, witness.Participants.Select(value => value.Line).ToArray());
            CollectionAssert.AreEquivalent(new[] { CodeEvidenceOperationKind.TweakArrayAppendOnce, CodeEvidenceOperationKind.TweakArrayRemove }, witness.Participants.Select(value => value.Kind).ToArray());
            Assert.IsTrue(witness.CanCompare(witness.Participants[0], witness.Participants[1]));
            Assert.IsTrue(witness.Participants.All(value => value.Sources.Length == 1 && value.Sources[0].Member == ArmorMember));
            Assert.HasCount(24, receipt.CodeEvidence.Where(value => value.Target == item.Target).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void OpposingArrayMembersBecomeSeparateIssues()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            Write(alpha, "r6\\tweaks\\adds.yaml", "Items.Test:\n  values:\n    - !append-once Items.A\n    - !append-once Items.B");
            Write(alpha, "r6\\tweaks\\removes.yaml", "Items.Test:\n  values:\n    - !remove Items.A\n    - !remove Items.B");

            ConflictWorkItem item = WorkItem(BuildReceipt([new("Alpha", alpha)]), "Items.Test.values", ConflictSurface.ScriptAndTweak);

            Assert.HasCount(2, item.Comparisons);
            CollectionAssert.AreEquivalent(OpposingMembers, item.Comparisons.Select(value => value.Member).ToArray());
            Assert.IsTrue(item.Comparisons.All(value => value.Participants.Length == 2 && value.CanCompare(value.Participants[0], value.Participants[1])));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ScalarIssueKeepsEqualDeclarationsButAllowsOnlyDifferentValues()
    {
        string root = TempRoot();
        try
        {
            DeploymentProvider[] providers = ScalarProviders(root, "1", "1", "2");
            CodeFindingWitness witness = WorkItem(BuildReceipt(providers), "Items.Test.value", ConflictSurface.ScriptAndTweak).Comparisons.Single();
            CodeFindingParticipant[] ones = witness.Participants.Where(value => value.NormalizedValue == "1").ToArray();
            CodeFindingParticipant two = witness.Participants.Single(value => value.NormalizedValue == "2");

            Assert.HasCount(3, witness.Participants);
            Assert.IsFalse(witness.CanCompare(ones[0], ones[1]));
            Assert.IsTrue(witness.CanCompare(ones[0], two));
            CollectionAssert.AreEqual(new[] { two.OperationId }, witness.OpponentsFor(ones[0]).Select(value => value.OperationId).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void DuplicateMethodsExcludeWrapsAndRetainIdenticalDeclarations()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            string addition = "@addMethod(PlayerPuppet)\npublic func SharedValue() -> Int32 { return 1; }";
            Write(alpha, "r6\\scripts\\one.reds", addition);
            Write(beta, "r6\\scripts\\two.reds", addition);
            Write(beta, "r6\\scripts\\wrap.reds", "@wrapMethod(PlayerPuppet)\npublic func SharedValue() -> Int32 { return wrappedMethod(); }");

            CodeFindingWitness witness = WorkItem(BuildReceipt([new("Alpha", alpha), new("Beta", beta)]), "PlayerPuppet.SharedValue()", ConflictSurface.ScriptAndTweak).Comparisons.Single(value => value.Kind == CodeFindingWitnessKind.DuplicateMemberDeclaration);

            Assert.IsTrue(witness.IsDuplicateDeclaration);
            Assert.HasCount(2, witness.Participants);
            Assert.IsTrue(witness.Participants.All(value => value.Kind == CodeEvidenceOperationKind.RedScriptAddMethod && value.Sources.Length == 1));
            Assert.IsTrue(witness.CanCompare(witness.Participants[0], witness.Participants[1]));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void DuplicateFieldsRetainIdenticalDeclarations()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            string field = "@addField(PlayerPuppet)\npublic let sharedValue: Int32;";
            Write(alpha, "r6\\scripts\\one.reds", field);
            Write(beta, "r6\\scripts\\two.reds", field);

            CodeFindingWitness witness = WorkItem(BuildReceipt([new("Alpha", alpha), new("Beta", beta)]), "PlayerPuppet.sharedValue", ConflictSurface.ScriptAndTweak).Comparisons.Single();

            Assert.AreEqual(CodeFindingWitnessKind.DuplicateMemberDeclaration, witness.Kind);
            Assert.HasCount(2, witness.Participants);
            Assert.IsTrue(witness.Participants.All(value => value.Kind == CodeEvidenceOperationKind.RedScriptAddField && value.Sources.Length == 1));
            Assert.IsTrue(witness.CanCompare(witness.Participants[0], witness.Participants[1]));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ArrayReplacementPairsRequireAReplacementOperand()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            string gamma = Path.Combine(root, "Gamma");
            string delta = Path.Combine(root, "Delta");
            Write(alpha, "r6\\tweaks\\replace.yaml", "Items.Test:\n  values: [Items.A]");
            Write(beta, "r6\\tweaks\\append.yaml", "Items.Test:\n  values:\n    - !append Items.B");
            Write(gamma, "r6\\tweaks\\remove.yaml", "Items.Test:\n  values:\n    - !remove Items.C");
            Write(delta, "r6\\tweaks\\replace-other.yaml", "Items.Test:\n  values: [Items.D]");

            ProfileScanReceipt receipt = BuildReceipt([new("Alpha", alpha), new("Beta", beta), new("Gamma", gamma), new("Delta", delta)]);
            Assert.IsTrue(receipt.TweakOverlaps.Any(value => value.Target == "Items.Test.values"), string.Join("; ", receipt.TweakOverlaps.Select(value => value.Target + ":" + value.Kind)));
            Assert.AreEqual(TweakOverlapKind.MixedArrayOperations, receipt.TweakOverlaps.Single(value => value.Target == "Items.Test.values").Kind);
            CodeFindingWitness witness = WorkItem(receipt, "Items.Test.values", ConflictSurface.ScriptAndTweak).Comparisons.Single(value => value.Kind == CodeFindingWitnessKind.ArrayReplacement);
            CodeFindingParticipant[] replacements = witness.Participants.Where(value => value.Kind == CodeEvidenceOperationKind.TweakArrayReplacement).ToArray();
            CodeFindingParticipant[] mutations = witness.Participants.Where(value => value.Kind != CodeEvidenceOperationKind.TweakArrayReplacement).ToArray();

            Assert.HasCount(4, witness.Participants);
            Assert.IsTrue(replacements.All(replacement => mutations.All(value => witness.CanCompare(replacement, value))));
            Assert.IsTrue(witness.CanCompare(replacements[0], replacements[1]));
            Assert.IsFalse(witness.CanCompare(mutations[0], mutations[1]));
            StringAssert.Contains(witness.Boundary, "order-sensitive");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void DeclarationRuntimeAndRuntimePairsUseExactDifferingProviders()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            string gamma = Path.Combine(root, "Gamma");
            Write(alpha, "r6\\tweaks\\value.yaml", "Items.Test.value: 1");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "TweakDB:SetFlat('Items.Test.value', 2)");
            Write(gamma, "r6\\scripts\\gamma.reds", "public func ApplyValue() -> Void { TweakDBInterface.SetFlat(t'Items.Test.value', 3); }");

            ConflictWorkItem[] items = ConflictWorkQueueBuilder.Build(BuildReceipt([new("Alpha", alpha), new("Beta", beta), new("Gamma", gamma)]), []);
            CodeFindingWitness declarationRuntime = items.Single(value => value.Surface == ConflictSurface.ScriptAndTweak && value.Target == "Items.Test.value").Comparisons.Single(value => value.Kind == CodeFindingWitnessKind.DeclarationRuntimeValue);
            CodeFindingWitness runtime = items.Single(value => value.Surface == ConflictSurface.SharedState && value.Target == "Items.Test.value").Comparisons.Single();

            Assert.HasCount(3, declarationRuntime.Participants);
            Assert.IsTrue(declarationRuntime.Participants.Where(value => value.Role == CodeFindingParticipantRole.RuntimeWrite).All(value => declarationRuntime.CanCompare(declarationRuntime.Participants.Single(item => item.Role == CodeFindingParticipantRole.ValueDeclaration), value)));
            Assert.HasCount(2, runtime.Participants);
            Assert.IsTrue(runtime.CanCompare(runtime.Participants[0], runtime.Participants[1]));
            Assert.IsTrue(runtime.Participants.All(value => value.Sources.Length == 1));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void AliasGeneratedAndRepeatedOperationsKeepTypedCachedIdentity()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Write(root, "profiles\\Standard\\modlist.txt", "+Alpha\n+Beta\n");
            Write(alpha, "r6\\tweaks\\values.yaml", "Items.Test.values: [!append Items.A, !append Items.A]\nShared: &chosen 1\nItems.Alias.value: *chosen\nItems.Generated$(tier):\n  $instances:\n    - { tier: Rare }\n  value: 1");
            Write(beta, "r6\\tweaks\\other.yaml", "Items.Test.values: [!append Items.A]\nItems.Alias.value: 2\nItems.GeneratedRare.value: 2");

            ProfileScanReceipt first = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            ProfileScanReceipt cached = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            CodeSourceEvidence[] repeated = first.CodeEvidence.Where(value => value.Target == "Items.Test.values" && value.FilePath.EndsWith("values.yaml", StringComparison.OrdinalIgnoreCase)).ToArray();
            CodeSourceEvidence alias = first.CodeEvidence.Single(value => value.Target == "Items.Alias.value" && value.IsAliasSource);
            CodeSourceEvidence generated = first.CodeEvidence.Single(value => value.Target == "Items.GeneratedRare.value" && value.IsGeneratedSource);

            Assert.HasCount(2, repeated);
            Assert.AreNotEqual(repeated[0].OperationId, repeated[1].OperationId);
            CollectionAssert.AreEqual(RepeatedOccurrences, repeated.Select(value => value.OperationOccurrence).ToArray());
            Assert.IsTrue(repeated.All(value => !value.IsAliasSource && value.NormalizedValue == "Items.A"));
            Assert.AreEqual("1", alias.NormalizedValue);
            Assert.AreEqual("1", generated.NormalizedValue);
            Assert.AreEqual(CodeEvidenceOperationKind.TweakScalarAssignment, generated.OperationKind);
            Assert.AreEqual(1, cached.Metrics!.CodeCacheHits);
            CollectionAssert.AreEqual(first.CodeEvidence, cached.CodeEvidence);
            CodeFindingWitness firstWitness = WorkItem(first, "Items.Test.values", ConflictSurface.ScriptAndTweak).Comparisons.Single();
            CodeFindingWitness cachedWitness = WorkItem(cached, "Items.Test.values", ConflictSurface.ScriptAndTweak).Comparisons.Single();
            Assert.AreEqual(firstWitness.StableId, cachedWitness.StableId);
            Assert.IsTrue(cachedWitness.Participants.All(value => value.Sources.Length == 1 && value.Sources[0].OperationOccurrence > 0));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CachedSubsetKeepsSlashAndBackslashMembersDistinct()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Write(root, "profiles\\Standard\\modlist.txt", "+Alpha\n+Beta\n");
            Write(alpha, "r6\\tweaks\\values.yaml", "Items.Test.values: [!append A\\B, !append A/B]");
            Write(beta, "r6\\tweaks\\other.yaml", "Items.Test.values: [!append A/B]");

            ProfileScanReceipt first = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            ProfileScanReceipt cached = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            ConflictWorkItem firstItem = WorkItem(first, "Items.Test.values", ConflictSurface.ScriptAndTweak);
            ConflictWorkItem cachedItem = WorkItem(cached, "Items.Test.values", ConflictSurface.ScriptAndTweak);
            CodeFindingParticipant firstAlpha = firstItem.Comparisons.Single().Participants.Single(value => value.Provider == "Alpha");
            CodeFindingParticipant cachedAlpha = cachedItem.Comparisons.Single().Participants.Single(value => value.Provider == "Alpha");
            CodeSourceEvidence unrelated = first.CodeEvidence.Single(value => value.Provider == "Alpha" && value.NormalizedValue == "A\\B");

            Assert.AreEqual("A/B", firstAlpha.NormalizedValue);
            Assert.AreEqual(firstAlpha.OperationId, cachedAlpha.OperationId);
            Assert.AreNotEqual(firstAlpha.OperationId, unrelated.OperationId);
            Assert.AreEqual(1, firstAlpha.Sources.Single().OperationOccurrence);
            Assert.AreEqual(1, cachedAlpha.Sources.Single().OperationOccurrence);
            Assert.AreEqual(firstItem.EvidenceSha256, cachedItem.EvidenceSha256);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void OpposingMutationWitnessKeepsOnlyParticipantsWithAnOpponent()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Test.values:\n  - !remove Items.A\n  - !append-once Items.A");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Test.values:\n  - !remove Items.A");

            CodeFindingWitness witness = WorkItem(BuildReceipt([new("Alpha", alpha), new("Beta", beta)]), "Items.Test.values", ConflictSurface.ScriptAndTweak).Comparisons.Single();

            Assert.HasCount(2, witness.Participants);
            Assert.IsTrue(witness.Participants.All(value => witness.OpponentsFor(value).Length > 0));
            Assert.IsTrue(witness.CanCompare(witness.Participants[0], witness.Participants[1]));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void WarmProfileScanCacheKeepsOnlyDifferingSharedStateWrites()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Write(root, "profiles\\Standard\\modlist.txt", "+Alpha\n+Beta\n");
            Write(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Alpha\\init.lua", "TweakDB:SetFlat('Items.Test.value', 2)\nTweakDB:SetFlat('Items.Test.value', 1)");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "TweakDB:SetFlat('Items.Test.value', 2)");

            ProfileScanReceipt cold = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            ProfileScanReceipt warm = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            CodeFindingWitness coldWitness = ConflictWorkQueueBuilder.Build(cold, []).Single(value => value.Surface == ConflictSurface.SharedState).Comparisons.Single();
            CodeFindingWitness warmWitness = ConflictWorkQueueBuilder.Build(warm, []).Single(value => value.Surface == ConflictSurface.SharedState).Comparisons.Single();

            Assert.AreEqual(0, cold.Metrics!.CodeCacheHits);
            Assert.AreEqual(1, warm.Metrics!.CodeCacheHits);
            Assert.HasCount(2, coldWitness.Participants);
            Assert.HasCount(2, warmWitness.Participants);
            Assert.IsTrue(warmWitness.Participants.All(value => warmWitness.OpponentsFor(value).Length == 1));
            CollectionAssert.AreEquivalent(coldWitness.Participants.Select(value => value.NormalizedValue).ToArray(), warmWitness.Participants.Select(value => value.NormalizedValue).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void MissingSourceEvidenceStaysUnavailableWithoutChangingReviewIdentityOrExport()
    {
        string root = TempRoot();
        try
        {
            ProfileScanReceipt receipt = BuildReceipt(ScalarProviders(root, "1", "2"));
            ConflictWorkItem complete = WorkItem(receipt, "Items.Test.value", ConflictSurface.ScriptAndTweak);
            ConflictWorkItem unavailable = WorkItem(receipt with { CodeEvidence = [] }, "Items.Test.value", ConflictSurface.ScriptAndTweak);
            EvidenceDecision decision = new(receipt.ProfileName, complete.Target, complete.Providers, complete.EvidenceSha256, "Expected values.", DateTimeOffset.UtcNow, receipt.InstallationId!, complete.Surface);
            ConflictWorkItem reviewed = ConflictWorkQueueBuilder.Build(receipt, [decision]).Single(value => value.Target == complete.Target && value.Surface == complete.Surface);

            Assert.AreEqual(complete.EvidenceSha256, unavailable.EvidenceSha256);
            Assert.AreEqual(complete.EvidenceSha256, reviewed.EvidenceSha256);
            Assert.IsTrue(unavailable.Comparisons.Single().Participants.All(value => value.Sources.Length == 0));
            Assert.HasCount(1, reviewed.Comparisons);
            string json = JsonSerializer.Serialize(complete);
            Assert.IsFalse(json.Contains("Comparisons", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("OperationId", StringComparison.Ordinal));
            string receiptJson = JsonSerializer.Serialize(receipt);
            Assert.IsFalse(receiptJson.Contains("OperationId", StringComparison.Ordinal));
            Assert.IsFalse(receiptJson.Contains("OperationOccurrence", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ConflictWorkItem WorkItem(ProfileScanReceipt receipt, string target, ConflictSurface surface)
        => ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == target && value.Surface == surface);

    private static ProfileScanReceipt BuildReceipt(DeploymentProvider[] providers)
    {
        DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
        ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
        SharedStateWriteFinding[] stateFindings = SharedStateWriteAnalyzer.Analyze(writes);
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        InteractionFinding[] interactions = InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps, tweaks.Operations, writes);
        CodeSourceEvidence[] sourceEvidence = CodeSourceEvidenceBuilder.Build(manifest, inventory, interactions, flows, callbacks, writes, stateFindings, tweaks.Operations);
        return new ProfileScanReceipt(2, "Standard", DateTimeOffset.UtcNow, providers.Select(value => value.Name).ToArray(), [], [], [], [], interactions, flows, stateFindings, callbacks, tweaks.Overlaps, [], [], [], InstallationId: "comparison-tests")
        {
            SourceProviders = providers,
            CodeEvidence = sourceEvidence
        };
    }

    private static DeploymentProvider[] ScalarProviders(string root, params string[] values)
    {
        string[] names = ["Alpha", "Beta", "Gamma"];
        DeploymentProvider[] providers = values.Select((value, index) => new DeploymentProvider(names[index], Path.Combine(root, names[index]))).ToArray();
        foreach ((DeploymentProvider provider, int index) in providers.Select((value, index) => (value, index))) Write(provider.RootPath, $"r6\\tweaks\\value-{index}.yaml", $"Items.Test.value: {values[index]}");
        return providers;
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-finding-witness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Copy(string sourceRoot, string destinationRoot, string relative)
    {
        string source = Path.Combine(sourceRoot, relative);
        Assert.IsTrue(File.Exists(source), source);
        string destination = Path.Combine(destinationRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
