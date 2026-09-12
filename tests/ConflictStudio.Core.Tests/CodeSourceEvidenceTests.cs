using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class CodeSourceEvidenceTests
{
    private static readonly string[] DecodedLines = ["first", "second", "third", "fourth"];
    private static readonly int[] RepeatedMethodLines = [1, 8];
    private static readonly int[] RepeatedInvocationLines = [4, 5];
    private static readonly int[] RepeatedOccurrences = [1, 2];
    private static readonly int[] AliasUseLines = [4, 5];
    private static readonly int[] RecordAliasUseLines = [9, 10];
    private static readonly string[] NamedCallbackLines = ["local function replacement(self, wrapped)", "  return 19", "end"];

    [TestMethod]
    public async Task ReaderVerifiesRawUtf16BytesAndPreservesOriginalLines()
    {
        string root = TempRoot();
        try
        {
            string path = Path.Combine(root, "source.reds");
            string source = "first\r\nsecond\nthird\rfourth";
            await File.WriteAllTextAsync(path, source, Encoding.Unicode);
            CodeSourceEvidence evidence = Evidence(path, Hash(path), 2, 3);

            CodeSourceDocument document = await CodeSourceReader.ReadAsync(evidence);

            CollectionAssert.AreEqual(DecodedLines, document.Lines);
            Assert.AreSame(evidence, document.Evidence);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ReaderRefusesChangedDeletedAndOversizedSources()
    {
        string root = TempRoot();
        try
        {
            string changedPath = Path.Combine(root, "changed.lua");
            await File.WriteAllTextAsync(changedPath, "return 1");
            CodeSourceEvidence changed = Evidence(changedPath, Hash(changedPath), 1, 1);
            await File.WriteAllTextAsync(changedPath, "return 2");
            CodeSourceReadException changedError = await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(changed));
            StringAssert.Contains(changedError.Message, "changed after the scan");

            string deletedPath = Path.Combine(root, "deleted.lua");
            await File.WriteAllTextAsync(deletedPath, "return 1");
            CodeSourceEvidence deleted = Evidence(deletedPath, Hash(deletedPath), 1, 1);
            File.Delete(deletedPath);
            CodeSourceReadException deletedError = await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(deleted));
            StringAssert.Contains(deletedError.Message, "no longer available");

            string oversizedPath = Path.Combine(root, "oversized.reds");
            await using (FileStream stream = new(oversizedPath, FileMode.CreateNew, FileAccess.Write)) stream.SetLength(CodeSourceReader.MaximumSourceBytes + 1L);
            CodeSourceReadException oversizedError = await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(Evidence(oversizedPath, new string('0', 64), 1, 1)));
            StringAssert.Contains(oversizedError.Message, "larger than the 8 MB");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ReaderHonorsCancellationBeforeOpeningTheSource()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => CodeSourceReader.ReadAsync(Evidence("missing.reds", new string('0', 64), 1, 1), cancellation.Token));
    }

    [TestMethod]
    public async Task ReaderRefusesRangesOutsideTheVerifiedDocument()
    {
        string root = TempRoot();
        try
        {
            string path = Path.Combine(root, "source.reds");
            await File.WriteAllTextAsync(path, "one\ntwo");

            CodeSourceReadException error = await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(Evidence(path, Hash(path), 2, 3)));

            StringAssert.Contains(error.Message, "invalid source evidence");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task ReaderRefusesSourcesWithTooManyLinesBeforeSplittingThem()
    {
        string root = TempRoot();
        try
        {
            string path = Path.Combine(root, "many-lines.lua");
            await File.WriteAllTextAsync(path, new string('\n', CodeSourceReader.MaximumSourceLines));

            CodeSourceReadException error = await Assert.ThrowsExactlyAsync<CodeSourceReadException>(() => CodeSourceReader.ReadAsync(Evidence(path, Hash(path), 1, 1)));

            StringAssert.Contains(error.Message, "more than 200,000 lines");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void RedScriptEvidenceKeepsProvidersRepeatedMethodsCommentsAndNestedBracesDistinct()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\scripts\\alpha\\shared.reds", "@replaceMethod(Test)\npublic func Value() -> Int32 {\n  // nineteen\n  if true { return 19; }\n  return 0;\n}\n\n@replaceMethod(Test)\npublic func Value() -> Int32 { return 20; }");
            Write(beta, "r6\\scripts\\beta\\shared.reds", "@replaceMethod(Test)\npublic func Value() -> Int32 { return 10; }");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]).Where(value => value.Target == "Test.Value()").ToArray();

            Assert.HasCount(3, evidence);
            CollectionAssert.AreEqual(RepeatedMethodLines, evidence.Where(value => value.Provider == "Alpha").Select(value => value.StartLine).ToArray());
            CodeSourceEvidence nested = evidence.Single(value => value.Provider == "Alpha" && value.StartLine == 1);
            Assert.AreEqual(6, nested.EndLine);
            Assert.AreEqual(2, nested.FocusStartLine);
            Assert.IsTrue(nested.IsCompleteBlock);
            Assert.AreNotEqual(nested.PhysicalPath, evidence.Single(value => value.Provider == "Beta").PhysicalPath);
            Assert.IsTrue(evidence.All(value => Path.GetFileName(value.FilePath) == "shared.reds"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SameLineRedScriptOccurrencesRemainDistinctExcerpts()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\scripts\\alpha.reds", "@replaceMethod(Test) public func Value() -> Int32 { return 19; } @replaceMethod(Test) public func Value() -> Int32 { return 20; }");
            Write(beta, "r6\\scripts\\beta.reds", "@replaceMethod(Test) public func Value() -> Int32 { return 10; }");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]).Where(value => value.Target == "Test.Value()").ToArray();

            Assert.HasCount(3, evidence);
            CodeSourceEvidence[] alphaEvidence = evidence.Where(value => value.Provider == "Alpha").ToArray();
            Assert.HasCount(2, alphaEvidence);
            CollectionAssert.AreEqual(RepeatedOccurrences, alphaEvidence.Select(value => value.Occurrence).ToArray());
            Assert.IsTrue(alphaEvidence.All(value => value.StartLine == 1 && value.EndLine == 1 && !value.IsCompleteBlock && value.Description == "RedScript method excerpt"));
            Assert.AreEqual(1, evidence.Single(value => value.Provider == "Beta").Occurrence);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void LuaEvidenceKeepsRepeatedResolvedInvocationsAndInlineCallbacksDistinct()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Alpha\\init.lua", "local function Register()\n  Override('PlayerPuppet', 'Value', function(self, wrapped) return 19 end)\nend\nRegister()\nRegister()");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "Override('PlayerPuppet', 'Value', function(self, wrapped) return 10 end)");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]).Where(value => value.Target == "PlayerPuppet.Value").ToArray();

            Assert.HasCount(3, evidence);
            CollectionAssert.AreEqual(RepeatedInvocationLines, evidence.Where(value => value.Provider == "Alpha").Select(value => value.StartLine).ToArray());
            Assert.IsTrue(evidence.Where(value => value.Provider == "Alpha").All(value => !value.IsCompleteBlock && value.Description == "Resolved Lua hook invocation"));
            Assert.IsTrue(evidence.Single(value => value.Provider == "Beta").IsCompleteBlock);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task NamedLuaCallbackIncludesItsVerifiedDefinitionAndRegistrationExcerpt()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Alpha\\init.lua", "local function replacement(self, wrapped)\n  return 19\nend\nOverride('PlayerPuppet', 'Value', replacement)");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "Override('PlayerPuppet', 'Value', function(self, wrapped) return 10 end)");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]).Where(value => value.Target == "PlayerPuppet.Value").ToArray();
            CodeSourceEvidence body = evidence.Single(value => value.Provider == "Alpha" && value.Description == "Named Lua callback body");
            CodeSourceEvidence registration = evidence.Single(value => value.Provider == "Alpha" && value.Description == "Lua hook registration excerpt");
            CodeSourceDocument document = await CodeSourceReader.ReadAsync(body);

            Assert.AreEqual(1, body.StartLine);
            Assert.AreEqual(3, body.EndLine);
            Assert.IsTrue(body.IsCompleteBlock);
            Assert.AreEqual(4, registration.StartLine);
            Assert.IsFalse(registration.IsCompleteBlock);
            CollectionAssert.AreEqual(NamedCallbackLines, document.Lines[(body.StartLine - 1)..body.EndLine]);
            Assert.AreEqual(Hash(body.PhysicalPath), body.SourceSha256);
            Assert.IsTrue(evidence.Any(value => value.Provider == "Beta" && value.IsCompleteBlock));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void DuplicateLuaFunctionNamesDoNotClaimOneDefinitionAsTheCallbackBody()
    {
        string source = "local function replacement(self, wrapped) return 19 end\nlocal function replacement(self, wrapped) return 20 end\nOverride('PlayerPuppet', 'Value', replacement)";

        LuaHookRegistration registration = LuaHookRegistrationAnalyzer.Analyze(source).Single();

        Assert.AreEqual(-1, registration.CallbackStartIndex);
        Assert.AreEqual(-1, registration.CallbackEndIndex);
    }

    [TestMethod]
    public void AddedFieldsAndSharedStateWritesUseTheirOriginalDeclarationsAndCalls()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\scripts\\alpha.reds", "@addField(PlayerPuppet)\npublic let counter: Int32;");
            Write(beta, "r6\\scripts\\beta.reds", "@addField(PlayerPuppet)\npublic let counter: Float;");
            Write(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Alpha\\init.lua", "TweakDB:SetFlat(\n  'Items.Test.value',\n  19\n)");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "TweakDB:SetFlat('Items.Test.value', 10)");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]);

            CodeSourceEvidence[] fields = evidence.Where(value => value.Surface == ConflictSurface.ScriptAndTweak && value.Target == "PlayerPuppet.counter").ToArray();
            Assert.HasCount(2, fields);
            Assert.IsTrue(fields.All(value => value.StartLine == 1 && value.EndLine == 2 && value.IsCompleteBlock));
            CodeSourceEvidence[] writes = evidence.Where(value => value.Surface == ConflictSurface.SharedState && value.Target == "Items.Test.value").ToArray();
            Assert.HasCount(2, writes);
            Assert.AreEqual(4, writes.Single(value => value.Provider == "Alpha").EndLine);
            Assert.IsTrue(writes.All(value => value.IsCompleteBlock));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void TweakEvidenceShowsDirectGeneratedAndAliasSourceWithoutFabricatedValues()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Shared: &chosen 19\nItems.Direct.value: 19\nItems.Alias:\n  value: *chosen\nItems.Generated$(tier):\n  $instances:\n    - { tier: Rare }\n  value: 19\nItems.Array:\n  values:\n    - Items.A\n    - Items.B\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.Direct.value: 10\nItems.Alias:\n  value: 10\nItems.GeneratedRare:\n  value: 10\nItems.Array:\n  values:\n    - Items.C\n");

            CodeSourceEvidence[] evidence = Build([new("Alpha", alpha), new("Beta", beta)]);

            CodeSourceEvidence direct = evidence.Single(value => value.Provider == "Alpha" && value.Target == "Items.Direct.value");
            Assert.AreEqual(2, direct.StartLine);
            Assert.IsTrue(direct.IsCompleteBlock);
            CodeSourceEvidence alias = evidence.Single(value => value.Provider == "Alpha" && value.Target == "Items.Alias.value");
            Assert.AreEqual("TweakXL alias source excerpt", alias.Description);
            Assert.IsFalse(alias.IsCompleteBlock);
            CodeSourceEvidence generated = evidence.Single(value => value.Provider == "Alpha" && value.Target == "Items.GeneratedRare.value");
            Assert.AreEqual("Generated TweakXL source excerpt", generated.Description);
            Assert.IsFalse(generated.IsCompleteBlock);
            Assert.AreEqual(8, generated.StartLine);
            CodeSourceEvidence array = evidence.Single(value => value.Provider == "Alpha" && value.Target == "Items.Array.values");
            Assert.AreEqual(10, array.StartLine);
            Assert.AreEqual(12, array.EndLine);
            Assert.IsTrue(array.IsCompleteBlock);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task AliasBackedMutationsAndRecordsPointToEachOriginalUse()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\tweaks\\alpha.yaml", "Shared: &ops\n  - !append Items.A\n\nItems.First.values: *ops\nItems.Second.values: *ops\nSharedRecord: &record\n  $type: gamedataItem_Record\n  value: 19\nItems.AliasOne: *record\nItems.AliasTwo: *record\n");
            Write(beta, "r6\\tweaks\\beta.yaml", "Items.First:\n  values:\n    - !append Items.B\nItems.Second:\n  values:\n    - !append Items.B\nItems.AliasOne:\n  $type: gamedataItem_Record\n  value: 10\nItems.AliasTwo:\n  $type: gamedataItem_Record\n  value: 10\n");
            DeploymentProvider[] providers = [new("Alpha", alpha), new("Beta", beta)];
            DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
            ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
            TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);

            TweakOperation[] aliasMutations = tweaks.Operations.Where(value => value.Provider == "Alpha" && value.Target is "Items.First.values" or "Items.Second.values").ToArray();
            Assert.HasCount(2, aliasMutations);
            CollectionAssert.AreEqual(AliasUseLines, aliasMutations.Select(value => value.SourceStartLine).ToArray());
            Assert.IsTrue(aliasMutations.All(value => value.Kind == TweakOperationKind.ArrayAppend && value.Value == "Items.A" && value.IsAliasSource && !value.IsCompleteSourceBlock));

            CodeSourceEvidence[] evidence = Build(providers);
            CodeSourceEvidence[] mutationEvidence = evidence.Where(value => value.Provider == "Alpha" && value.Target is "Items.First.values" or "Items.Second.values").ToArray();
            Assert.HasCount(2, mutationEvidence);
            Assert.IsTrue(mutationEvidence.All(value => value.Description == "TweakXL alias source excerpt" && !value.IsCompleteBlock));
            foreach (CodeSourceEvidence item in mutationEvidence)
            {
                CodeSourceDocument document = await CodeSourceReader.ReadAsync(item);
                string excerpt = string.Join('\n', document.Lines[(item.StartLine - 1)..item.EndLine]);
                Assert.AreEqual($"{item.Target}: *ops", excerpt);
                Assert.IsFalse(excerpt.Contains("Items.A", StringComparison.Ordinal));
            }

            CodeSourceEvidence[] recordEvidence = evidence.Where(value => value.Provider == "Alpha" && value.Target is "Items.AliasOne.value" or "Items.AliasTwo.value").ToArray();
            Assert.HasCount(2, recordEvidence);
            CollectionAssert.AreEqual(RecordAliasUseLines, recordEvidence.Select(value => value.StartLine).ToArray());
            Assert.IsTrue(recordEvidence.All(value => value.Description == "TweakXL alias source excerpt" && !value.IsCompleteBlock));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CachedScanRetainsLiveEvidenceButReceiptJsonDoesNotExportIt()
    {
        string root = TempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            Write(root, "r6\\tweaks\\initial.yaml", "Items.Test.value: 1");
            Write(root, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Test\\init.lua", "TweakDB:SetFlat('Items.Test.value', 2)");

            ProfileScanReceipt first = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);
            ProfileScanReceipt cached = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.AreEqual(0, first.Metrics!.CodeCacheHits);
            Assert.AreEqual(1, cached.Metrics!.CodeCacheHits);
            Assert.HasCount(2, first.CodeEvidence.Where(value => value.Surface == ConflictSurface.ScriptAndTweak && value.Target == "Items.Test.value").ToArray());
            CollectionAssert.AreEqual(first.CodeEvidence, cached.CodeEvidence);
            Assert.IsFalse(JsonSerializer.Serialize(cached).Contains("CodeEvidence", StringComparison.Ordinal));
            Assert.IsTrue(cached.CodeEvidence.All(value => value.SourceSha256 == Hash(value.PhysicalPath)));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CarriageReturnOnlyRuntimeEvidenceDisplaysTheWriteLine()
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Alpha\\init.lua", "local unrelated = 1\rTweakDB:SetFlat('Items.Test.value', 1)");
            Write(beta, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Beta\\init.lua", "local unrelated = 2\rTweakDB:SetFlat('Items.Test.value', 2)");
            DeploymentProvider[] providers = [new("Alpha", alpha), new("Beta", beta)];
            DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
            ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
            RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
            SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
            SharedStateWriteFinding[] stateFindings = SharedStateWriteAnalyzer.Analyze(writes);
            LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
            TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
            InteractionFinding[] interactions = InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps, tweaks.Operations, writes);
            CodeSourceEvidence[] evidence = CodeSourceEvidenceBuilder.Build(manifest, inventory, interactions, flows, callbacks, writes, stateFindings, tweaks.Operations);
            CodeSourceEvidence source = evidence.Single(value => value.Surface == ConflictSurface.SharedState && value.Provider == "Alpha");

            CodeSourceDocument document = await CodeSourceReader.ReadAsync(source);

            Assert.AreEqual(2, source.StartLine);
            Assert.AreEqual(2, source.EndLine);
            Assert.AreEqual("TweakDB:SetFlat('Items.Test.value', 1)", document.Lines[source.StartLine - 1]);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    [DataRow("\r")]
    public async Task LegacyFlowEvidenceFindsItsLineWithEverySupportedNewline(string newline)
    {
        string root = TempRoot();
        try
        {
            string alpha = Path.Combine(root, "Alpha");
            string beta = Path.Combine(root, "Beta");
            Write(alpha, "r6\\scripts\\alpha.reds", string.Join(newline, "module Alpha", "@replaceMethod(PlayerPuppet)", "public func Value() -> Int32 { return 1; }", "module Alpha.Tail"));
            Write(beta, "r6\\scripts\\beta.reds", string.Join(newline, "module Beta", "@replaceMethod(PlayerPuppet)", "public func Value() -> Int32 { return 2; }", "module Beta.Tail"));
            DeploymentProvider[] providers = [new("Alpha", alpha), new("Beta", beta)];
            DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
            ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
            RedScriptFlowEvidence[] analyzedFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
            RedScriptFlowEvidence[] legacyFlows = analyzedFlows.Select(value => value with { SourceStartIndex = -1, SourceEndIndex = -1 }).ToArray();
            string target = analyzedFlows.Select(value => value.Target).Distinct(StringComparer.Ordinal).Single();
            InteractionFinding[] interactions = [new(target, InteractionFindingKind.Exclusive, string.Empty, ["Alpha", "Beta"])];
            CodeSourceEvidence[] evidence = CodeSourceEvidenceBuilder.Build(manifest, inventory, interactions, legacyFlows, [], [], [], []);
            CodeSourceEvidence source = evidence.Single(value => value.Provider == "Alpha");

            CodeSourceDocument document = await CodeSourceReader.ReadAsync(source);

            Assert.AreEqual(3, source.StartLine);
            Assert.AreEqual("public func Value() -> Int32 { return 1; }", document.Lines[source.StartLine - 1]);
        }
        finally { Directory.Delete(root, true); }
    }

    private static CodeSourceEvidence[] Build(DeploymentProvider[] providers)
    {
        DeploymentFileManifest manifest = DeploymentFileManifest.Build(providers);
        ModSourceInventory inventory = ModSourceScanner.ScanManifest(manifest, null, null);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
        SharedStateWriteFinding[] stateFindings = SharedStateWriteAnalyzer.Analyze(writes);
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        InteractionFinding[] interactions = InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps, tweaks.Operations, writes);
        return CodeSourceEvidenceBuilder.Build(manifest, inventory, interactions, flows, callbacks, writes, stateFindings, tweaks.Operations);
    }

    private static CodeSourceEvidence Evidence(string path, string sha256, int startLine, int endLine)
        => new(ConflictSurface.ScriptAndTweak, "Target", "Provider", Path.GetFileName(path), path, sha256, startLine, endLine, startLine, endLine, true, "Source");

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-code-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
