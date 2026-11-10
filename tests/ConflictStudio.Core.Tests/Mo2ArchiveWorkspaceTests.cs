using ConflictStudio.Core;
using System.Text;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ArchiveWorkspaceTests
{
    private static readonly string[] SeedOrder = ["Beta.archive", "Alpha.archive"];
    [TestMethod]
    public void ScanSeedsTheWinningOrderWithoutCreatingAProfileOverride()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();

        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);

        CollectionAssert.AreEqual(SeedOrder, state.EditableOrder);
        Assert.AreEqual(fixture.Seed, state.OrderSource);
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order.txt"), state.WriteTarget);
        Assert.IsFalse(File.Exists(state.WriteTarget));
        Assert.IsNull(state.WriteBlockedReason);
        Assert.AreEqual(2, state.ArchiveRows.Length);
        Assert.AreEqual(1, state.ArchiveRows.Single(value => value.Name == "Beta.archive").Winning);
        Mo2ArchiveSelection selection = workspace.Select(state.Token, ["Beta.archive"]);
        Assert.AreEqual(1, selection.TotalSharedResources);
        Assert.AreEqual("Beta.archive", selection.SharedResources.Single().WinnerArchive);
    }

    [TestMethod]
    public void LegacyProfileOrderMigratesToTheCanonicalFileWithoutChangingItsSource()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string legacyPath = Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order", "modlist.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        byte[] original = Encoding.UTF8.GetBytes("Beta.archive\nDisabled.archive\nAlpha.archive\n");
        File.WriteAllBytes(legacyPath, original);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.AreEqual(legacyPath, state.OrderSource);
        Assert.AreEqual(Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order.txt"), state.WriteTarget);
        string[] proposed = ["Alpha.archive", "Beta.archive"];

        workspace.Preview(state.Token, proposed);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, proposed);

        Assert.AreEqual("Alpha.archive\r\nDisabled.archive\r\nBeta.archive\r\n", File.ReadAllText(saved.WriteTarget));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(legacyPath));
        Assert.AreEqual(saved.WriteTarget, saved.OrderSource);
        Assert.IsFalse(Directory.Exists(fixture.BackupDirectory));
        Mo2ArchiveWorkspaceState restored = workspace.Undo(saved.Token);
        Assert.IsFalse(File.Exists(saved.WriteTarget));
        Assert.AreEqual(legacyPath, restored.OrderSource);
        CollectionAssert.AreEqual(SeedOrder, restored.EditableOrder);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(legacyPath));
    }

    [TestMethod]
    public void CanonicalProfileOrderTakesPriorityOverTheLegacyFile()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string legacyPath = Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order", "modlist.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "Beta.archive\nAlpha.archive\n");
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        File.WriteAllText(target, "Alpha.archive\nBeta.archive\n");

        Mo2ArchiveProfile profile = Mo2ArchiveProfileScanner.ScanInstance(fixture.Root, fixture.Profile);

        Assert.AreEqual(target, profile.OrderEvidence!.SourcePath);
        Assert.IsTrue(profile.OrderEvidence.PreserveInactiveArchives);
        string[] expected = ["Alpha.archive", "Beta.archive"];
        CollectionAssert.AreEqual(expected, profile.EffectiveOrder);
        Assert.AreEqual(target, Mo2ArchiveWriteTargetResolver.Resolve(fixture.Root, profile.OrderEvidence).ModlistPath);
    }

    [TestMethod]
    public void UndoMigrationRefusesAChangedLegacySource()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string legacyPath = Path.Combine(Path.GetDirectoryName(fixture.Profile)!, "archive-order", "modlist.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "Beta.archive\nAlpha.archive\n");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        string[] proposed = ["Alpha.archive", "Beta.archive"];
        workspace.Preview(state.Token, proposed);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, proposed);
        File.AppendAllText(legacyPath, "# concurrent edit\n");
        byte[] written = File.ReadAllBytes(saved.WriteTarget);

        Assert.Throws<ProfileInputChangedException>(() => workspace.Undo(saved.Token));

        CollectionAssert.AreEqual(written, File.ReadAllBytes(saved.WriteTarget));
        StringAssert.Contains(File.ReadAllText(legacyPath), "concurrent edit");
    }

    [TestMethod]
    public void SaveCreatesOnlyTheSelectedProfilesOrderAndUndoRestoresTheSeed()
    {
        using ArchiveWorkspaceFixture fixture = new();
        byte[] seedBytes = File.ReadAllBytes(fixture.Seed);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        string[] proposed = ["Alpha.archive", "Beta.archive"];

        Mo2ArchivePreview preview = workspace.Preview(state.Token, proposed);
        Assert.IsTrue(preview.CanApply);
        Assert.IsTrue(preview.WinnerChangesKnown);
        Assert.AreEqual("Alpha.archive", preview.WinnerDeltas.Single().AfterWinner);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, proposed);

        CollectionAssert.AreEqual(proposed, ArchiveOrderText.ArchiveEntries(File.ReadAllLines(saved.WriteTarget)));
        Assert.AreEqual(saved.WriteTarget, saved.OrderSource);
        Assert.IsTrue(saved.CanUndo);
        CollectionAssert.AreEqual(seedBytes, File.ReadAllBytes(fixture.Seed));
        CollectionAssert.AreEqual(SeedOrder, Mo2ArchiveProfileScanner.ScanInstance(fixture.Root, fixture.OtherProfile).EffectiveOrder);
        Assert.IsFalse(File.Exists(Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.OtherProfile)));
        Mo2ArchiveWorkspaceState restored = workspace.Undo(saved.Token);
        Assert.IsFalse(File.Exists(saved.WriteTarget));
        Assert.IsFalse(restored.CanUndo);
        Assert.AreEqual(fixture.Seed, restored.OrderSource);
        CollectionAssert.AreEqual(SeedOrder, restored.EditableOrder);
    }

    [TestMethod]
    public void SavingWithADisabledModPreservesItsArchivePositionWhenReenabled()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        byte[] original = Encoding.UTF8.GetBytes("# profile order\nAlpha.archive\nBeta.archive\nGamma.archive\nbeta.archive\nhelper.archive.xl\n");
        File.WriteAllBytes(target, original);
        ArchiveWorkspaceFixture.WriteArchive(Path.Combine(Path.GetDirectoryName(fixture.Alpha)!, "Gamma.archive"), 'c');
        ArchiveWorkspaceFixture.WriteArchive(Path.Combine(Path.GetDirectoryName(fixture.Alpha)!, "Delta.archive"), 'd');
        File.WriteAllText(fixture.Profile, "+Settings\n-Beta\n+Alpha\n");
        Mo2ArchiveProfile profile = Mo2ArchiveProfileScanner.ScanInstance(fixture.Root, fixture.Profile);
        ArchiveOrderEvidence evidence = PackedArchiveTopology.Compose(profile, new RedmodArchiveProfile([], [], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "No REDmods"), [])).OrderEvidence!;
        Assert.IsTrue(evidence.PreserveInactiveArchives);
        Assert.AreEqual("Beta.archive", evidence.IgnoredEntries.Single());
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        string[] proposed = ["Gamma.archive", "Alpha.archive", "Delta.archive"];

        workspace.Preview(state.Token, proposed);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, proposed);

        Assert.AreEqual("# profile order\r\nGamma.archive\r\nBeta.archive\r\nAlpha.archive\r\nhelper.archive.xl\r\nDelta.archive\r\n", File.ReadAllText(target));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Directory.GetFiles(fixture.BackupDirectory, "*.bak").Single()));
        CollectionAssert.AreEqual(proposed, saved.EditableOrder);
        File.WriteAllText(fixture.Profile, "+Settings\n+Beta\n+Alpha\n");
        Mo2ArchiveWorkspaceState reenabled = workspace.Scan(fixture.Root, fixture.Profile);
        string[] reenabledOrder = ["Gamma.archive", "Beta.archive", "Alpha.archive", "Delta.archive"];
        CollectionAssert.AreEqual(reenabledOrder, reenabled.EditableOrder);
        Assert.IsNull(reenabled.WriteBlockedReason);
    }

    [TestMethod]
    public void AnUnlistedArchiveLeavesWinnerChangesUnknownRatherThanProvingZeroChanges()
    {
        using ArchiveWorkspaceFixture fixture = new();
        File.WriteAllText(fixture.Seed, "Beta.archive\n");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);

        Mo2ArchivePreview preview = workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);

        Assert.IsTrue(preview.CanApply);
        Assert.IsFalse(preview.WinnerChangesKnown);
        Assert.IsEmpty(preview.WinnerDeltas);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        Mo2ArchiveSelection selection = workspace.Select(saved.Token, ["Alpha.archive"]);
        Assert.AreEqual("Alpha.archive", selection.SharedResources.Single().WinnerArchive);
    }

    [TestMethod]
    public void SaveAndUndoPreserveExistingProfileFileBytes()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        byte[] original = Encoding.UTF8.GetBytes("# profile order\nBeta.archive\nAlpha.archive\n");
        File.WriteAllBytes(target, original);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);

        Mo2ArchiveWorkspaceState restored = workspace.Undo(saved.Token);

        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        Assert.AreEqual(target, restored.OrderSource);
        Assert.AreEqual(1, Directory.GetFiles(fixture.BackupDirectory, "*.bak").Length);
    }

    [TestMethod]
    public void SaveRequiresTheExactPreviewedMembership()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));
        Assert.Throws<ArchiveOrderException>(() => workspace.Preview(state.Token, ["Alpha.archive", "Alpha.archive"]));
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, state.EditableOrder));
        Assert.IsFalse(File.Exists(state.WriteTarget));
    }

    [TestMethod]
    public void SavePreservesAConcurrentSeedOrderEdit()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.AppendAllText(fixture.Seed, "# concurrent change\n");

        Assert.Throws<ProfileInputChangedException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        Assert.IsFalse(File.Exists(state.WriteTarget));
        Assert.IsTrue(File.ReadAllText(fixture.Seed).Contains("# concurrent change", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SaveRejectsArchiveChangesWithUnchangedLengthAndTimestamp()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        DateTime timestamp = File.GetLastWriteTimeUtc(fixture.Alpha);
        byte[] bytes = File.ReadAllBytes(fixture.Alpha);
        bytes[^1] ^= 1;
        File.WriteAllBytes(fixture.Alpha, bytes);
        File.SetLastWriteTimeUtc(fixture.Alpha, timestamp);

        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        Assert.IsFalse(File.Exists(state.WriteTarget));
    }

    [TestMethod]
    public void CommitRevalidationPreservesAProfileChangedDuringBackup()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(fixture.Seed, target);
        byte[] original = File.ReadAllBytes(target);
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, () => [], path =>
        {
            if (path.EndsWith(".bak", StringComparison.Ordinal)) File.AppendAllText(fixture.Profile, "# concurrent profile change\n");
            return File.ReadAllBytes(path);
        });
        Mo2ArchiveWorkspace workspace = new(writer, Path.Combine(fixture.Root, "cache"));
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);

        Assert.Throws<ProfileInputChangedException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp").Length);
    }

    [TestMethod]
    public void UndoPreservesAnOrderEditedAfterSave()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.AppendAllText(saved.WriteTarget, "# concurrent order change\n");

        Assert.Throws<ProfileInputChangedException>(() => workspace.Undo(saved.Token));

        Assert.IsTrue(File.ReadAllText(saved.WriteTarget).Contains("# concurrent order change", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SaveRefusesARunningGameAndCancellation()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = new(new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => ["Cyberpunk2077"]), Path.Combine(fixture.Root, "cache"));
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);

        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"], cancellation.Token));
        Assert.IsFalse(File.Exists(state.WriteTarget));
    }

    [TestMethod]
    public void ProfileOrderCannotEscapeTheConfiguredProfileDirectory()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();

        Assert.Throws<ArchiveOrderException>(() => workspace.Scan(fixture.Root, fixture.Seed));
        Assert.Throws<ArchiveOrderException>(() => workspace.Scan(fixture.Root, Path.Combine(fixture.Root, "profiles", "Standard", "archive-order", "modlist.txt")));
    }

    [TestMethod]
    public void SelectionUsesTheSnapshotAndPagesSharedResources()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        using FileStream locked = File.Open(fixture.Alpha, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Mo2ArchiveSelection selected = workspace.Select(state.Token, ["Alpha.archive"], ["Alpha.archive", "Beta.archive"], 0, 1);

        Assert.AreEqual("Alpha.archive", selected.SharedResources.Single().WinnerArchive);
        Assert.AreEqual(1, selected.Limit);
        Assert.Throws<ArchiveOrderException>(() => workspace.Select("old-token", ["Alpha.archive"]));
    }

    [TestMethod]
    public void ProfileOverridePriorityDoesNotChangeWhichModSuppliesAnArchive()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "Alpha.archive\nBeta.archive\n");

        Mo2ArchiveProfile profile = Mo2ArchiveProfileScanner.ScanInstance(fixture.Root, fixture.Profile);

        Assert.AreEqual(target, profile.OrderEvidence!.SourcePath);
        Assert.AreEqual(fixture.Alpha, profile.Archives.Single(value => value.ArchiveName == "Alpha.archive").PhysicalPath);
        Assert.AreEqual(target, Mo2ArchiveWriteTargetResolver.Resolve(fixture.Root, profile.OrderEvidence).ModlistPath);
    }

    [TestMethod]
    public void RedmodArchivesStayVisibleAndOutsideTheEditableOrder()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string redmodRoot = Path.Combine(fixture.Root, "mods", "Alpha", "mods", "Demo");
        string archivePath = Path.Combine(redmodRoot, "archives", "Demo.archive");
        ArchiveWorkspaceFixture.WriteArchive(archivePath, 'c');
        File.WriteAllText(Path.Combine(redmodRoot, "info.json"), "{\"name\":\"Demo\",\"version\":\"1.0\"}");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();

        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);

        Assert.AreEqual(3, state.ArchiveRows.Length);
        Assert.IsFalse(state.ArchiveRows.Single(value => value.Name == "REDmod/Demo/Demo.archive").Editable);
        Assert.AreEqual("REDmod/Demo/Demo.archive", state.EffectiveOrder[^1]);
        Assert.AreEqual(2, state.EditableOrder.Length);
        Assert.Throws<ArchiveOrderException>(() => workspace.Preview(state.Token, state.EffectiveOrder));
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        Assert.AreEqual("REDmod/Demo/Demo.archive", saved.EffectiveOrder[^1]);
        Assert.IsFalse(File.ReadAllText(saved.WriteTarget).Contains("REDmod/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnUnreadableArchiveCannotBeCrossedAndOtherMovesRequireAcknowledgement()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string broken = Path.Combine(Path.GetDirectoryName(fixture.Alpha)!, "Broken.archive");
        File.WriteAllText(broken, "invalid RDAR");
        File.WriteAllText(fixture.Seed, "Beta.archive\nAlpha.archive\nBroken.archive\n");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);

        Mo2ArchivePreview crossing = workspace.Preview(state.Token, ["Broken.archive", "Alpha.archive", "Beta.archive"], true);
        Assert.IsFalse(crossing.CanApply);
        Assert.AreEqual(UnknownArchiveOrderImpact.BlockedCrossing, crossing.UnknownImpact);
        Mo2ArchivePreview unknown = workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive", "Broken.archive"]);
        Assert.IsFalse(unknown.CanApply);
        Assert.AreEqual(UnknownArchiveOrderImpact.RequiresAcknowledgement, unknown.UnknownImpact);
        Assert.IsTrue(workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive", "Broken.archive"], true).CanApply);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive", "Broken.archive"]);
        Assert.AreEqual("Broken.archive", saved.EditableOrder[^1]);
    }

    [TestMethod]
    public void UndoRefusesABackupWhoseContentsWereChanged()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(fixture.Seed, target);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        byte[] written = File.ReadAllBytes(target);
        File.AppendAllText(Directory.GetFiles(fixture.BackupDirectory, "*.bak").Single(), "# backup edited\n");

        Assert.Throws<ArchiveOrderException>(() => workspace.Undo(saved.Token));

        CollectionAssert.AreEqual(written, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void PostSaveContextFailureRestoresThePreviousOrderExactly()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(fixture.Seed, target);
        byte[] original = File.ReadAllBytes(target);
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, () => [], path =>
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (path == target && Encoding.UTF8.GetString(bytes).StartsWith("Alpha.archive", StringComparison.Ordinal)) File.AppendAllText(fixture.Profile, "# profile changed during write verification\n");
            return bytes;
        });
        Mo2ArchiveWorkspace workspace = new(writer, Path.Combine(fixture.Root, "cache"));
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);

        ArchiveOrderException failure = Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        Assert.IsTrue(failure.Message.Contains("previous archive order was restored", StringComparison.Ordinal));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        Assert.Throws<ArchiveOrderException>(() => workspace.Select(state.Token, ["Alpha.archive"]));
    }

    [TestMethod]
    public void SaveRespectsTheExistingOperationLock()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        Directory.CreateDirectory(Path.GetDirectoryName(state.WriteTarget)!);
        using FileStream operation = File.Open(state.WriteTarget + ".conflictstudio.lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        Assert.IsFalse(File.Exists(state.WriteTarget));
    }

    [TestMethod]
    [DataRow("[PluginPersistance]\nChrome%20%26%20Blood%20Archives\\enabled=false\n", true, "Enable Chrome & Blood Archives")]
    [DataRow("[PluginPersistance]\nPython%20Proxy\\enabled=false\n", true, "Enable Python Proxy")]
    [DataRow("[Plugins]\nCyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=true\n", true, "Disable 'enforce archive load order'")]
    [DataRow("", false, "Install Chrome & Blood Archives")]
    public void InactiveMappingFallsBackToTheCuratedOrderAndReenableRestoresTheProfileOrder(string settings, bool installed, string reason)
    {
        using ArchiveWorkspaceFixture fixture = new();
        string plugin = Path.Combine(fixture.Root, "plugins", "chrome_blood_archives", "__init__.py");
        string ini = Path.Combine(fixture.Root, "ModOrganizer.ini");
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        byte[] profileOrder = Encoding.UTF8.GetBytes("Alpha.archive\nBeta.archive\n");
        File.WriteAllBytes(target, profileOrder);
        File.WriteAllText(ini, settings);
        if (!installed) File.Delete(plugin);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();

        Assert.IsFalse(Mo2ProfileArchiveOrder.MappingEnabled(fixture.Root));
        Mo2ArchiveWorkspaceState inactive = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.AreEqual(fixture.Seed, inactive.OrderSource);
        CollectionAssert.AreEqual(SeedOrder, inactive.EditableOrder);
        Assert.IsTrue(inactive.WriteBlockedReason!.StartsWith(reason, StringComparison.Ordinal));
        Assert.IsFalse(workspace.Preview(inactive.Token, ["Alpha.archive", "Beta.archive"]).CanApply);
        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(inactive.Token, ["Alpha.archive", "Beta.archive"]));
        CollectionAssert.AreEqual(profileOrder, File.ReadAllBytes(target));

        File.WriteAllText(plugin, string.Empty);
        File.WriteAllText(ini, "[Plugins]\nCyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=false\n");
        Assert.IsTrue(Mo2ProfileArchiveOrder.MappingEnabled(fixture.Root));
        Mo2ArchiveWorkspaceState enabled = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.AreEqual(target, enabled.OrderSource);
        Assert.IsNull(enabled.WriteBlockedReason);
        Assert.AreEqual("Alpha.archive", enabled.EditableOrder[0]);
        Assert.IsTrue(workspace.Preview(enabled.Token, SeedOrder).CanApply);
    }

    [TestMethod]
    public void PluginPersistenceIsReadOnlyFromItsPublicSettingsGroup()
    {
        using ArchiveWorkspaceFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "ModOrganizer.ini"), "[Other]\nChrome%20%26%20Blood%20Archives\\enabled=false\nPython%20Proxy\\enabled=false\n[PluginPersistance]\nChrome%20%26%20Blood%20Archives\\enabled=true\nPython%20Proxy\\enabled=1\n");

        Assert.IsTrue(Mo2ProfileArchiveOrder.MappingEnabled(fixture.Root));

        File.AppendAllText(Path.Combine(fixture.Root, "ModOrganizer.ini"), "Python%20Proxy\\enabled=0\n");
        Assert.IsFalse(Mo2ProfileArchiveOrder.MappingEnabled(fixture.Root));
    }

    [TestMethod]
    public void UninstallingTheMappingPackageAfterPreviewPreventsTheSave()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.Delete(Path.Combine(fixture.Root, "plugins", "chrome_blood_archives", "__init__.py"));

        Assert.Throws<ArchiveOrderException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));

        Assert.IsFalse(File.Exists(state.WriteTarget));
        CollectionAssert.AreEqual(SeedOrder, Mo2ArchiveProfileScanner.ScanInstance(fixture.Root, fixture.Profile).EffectiveOrder);
    }

    [TestMethod]
    public void OrdinaryWindowAndColumnSettingsChangesAllowSaveAndUndo()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string ini = Path.Combine(fixture.Root, "ModOrganizer.ini");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.WriteAllText(ini, "[General]\nwindowGeometry=@ByteArray(changed)\n[Settings]\ncolumn_widths=100,250,60\n[Plugins]\nCyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=false\n");

        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.AppendAllText(ini, "[Geometry]\nwindowSize=1200,800\n");
        Mo2ArchiveWorkspaceState restored = workspace.Undo(saved.Token);

        CollectionAssert.AreEqual(SeedOrder, restored.EditableOrder);
        Assert.IsFalse(File.Exists(saved.WriteTarget));
    }

    [TestMethod]
    public void CreatingOrRemovingAnIniWithTheSameResolvedContextAllowsSaveAndUndo()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string ini = Path.Combine(fixture.Root, "ModOrganizer.ini");
        File.Delete(ini);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, state.EditableOrder);
        File.WriteAllText(ini, "[General]\nwindowGeometry=@ByteArray(created)\n");

        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, state.EditableOrder);
        File.Delete(ini);
        Mo2ArchiveWorkspaceState restored = workspace.Undo(saved.Token);

        Assert.IsFalse(File.Exists(saved.WriteTarget));
        CollectionAssert.AreEqual(SeedOrder, restored.EditableOrder);
    }

    [TestMethod]
    [DataRow("mod_directory=other-mods")]
    [DataRow("gamePath=other-game")]
    [DataRow("profiles_directory=other-profiles")]
    [DataRow("overwrite_directory=other-overwrite")]
    [DataRow("selected_profile=Other")]
    [DataRow("Cyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=true")]
    public void MeaningfulInstanceContextChangesPreventSaveAndUndo(string setting)
    {
        using ArchiveWorkspaceFixture fixture = new();
        string ini = Path.Combine(fixture.Root, "ModOrganizer.ini");
        string original = File.ReadAllText(ini);
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        workspace.Preview(state.Token, ["Alpha.archive", "Beta.archive"]);
        File.WriteAllText(ini, original + "\n[General]\n" + setting + "\n");

        Assert.Throws<ProfileInputChangedException>(() => workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]));
        Assert.IsFalse(File.Exists(state.WriteTarget));
        File.WriteAllText(ini, original);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(state.Token, ["Alpha.archive", "Beta.archive"]);
        byte[] written = File.ReadAllBytes(saved.WriteTarget);
        File.WriteAllText(ini, original + "\n[General]\n" + setting + "\n");

        Assert.Throws<ProfileInputChangedException>(() => workspace.Undo(saved.Token));
        CollectionAssert.AreEqual(written, File.ReadAllBytes(saved.WriteTarget));
    }

    [TestMethod]
    public void OrderWriteIsNeededForFirstSaveAndNewArchivesWithoutMovingRows()
    {
        using ArchiveWorkspaceFixture fixture = new();
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.IsTrue(state.NeedsOrderWrite);
        Assert.IsTrue(workspace.Preview(state.Token, state.EditableOrder).CanApply);
        Mo2ArchiveWorkspaceState pinned = workspace.Apply(state.Token, state.EditableOrder);
        Assert.IsFalse(pinned.NeedsOrderWrite);
        string newArchive = Path.Combine(Path.GetDirectoryName(fixture.Alpha)!, "New.archive");
        ArchiveWorkspaceFixture.WriteArchive(newArchive, 'n');

        Mo2ArchiveWorkspaceState expanded = workspace.Scan(fixture.Root, fixture.Profile);

        Assert.IsTrue(expanded.NeedsOrderWrite);
        Assert.AreEqual("New.archive", expanded.EditableOrder[^1]);
        Assert.IsTrue(workspace.Preview(expanded.Token, expanded.EditableOrder).CanApply);
        Mo2ArchiveWorkspaceState saved = workspace.Apply(expanded.Token, expanded.EditableOrder);
        Assert.IsFalse(saved.NeedsOrderWrite);
        CollectionAssert.AreEqual(saved.EditableOrder, ArchiveOrderText.ArchiveEntries(File.ReadAllLines(saved.WriteTarget)));
    }

    [TestMethod]
    public void OrderWriteRecognizesDuplicateRepairButNotOnlyInactiveEntries()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string target = Mo2ProfileArchiveOrder.Resolve(fixture.Root, fixture.Profile);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "Beta.archive\nBeta.archive\nAlpha.archive\n");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();
        Mo2ArchiveWorkspaceState duplicate = workspace.Scan(fixture.Root, fixture.Profile);
        Assert.IsTrue(duplicate.NeedsOrderWrite);
        Assert.IsTrue(workspace.Preview(duplicate.Token, duplicate.EditableOrder).CanApply);
        Mo2ArchiveWorkspaceState repaired = workspace.Apply(duplicate.Token, duplicate.EditableOrder);
        Assert.IsFalse(repaired.NeedsOrderWrite);
        File.AppendAllText(target, "Inactive.archive\n");

        Mo2ArchiveWorkspaceState inactive = workspace.Scan(fixture.Root, fixture.Profile);

        Assert.IsFalse(inactive.NeedsOrderWrite);
    }

    [TestMethod]
    public void ArchiveDirectoriesIncludeConfiguredGameOverwriteAndMissingActiveModPaths()
    {
        using ArchiveWorkspaceFixture fixture = new();
        string ini = Path.Combine(fixture.Root, "ModOrganizer.ini");
        File.AppendAllText(ini, "[General]\ngamePath=game\noverwrite_directory=custom-overwrite\n");
        File.AppendAllText(fixture.Profile, "+Missing\n");
        Mo2ArchiveWorkspace workspace = fixture.Workspace();

        Mo2ArchiveWorkspaceState state = workspace.Scan(fixture.Root, fixture.Profile);

        CollectionAssert.AreEquivalent(new[]
        {
            Path.Combine(fixture.Root, "custom-overwrite", "archive", "pc", "mod"),
            Path.Combine(fixture.Root, "game", "archive", "pc", "mod"),
            Path.Combine(fixture.Root, "mods", "Alpha", "archive", "pc", "mod"),
            Path.Combine(fixture.Root, "mods", "Beta", "archive", "pc", "mod"),
            Path.Combine(fixture.Root, "mods", "Settings", "archive", "pc", "mod"),
            Path.Combine(fixture.Root, "mods", "Missing", "archive", "pc", "mod")
        }, state.ArchiveDirectories);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "mods", "Missing")));
        Assert.IsTrue(state.ArchiveRows.All(value => state.ArchiveDirectories.Contains(Path.GetDirectoryName(value.PhysicalPath)!, StringComparer.OrdinalIgnoreCase)));
    }
}

