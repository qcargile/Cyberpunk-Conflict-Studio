using System.IO;
using System.Text.Json;
using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ProfileViewStateStoreTests
{
    [TestMethod]
    public void StoreRoundTripsViewStateForTheSelectedProfile()
    {
        string root = TemporaryRoot();
        try
        {
            ProfileViewStateStore store = new(root);
            ProfileViewState expected = State("Standard") with
            {
                CodeSearch = "apogee",
                CodeView = "Reviewed",
                CodeSurface = "ArchiveXl",
                CodeProvider = "Apogee",
                CodeOtherProvider = "Time Control",
                ArchiveModFilter = "sandevistan",
                ArchiveFileFilter = "mesh",
                ShowNonConflictingFiles = true,
                OnlyConflictingArchives = true,
                Columns = [new ProfileColumnState("target", 640, false, "Descending", 1)],
                CodeDetailFraction = 0.72,
                SummaryExpanded = false,
                SelectedTab = 4,
                HistoryReference = "baseline",
                HistoryFilter = "Changed"
            };

            Assert.IsTrue(store.TrySave(expected));

            ProfileViewState actual = store.Load(ModManagerKind.Mo2, "instance", "Standard");
            Assert.AreEqual("Time Control", actual.CodeOtherProvider);
            Assert.AreEqual(expected.ManagerKind, actual.ManagerKind);
            Assert.AreEqual(expected.InstallationId, actual.InstallationId);
            Assert.AreEqual(expected.ProfileName, actual.ProfileName);
            Assert.AreEqual(expected.CodeSearch, actual.CodeSearch);
            Assert.AreEqual(expected.CodeView, actual.CodeView);
            Assert.AreEqual(expected.CodeSurface, actual.CodeSurface);
            Assert.AreEqual(expected.CodeProvider, actual.CodeProvider);
            Assert.AreEqual(expected.ArchiveModFilter, actual.ArchiveModFilter);
            Assert.AreEqual(expected.ArchiveFileFilter, actual.ArchiveFileFilter);
            Assert.AreEqual(expected.ShowNonConflictingFiles, actual.ShowNonConflictingFiles);
            Assert.AreEqual(expected.OnlyConflictingArchives, actual.OnlyConflictingArchives);
            Assert.AreEqual(expected.CodeDetailFraction, actual.CodeDetailFraction);
            Assert.AreEqual(expected.SummaryExpanded, actual.SummaryExpanded);
            Assert.AreEqual(expected.SelectedTab, actual.SelectedTab);
            Assert.AreEqual(expected.HistoryReference, actual.HistoryReference);
            Assert.AreEqual(expected.HistoryFilter, actual.HistoryFilter);
            CollectionAssert.AreEqual(expected.Columns, actual.Columns);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void StoreKeepsEachProfileInItsOwnViewFile()
    {
        string root = TemporaryRoot();
        try
        {
            ProfileViewStateStore store = new(root);
            Assert.IsTrue(store.TrySave(State("Standard") with { CodeSearch = "standard" }));
            Assert.IsTrue(store.TrySave(State("Testing") with { CodeSearch = "testing" }));

            Assert.AreEqual("standard", store.Load(ModManagerKind.Mo2, "instance", "Standard").CodeSearch);
            Assert.AreEqual("testing", store.Load(ModManagerKind.Mo2, "instance", "Testing").CodeSearch);
            Assert.AreEqual(string.Empty, store.Load(ModManagerKind.Mo2, "instance", "Fresh").CodeSearch);
            Assert.AreEqual(2, Directory.EnumerateFiles(Path.Combine(root, "views"), "*.json").Count());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void StoreClampsAndRejectsInvalidPersistedValues()
    {
        string root = TemporaryRoot();
        try
        {
            ProfileViewStateStore store = new(root);
            ProfileViewState identity = State("Standard");
            Assert.IsTrue(store.TrySave(identity));
            string path = Directory.EnumerateFiles(Path.Combine(root, "views"), "*.json").Single();
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                state = new
                {
                    managerKind = "Mo2",
                    installationId = "instance",
                    profileName = "Standard",
                    codeSearch = new string('x', 513),
                    codeView = "Unknown",
                    codeSurface = "Unknown",
                    codeProvider = new string('y', 513),
                    archiveModFilter = new string('z', 513),
                    archiveFileFilter = "file",
                    columns = new[]
                    {
                        new { key = "target", width = 9000, visible = true, sortDirection = "Sideways", sortPriority = 9 },
                        new { key = "unknown", width = 200, visible = true, sortDirection = "Ascending", sortPriority = 1 },
                        new { key = "target", width = 220, visible = false, sortDirection = "Descending", sortPriority = 2 }
                    },
                    codeDetailFraction = 4.0,
                    selectedTab = 99,
                    historyReference = "unknown",
                    historyFilter = "unknown"
                }
            }));

            ProfileViewState loaded = store.Load(ModManagerKind.Mo2, "instance", "Standard");

            Assert.AreEqual(string.Empty, loaded.CodeSearch);
            Assert.AreEqual("Actionable", loaded.CodeView);
            Assert.AreEqual("All", loaded.CodeSurface);
            Assert.AreEqual("All mods", loaded.CodeProvider);
            Assert.AreEqual(string.Empty, loaded.ArchiveModFilter);
            Assert.AreEqual("file", loaded.ArchiveFileFilter);
            Assert.AreEqual(1, loaded.Columns.Length);
            Assert.AreEqual("target", loaded.Columns[0].Key);
            Assert.AreEqual(1600d, loaded.Columns[0].Width);
            Assert.IsNull(loaded.Columns[0].SortDirection);
            Assert.AreEqual(5, loaded.Columns[0].SortPriority);
            Assert.AreEqual(0.85d, loaded.CodeDetailFraction);
            Assert.AreEqual(0, loaded.SelectedTab);
            Assert.AreEqual("previous", loaded.HistoryReference);
            Assert.AreEqual("Changes", loaded.HistoryFilter);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void DeleteResetsOnlyTheRequestedProfile()
    {
        string root = TemporaryRoot();
        try
        {
            ProfileViewStateStore store = new(root);
            Assert.IsTrue(store.TrySave(State("Standard") with { CodeSearch = "standard" }));
            Assert.IsTrue(store.TrySave(State("Testing") with { CodeSearch = "testing" }));

            Assert.IsTrue(store.TryDelete(ModManagerKind.Mo2, "instance", "Standard"));

            Assert.AreEqual(string.Empty, store.Load(ModManagerKind.Mo2, "instance", "Standard").CodeSearch);
            Assert.AreEqual("testing", store.Load(ModManagerKind.Mo2, "instance", "Testing").CodeSearch);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static ProfileViewState State(string profile) => new(ModManagerKind.Mo2, "instance", profile);

    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), "conflict-studio-view-state-" + Guid.NewGuid().ToString("N"));
}
