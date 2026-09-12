using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeContributorOverviewTests
{
    [TestMethod]
    public void IncludesSixProvidersAndSeparatesContextFromSupportingOperations()
    {
        CodeSourceEvidence[] sources = Enumerable.Range(1, 6).Select(index => Source("Mod " + index, index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        CodeFindingParticipant[] participants = sources.Take(2).Select(source => new CodeFindingParticipant(source.OperationId, CodeFindingParticipantRole.ValueDeclaration, "Assigns value", source.OperationKind, source.Provider, source.FilePath, 1, null, source.NormalizedValue, "scalar", [source])).ToArray();
        CodeFindingWitness witness = new("values", CodeFindingWitnessKind.ScalarValue, "Different values", "Different declarations", "Unknown runtime value", null, participants);
        ConflictWorkItem item = new(ConflictSurface.ScriptAndTweak, "Items.Test.value", EvidenceClassification.Review, ConflictWorkState.ReviewWhenRelevant, "", "", null, sources.Select(source => source.Provider).ToArray(), new string('b', 64));

        CodeContributorRow[] rows = CodeContributorOverview.Create(item, sources.Append(Source("Other", "unrelated") with { Target = "Items.Other.value" }).ToArray(), witness);

        Assert.AreEqual(6, rows.Length);
        Assert.AreEqual(2, rows.Count(row => row.SupportsSelectedIssue));
        Assert.AreEqual("Source context", rows.Single(row => row.Provider == "Mod 6").Relationship);
        Assert.AreEqual("6", rows.Single(row => row.Provider == "Mod 6").Value);
    }

    [TestMethod]
    public void PreservesUnavailableParticipantsAndDistinctOccurrences()
    {
        CodeSourceEvidence source = Source("Alpha", "first");
        CodeFindingParticipant missing = new("missing", CodeFindingParticipantRole.ValueDeclaration, "Assigns value", CodeEvidenceOperationKind.TweakScalarAssignment, "Beta", "missing.yaml", 8, null, "2", "scalar", []);
        CodeFindingWitness witness = new("values", CodeFindingWitnessKind.ScalarValue, "Different", "Different", "Unknown", null, [missing]);
        ConflictWorkItem item = new(ConflictSurface.ScriptAndTweak, source.Target, EvidenceClassification.Review, ConflictWorkState.ReviewWhenRelevant, "", "", null, ["Alpha", "Beta"], new string('b', 64));

        CodeContributorRow[] rows = CodeContributorOverview.Create(item, [source, source with { OperationId = "second", FocusStartLine = 2, StartLine = 2, EndLine = 2, FocusEndLine = 2 }], witness);

        Assert.AreEqual(3, rows.Length);
        Assert.IsNull(rows.Single(row => row.Provider == "Beta").Source);
        Assert.AreEqual(8, rows.Single(row => row.Provider == "Beta").Line);
    }

    [TestMethod]
    public void AddedFieldsShowTheirDeclaredType()
    {
        CodeSourceEvidence source = Source("Alpha", "field") with { Target = "PlayerPuppet.extra", OperationKind = CodeEvidenceOperationKind.RedScriptAddField, NormalizedValue = "Bool", ValueType = "field type" };
        ConflictWorkItem item = new(ConflictSurface.ScriptAndTweak, source.Target, EvidenceClassification.Review, ConflictWorkState.ReviewWhenRelevant, "", "", null, ["Alpha"], new string('b', 64));
        Assert.AreEqual("Bool", CodeContributorOverview.Create(item, [source], null).Single().Value);
    }

    private static CodeSourceEvidence Source(string provider, string operation)
        => new(ConflictSurface.ScriptAndTweak, "Items.Test.value", provider, "test.yaml", provider + ".yaml", new string('a', 64), 1, 1, 1, 1, true, "TweakXL property")
        { OperationId = operation, OperationKind = CodeEvidenceOperationKind.TweakScalarAssignment, NormalizedValue = operation };
}
