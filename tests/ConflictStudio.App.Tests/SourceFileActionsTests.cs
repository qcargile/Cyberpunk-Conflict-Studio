using ConflictStudio.App;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class SourceFileActionsTests
{
    [TestMethod]
    [DataRow(SourceEditorKind.VisualStudioCode, "--goto", @"C:\Editors & Tools\Code.exe")]
    [DataRow(SourceEditorKind.NotepadPlusPlus, "-n37", @"C:\Editors & Tools\notepad++.exe")]
    public void OpenAtLinePassesPathAndLineAsSeparateArguments(SourceEditorKind kind, string lineArgument, string executable)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-args-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "A&B (test) [copy] $source's.reds");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "source");
            RecordingLauncher launcher = new();

            SourceEditorOpenResult result = SourceFileActions.OpenAtLine(new SourceFileLocation("Provider", "source.reds", path), 37, new SourceEditorPreference(kind, executable), launcher);

            Assert.IsTrue(result.OpenedAtLine);
            Assert.IsFalse(result.UsedFallback);
            Assert.AreEqual(executable, launcher.Starts.Single().FileName);
            Assert.IsFalse(launcher.Starts.Single().UseShellExecute);
            if (kind == SourceEditorKind.VisualStudioCode)
                CollectionAssert.AreEqual(new[] { lineArgument, path + ":37" }, launcher.Starts.Single().ArgumentList.ToArray());
            else CollectionAssert.AreEqual(new[] { lineArgument, path }, launcher.Starts.Single().ArgumentList.ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void MissingConfiguredEditorFallsBackToNormalOpenAndReportsLostLineSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-fallback-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "source file.reds");
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "source");
            RecordingLauncher launcher = new() { FailFirst = true };

            SourceEditorOpenResult result = SourceFileActions.OpenAtLine(new SourceFileLocation("Provider", "source.reds", path), 8, new SourceEditorPreference(SourceEditorKind.VisualStudioCode, @"C:\Missing\Code.exe"), launcher);

            Assert.IsFalse(result.OpenedAtLine);
            Assert.IsTrue(result.UsedFallback);
            Assert.AreEqual(2, launcher.Starts.Count);
            Assert.IsTrue(launcher.Starts[1].UseShellExecute);
            Assert.AreEqual("edit", launcher.Starts[1].Verb);
            StringAssert.Contains(result.Status, "line 8 was not selected");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void OpenAtLineRejectsInvalidLineBeforeStartingAProcess()
    {
        RecordingLauncher launcher = new();
        SourceFileLocation file = new("Provider", "source.reds", "missing.reds");

        Assert.Throws<ArgumentOutOfRangeException>(() => SourceFileActions.OpenAtLine(file, 0, new SourceEditorPreference(SourceEditorKind.NormalOpen), launcher));
        Assert.IsEmpty(launcher.Starts);
    }

    private sealed class RecordingLauncher : ISourceEditorProcessLauncher
    {
        public List<ProcessStartInfo> Starts { get; } = [];
        public bool FailFirst { get; init; }

        public void Start(ProcessStartInfo startInfo)
        {
            ProcessStartInfo snapshot = new(startInfo.FileName) { UseShellExecute = startInfo.UseShellExecute, Verb = startInfo.Verb };
            foreach (string argument in startInfo.ArgumentList) snapshot.ArgumentList.Add(argument);
            Starts.Add(snapshot);
            if (FailFirst && Starts.Count == 1) throw new Win32Exception(2, "Editor not found");
        }
    }
}