internal sealed class ArchiveWorkspaceFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "conflict-studio-native-archive-" + Guid.NewGuid().ToString("N"));
    public string Profile => Path.Combine(Root, "profiles", "Standard", "modlist.txt");
    public string OtherProfile => Path.Combine(Root, "profiles", "Other", "modlist.txt");
    public string Seed => Path.Combine(Root, "mods", "Settings", "archive", "pc", "mod", "modlist.txt");
    public string Alpha => Path.Combine(Root, "mods", "Alpha", "archive", "pc", "mod", "Alpha.archive");
    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(Profile)!, "archive-order", "backups");

    public ArchiveWorkspaceFixture()
    {
        foreach (string path in new[] { Profile, OtherProfile, Seed, Alpha }) Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(Root, "ModOrganizer.ini"), "[Plugins]\nCyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=false\n");
        string plugin = Path.Combine(Root, "plugins", "chrome_blood_archives", "__init__.py");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        File.WriteAllText(plugin, string.Empty);
        File.WriteAllText(Profile, "+Settings\n+Beta\n+Alpha\n");
        File.WriteAllText(OtherProfile, File.ReadAllText(Profile));
        File.WriteAllText(Seed, "Beta.archive\nAlpha.archive\n");
        WriteArchive(Alpha, 'a');
        WriteArchive(Path.Combine(Root, "mods", "Beta", "archive", "pc", "mod", "Beta.archive"), 'b');
    }

    public Mo2ArchiveWorkspace Workspace() => new(new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, () => []), Path.Combine(Root, "cache"));

    public void Dispose() => Directory.Delete(Root, true);

    public static void WriteArchive(string path, char payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RDAR"));
        writer.Write(12U);
        writer.Write(172UL);
        writer.Write(84U);
        writer.Write(0UL);
        writer.Write(0U);
        writer.Write(256UL);
        writer.Write(0U);
        writer.Write(new byte[128]);
        writer.Write(8U);
        writer.Write(56U);
        writer.Write(0UL);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(42UL);
        writer.Write(0L);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(Enumerable.Repeat((byte)payload, 20).ToArray());
    }
}


