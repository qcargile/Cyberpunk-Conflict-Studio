using ConflictStudio.App;
using ConflictStudio.Core;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeInteractionWindowTests
{
    [TestMethod]
    public void TechnicalDetailsRetainDeclarativeAndRuntimeEvidence()
    {
        ModSourceInventory inventory = new([], [new("Beta", "runtime.lua", "TweakDB:SetFlat('Items.Test.value', 42)")], [new("Alpha", "initial.yaml", "Items.Test.value: 1")], []);
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [],
            InteractionReportBuilder.Build(inventory), [], [], [], [], [], []);
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        typeof(MainWindow).GetField("_receipt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, receipt);
        string details = (string)typeof(MainWindow).GetMethod("ExactEvidence", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [item])!;

        using JsonDocument json = JsonDocument.Parse(details);
        JsonElement evidence = json.RootElement.GetProperty("interactions")[0].GetProperty("TweakRuntimeEvidence");
        Assert.AreEqual("initial.yaml", evidence.GetProperty("Declarations")[0].GetProperty("FilePath").GetString());
        Assert.AreEqual("runtime.lua", evidence.GetProperty("Writes")[0].GetProperty("FilePath").GetString());
        StringAssert.Contains(evidence.GetProperty("Writes")[0].GetProperty("Evidence").GetString()!, "42");
    }

    [TestMethod]
    public void FixRoundTechnicalDetailsIncludeFieldDeclarationLocationsAndTypes()
    {
        string field = "@addField(PlayerPuppet)\nlet sharedState: Bool;";
        ModSourceInventory inventory = new([new("Alpha", "fields.reds", field + "\n" + field)], [], [], []);
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha"], [], [], [], [],
            InteractionReportBuilder.Build(inventory), [], [], [], [], [], []);
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        typeof(MainWindow).GetField("_receipt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, receipt);
        string details = (string)typeof(MainWindow).GetMethod("ExactEvidence", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [item])!;

        using JsonDocument json = JsonDocument.Parse(details);
        JsonElement declarations = json.RootElement.GetProperty("interactions")[0].GetProperty("DeclarationEvidence");
        Assert.AreEqual(2, declarations.GetArrayLength());
        Assert.AreEqual("fields.reds", declarations[0].GetProperty("FilePath").GetString());
        Assert.AreEqual("Alpha", declarations[0].GetProperty("Provider").GetString());
        Assert.AreEqual("Bool", declarations[0].GetProperty("Type").GetString());
        Assert.AreEqual(2, declarations[0].GetProperty("Line").GetInt32());
        Assert.AreEqual(4, declarations[1].GetProperty("Line").GetInt32());
    }

    [TestMethod]
    public void ReviewRationaleRetainsTheSelectedOutcomeAndNotes()
    {
        string rationale = CodeCaseWorkspace.ReviewRationale("Compatible in this profile", "Both wrappers continue.");

        Assert.AreEqual("Compatible in this profile: Both wrappers continue.", rationale);
        Assert.AreEqual("Both wrappers continue.", CodeCaseWorkspace.ReviewNotes(rationale, "Compatible in this profile"));
    }

    [TestMethod]
    public void ReviewNotesRejectsAnUnrelatedOutcome()
    {
        Assert.AreEqual(string.Empty, CodeCaseWorkspace.ReviewNotes("Compatible in this profile: Both wrappers continue.", "Needs runtime check"));
    }
}
