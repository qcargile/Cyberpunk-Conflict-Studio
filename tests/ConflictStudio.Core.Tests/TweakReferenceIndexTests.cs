using ConflictStudio.Core;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class TweakReferenceIndexTests
{
    [TestMethod]
    [DataRow("null-operations")]
    [DataRow("null-references")]
    [DataRow("null-operation-entry")]
    [DataRow("null-reference-entry")]
    [DataRow("null-definition")]
    [DataRow("inconsistent-count")]
    [DataRow("inconsistent-limit")]
    [DataRow("reference-binding")]
    [DataRow("definition-path")]
    [DataRow("definition-provider")]
    [DataRow("definition-file")]
    [DataRow("definition-hash")]
    [DataRow("definition-range")]
    [DataRow("definition-focus")]
    [DataRow("definition-inactive")]
    [DataRow("definition-missing")]
    [DataRow("definitions-empty")]
    [DataRow("definition-duplicate")]
    [DataRow("definition-omitted")]
    public void MalformedCachedReferenceMetadataIsRebuilt(string corruption)
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Target.values:\n  - !append Items.Reference\n  - !append Items.Other\nItems.Reference:\n  $type: Type.Reference\n  value: 1\nItems.Other:\n  $type: Type.Other\n  value: 2\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Target.values:\n  - !remove Items.Reference\n");
            if (corruption == "definition-inactive")
            {
                Write(alpha, "r6\\tweaks\\shared.yaml", "Items.Other:\n  $type: Type.Other\n  value: 3\n");
                Write(beta, "r6\\tweaks\\shared.yaml", "Items.Other:\n  $type: Type.Other\n  value: 4\n");
            }
            else if (corruption is "definition-duplicate" or "definition-omitted") Write(alpha, "r6\\tweaks\\shared.yaml", "Items.Other:\n  $type: Type.Other\n  value: 3\n");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            WriteFile(profile, "+Alpha\n+Beta\n");
            string cacheRoot = Path.Combine(root, "cache");
            ContentAddressedAnalysisCache cache = new(cacheRoot);
            Mo2Profile selected = new("Standard", profile);
            ProfileScanReceipt first = ProfileScanCoordinator.Scan(root, selected, DateTimeOffset.UtcNow, null, null, null, cache, CancellationToken.None);
            Assert.AreEqual(0, first.Metrics!.CodeCacheHits);
            string cachePath = Directory.GetFiles(Path.Combine(cacheRoot, "code"), "*.json.gz").Single();
            JsonObject document = ReadCache(cachePath);
            JsonObject references = document["TweakReferences"]!.AsObject();
            if (corruption == "null-operations") references["Operations"] = null;
            else if (corruption == "null-references") references["References"] = null;
            else if (corruption == "null-operation-entry") references["Operations"]![0] = null;
            else if (corruption == "null-reference-entry") references["References"]![0] = null;
            else if (corruption == "null-definition") references["References"]![0]!["Definitions"]![0] = null;
            else if (corruption == "inconsistent-count")
            {
                references["References"]![0]!["TotalDefinitions"] = 0;
                references["References"]![0]!["IsLimited"] = false;
            }
            else if (corruption == "reference-binding") references["Operations"]!.AsArray().First(value => value!["Reference"]!.GetValue<string>() == "Items.Reference")!["Reference"] = "Items.Other";
            else if (corruption is "definition-missing" or "definitions-empty" or "definition-duplicate" or "definition-omitted")
            {
                JsonArray sets = references["References"]!.AsArray();
                foreach (JsonObject set in sets.OfType<JsonObject>().Where(set => corruption == "definitions-empty" || set["Reference"]!.GetValue<string>() == "Items.Other"))
                {
                    JsonArray definitions = set["Definitions"]!.AsArray();
                    if (corruption == "definition-duplicate") definitions[1] = definitions[0]!.DeepClone();
                    else if (corruption == "definition-omitted") { definitions.RemoveAt(1); set["TotalDefinitions"] = 1; }
                    else { definitions.Clear(); set["TotalDefinitions"] = 0; }
                    set["IsLimited"] = false;
                }
            }
            else if (corruption == "definition-inactive")
            {
                string winner = first.TweakReferences.References.Single(reference => reference.Reference == "Items.Other").Definitions.Single(source => source.FilePath == "r6\\tweaks\\shared.yaml").Provider;
                string loser = winner == "Alpha" ? "Beta" : "Alpha";
                string path = Path.Combine(root, "mods", loser, "r6", "tweaks", "shared.yaml");
                string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                CodeSourceEvidence inactive = new(ConflictSurface.ScriptAndTweak, "Items.Other", loser, "r6\\tweaks\\shared.yaml", path, hash, 1, 3, 2, 2, true, "TweakXL record definition");
                references["References"]!.AsArray().Single(reference => reference!["Reference"]!.GetValue<string>() == "Items.Other")!["Definitions"]![0] = JsonSerializer.SerializeToNode(inactive);
            }
            else if (corruption.StartsWith("definition-", StringComparison.Ordinal))
            {
                JsonObject definition = references["References"]![0]!["Definitions"]![0]!.AsObject();
                if (corruption == "definition-path") definition["PhysicalPath"] = Path.Combine(root, "unrelated.txt");
                else if (corruption == "definition-provider") definition["Provider"] = "Unrelated provider";
                else if (corruption == "definition-file") definition["FilePath"] = "r6\\tweaks\\unrelated.yaml";
                else if (corruption == "definition-hash") definition["SourceSha256"] = new string('0', 64);
                else if (corruption == "definition-range")
                {
                    definition["StartLine"] = 1;
                    definition["EndLine"] = 2;
                    definition["FocusStartLine"] = 1;
                    definition["FocusEndLine"] = 2;
                }
                else definition["FocusStartLine"] = definition["StartLine"]!.GetValue<int>();
            }
            else references["References"]![0]!["IsLimited"] = true;
            WriteCache(cachePath, document);

            ProfileScanReceipt rebuilt = ProfileScanCoordinator.Scan(root, selected, DateTimeOffset.UtcNow, null, null, null, cache, CancellationToken.None);

            Assert.AreEqual(0, rebuilt.Metrics!.CodeCacheHits);
            CodeSourceEvidence source = rebuilt.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.Reference");
            Assert.AreEqual(TweakReferenceState.Available, rebuilt.TweakReferences.Resolve(source).State);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ProfileScanResolvesArmorMembersToTheirExactLocalRecordDefinitions()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            Write(alpha, "r6\\tweaks\\weapon.yaml", "Items.KVD_Techtronika:\n  statModifiers:\n    - !append-once Items.kvsilentstats_armor\n\nItems.kvsilentstats_armor:\n  $type: gamedataConstantStatModifier_Record\n  value: 0.75\n  modifierType: Additive\n  statType: BaseStats.CanWeaponIgnoreArmor\n");
            Write(beta, "r6\\tweaks\\armor.yaml", "Items.KVD_Techtronika:\n  statModifiers:\n    - !remove Items.kvsilentstats_armor\n    - !append-once ChromeBallistics.ArmorPenetrationPlus25\n\nChromeBallistics.ArmorPenetrationPlus25:\n  $type: gamedataConstantStatModifier_Record\n  modifierType: Additive\n  statType: BaseStats.CanWeaponIgnoreArmor\n  value: 0.25\n");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            WriteFile(profile, "+Alpha\n+Beta\n");

            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            CodeSourceEvidence armorUse = receipt.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.kvsilentstats_armor");
            CodeSourceEvidence competingUse = receipt.CodeEvidence.Single(value => value.Provider == "Beta" && value.Member == "ChromeBallistics.ArmorPenetrationPlus25");

            TweakReferenceResolution armor = receipt.TweakReferences.Resolve(armorUse);
            TweakReferenceResolution competing = receipt.TweakReferences.Resolve(competingUse);

            Assert.AreEqual(TweakReferenceState.Available, armor.State);
            Assert.AreEqual("Items.kvsilentstats_armor", armor.Reference);
            Assert.HasCount(1, armor.Definitions);
            Assert.AreEqual(5, armor.Definitions[0].StartLine);
            Assert.AreEqual(9, armor.Definitions[0].EndLine);
            StringAssert.Contains(armor.Message, "local definition");
            Assert.AreEqual(TweakReferenceState.Available, competing.State);
            Assert.AreEqual("ChromeBallistics.ArmorPenetrationPlus25", competing.Reference);
            Assert.HasCount(1, competing.Definitions);

            TweakReferenceIndex redirected = receipt.TweakReferences with { Operations = receipt.TweakReferences.Operations.Select(operation => operation.OperationId == armorUse.OperationId ? operation with { Reference = "ChromeBallistics.ArmorPenetrationPlus25" } : operation).ToArray() };
            Assert.IsEmpty(redirected.Resolve(armorUse).Definitions);

            CodeSourceDocument definition = await CodeSourceReader.ReadAsync(armor.Definitions[0]);
            Assert.AreEqual("Items.kvsilentstats_armor:", definition.Lines[armor.Definitions[0].StartLine - 1]);
            Assert.AreEqual("  statType: BaseStats.CanWeaponIgnoreArmor", definition.Lines[armor.Definitions[0].EndLine - 1]);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ExactRecordIdentityReturnsEveryDefinitionWithoutPrefixMatches()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Target.values:\n  - !append Items.Armor\nItems.Armor:\n  $type: Type.One\n  value: 1\nItems.ArmorPlus:\n  $type: Type.Two\n  value: 2\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Target.values:\n  - !remove Items.Armor\nItems.Armor:\n  $base: Items.BaseArmor\n  value: 3\n");

            ProfileScanReceipt receipt = Scan([new("Alpha", alpha), new("Beta", beta)]);
            CodeSourceEvidence source = receipt.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.Armor");

            TweakReferenceResolution resolution = receipt.TweakReferences.Resolve(source);

            Assert.AreEqual(TweakReferenceState.Ambiguous, resolution.State);
            Assert.HasCount(2, resolution.Definitions);
            Assert.IsTrue(resolution.Definitions.All(value => value.Target == "Items.Armor"));
            Assert.IsFalse(resolution.Definitions.Any(value => value.Target == "Items.ArmorPlus"));
            StringAssert.Contains(resolution.Message, "2 exact local definitions");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void MissingDynamicAliasAndGeneratedReferencesRemainExplicitlyUnavailable()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Shared: &members\n  - !append Items.AliasRecord\nItems.AliasTarget.values: *members\nItems.DynamicTarget.values:\n  - !append Items.$(record)\nItems.Template$(record):\n  $instances:\n    - { record: GeneratedTarget }\n  values:\n    - !append Items.GeneratedRecord\nItems.MissingTarget.values:\n  - !append Items.DoesNotExist\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.AliasTarget.values:\n  - !append Items.OtherAlias\nItems.DynamicTarget.values:\n  - !append Items.OtherDynamic\nItems.TemplateGeneratedTarget.values:\n  - !append Items.OtherGenerated\nItems.MissingTarget.values:\n  - !append Items.OtherMissing\n");

            ProfileScanReceipt receipt = Scan([new("Alpha", alpha), new("Beta", beta)]);

            Assert.AreEqual(TweakReferenceState.AliasSource, Resolve(receipt, "Items.AliasRecord").State);
            Assert.AreEqual(TweakReferenceState.Dynamic, Resolve(receipt, "Items.$(record)").State);
            Assert.AreEqual(TweakReferenceState.GeneratedSource, Resolve(receipt, "Items.GeneratedRecord").State);
            Assert.AreEqual(TweakReferenceState.Missing, Resolve(receipt, "Items.DoesNotExist").State);
            Assert.IsTrue(Resolve(receipt, "Items.DoesNotExist").Message.Contains("No exact local definition", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task WarmScanKeepsReferenceIndexAndDefinitionHashes()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha");
            string beta = Path.Combine(root, "mods", "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Target.values:\n  - !append Items.Reference\nItems.Reference:\n  $type: Type.Reference\n  value: 1\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Target.values:\n  - !remove Items.Reference\n");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            WriteFile(profile, "+Alpha\n+Beta\n");

            ProfileScanReceipt first = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            ProfileScanReceipt warm = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", profile), DateTimeOffset.UtcNow);
            CodeSourceEvidence firstUse = first.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.Reference");
            CodeSourceEvidence warmUse = warm.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.Reference");

            TweakReferenceResolution firstResolution = first.TweakReferences.Resolve(firstUse);
            TweakReferenceResolution warmResolution = warm.TweakReferences.Resolve(warmUse);

            Assert.AreEqual(1, warm.Metrics!.CodeCacheHits);
            CollectionAssert.AreEqual(firstResolution.Definitions, warmResolution.Definitions);
            Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(warm).Contains("TweakReferences", StringComparison.Ordinal));
            WriteFile(firstResolution.Definitions[0].PhysicalPath, "Items.Reference:\n  $type: Type.Reference\n  value: 2\n");
            await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(firstResolution.Definitions[0]));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CaptureLimitDoesNotReportAnOmittedReferenceAsMissing()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            int memberCount = TweakReferenceIndex.MaximumOperations + 1;
            string members = string.Join('\n', Enumerable.Range(0, memberCount).Select(value => $"  - !append-once Items.Reference{value:D4}"));
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Target.values:\n" + members + "\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Target.values: [Items.Other]\n");

            ProfileScanReceipt receipt = Scan([new("Alpha", alpha), new("Beta", beta)]);
            TweakReferenceResolution[] resolutions = receipt.CodeEvidence.Where(value => value.Provider == "Alpha").Select(receipt.TweakReferences.Resolve).ToArray();

            Assert.AreEqual(TweakReferenceIndex.MaximumOperations, receipt.TweakReferences.Operations.Length);
            Assert.AreEqual(memberCount, receipt.TweakReferences.TotalOperations);
            Assert.HasCount(1, resolutions.Where(value => value.State == TweakReferenceState.Limited).ToArray());
            Assert.IsTrue(resolutions.Single(value => value.State == TweakReferenceState.Limited).IsLimited);
            StringAssert.Contains(resolutions.Single(value => value.State == TweakReferenceState.Limited).Message, $"{TweakReferenceIndex.MaximumOperations:N0} of {memberCount:N0} source operations");
            Assert.IsFalse(resolutions.Any(value => value.State == TweakReferenceState.Missing && value.Reference == resolutions.Single(item => item.State == TweakReferenceState.Limited).Reference));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void DefinitionLimitReturnsEveryCapturedAmbiguityWithALimitFlag()
    {
        string root = TempRoot();
        try
        {
            List<DeploymentProvider> providers = [];
            int definitionCount = TweakReferenceIndex.MaximumDefinitionsPerReference + 1;
            for (int index = 0; index < definitionCount; index++)
            {
                string providerRoot = Path.Combine(root, $"Provider{index:D2}");
                string mutation = index == 0 ? "Items.Target.values:\n  - !append Items.Shared\n" : index == 1 ? "Items.Target.values:\n  - !remove Items.Shared\n" : string.Empty;
                Write(providerRoot, $"r6\\tweaks\\shared-{index:D2}.yaml", mutation + $"Items.Shared:\n  $type: Type.Provider{index:D2}\n  value: {index}\n");
                providers.Add(new DeploymentProvider($"Provider{index:D2}", providerRoot));
            }

            ProfileScanReceipt receipt = Scan(providers.ToArray());
            CodeSourceEvidence source = receipt.CodeEvidence.Single(value => value.Provider == "Provider00" && value.Member == "Items.Shared");

            TweakReferenceResolution resolution = receipt.TweakReferences.Resolve(source);

            Assert.AreEqual(TweakReferenceState.Ambiguous, resolution.State);
            Assert.HasCount(TweakReferenceIndex.MaximumDefinitionsPerReference, resolution.Definitions);
            Assert.IsTrue(resolution.IsLimited);
            StringAssert.Contains(resolution.Message, $"{definitionCount:N0} exact local definitions");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void RepeatedDefinitionsInOneFileKeepTheirSeparateSourceRanges()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Items.Target.values:\n  - !append Items.Shared\nItems.Shared:\n  $type: Type.First\n  value: 1\nItems.Shared:\n  $type: Type.Second\n  value: 2\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Target.values:\n  - !remove Items.Shared\n");

            ProfileScanReceipt receipt = Scan([new("Alpha", alpha), new("Beta", beta)]);
            CodeSourceEvidence source = receipt.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == "Items.Shared");

            TweakReferenceResolution resolution = receipt.TweakReferences.Resolve(source);

            Assert.AreEqual(TweakReferenceState.Ambiguous, resolution.State);
            int[] startLines = [3, 6];
            int[] endLines = [5, 8];
            CollectionAssert.AreEqual(startLines, resolution.Definitions.Select(value => value.StartLine).ToArray());
            CollectionAssert.AreEqual(endLines, resolution.Definitions.Select(value => value.EndLine).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }

    private static TweakReferenceResolution Resolve(ProfileScanReceipt receipt, string member)
        => receipt.TweakReferences.Resolve(receipt.CodeEvidence.Single(value => value.Provider == "Alpha" && value.Member == member));

    private static ProfileScanReceipt Scan(DeploymentProvider[] providers)
    {
        DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
        ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
        SharedStateWriteFinding[] states = SharedStateWriteAnalyzer.Analyze(writes);
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        InteractionFinding[] interactions = InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps, tweaks.Operations, writes);
        CodeSourceCapture capture = CodeSourceEvidenceBuilder.BuildCapture(manifest, inventory, interactions, flows, callbacks, writes, states, tweaks.Operations);
        return new ProfileScanReceipt(2, "Test", DateTimeOffset.UtcNow, providers.Select(value => value.Name).ToArray(), [], [], [], [], interactions, flows, states, callbacks, tweaks.Overlaps, [], [])
        {
            SourceProviders = providers,
            CodeEvidence = capture.Evidence,
            TweakReferences = capture.TweakReferences
        };
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-tweak-references-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relativePath, string text)
        => WriteFile(Path.Combine(root, relativePath), text);

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static JsonObject ReadCache(string path)
    {
        using FileStream file = File.OpenRead(path);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        return JsonNode.Parse(gzip)!.AsObject();
    }

    private static void WriteCache(string path, JsonObject document)
    {
        using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using GZipStream gzip = new(file, CompressionLevel.Fastest);
        using StreamWriter writer = new(gzip, new UTF8Encoding(false));
        writer.Write(document.ToJsonString());
    }
}
