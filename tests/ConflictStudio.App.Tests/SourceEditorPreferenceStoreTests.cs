using ConflictStudio.App;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class SourceEditorPreferenceStoreTests
{
    [TestMethod]
    public void StoreRoundTripsAnExplicitKnownEditor()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            string executable = Path.Combine(root, "Editors & Tools", "Code.exe");
            SourceEditorPreferenceStore store = new(root);
            SourceEditorPreference expected = new(SourceEditorKind.VisualStudioCode, executable);

            Assert.IsTrue(store.TrySave(expected));

            Assert.AreEqual(expected, store.Load());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InvalidSettingsRecoverToNormalOpen()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "source-editor.json"), "{\"schemaVersion\":1,\"preference\":{\"kind\":\"ShellTemplate\",\"executablePath\":\"cmd.exe /c {file}\"}}");

            SourceEditorPreference preference = new SourceEditorPreferenceStore(root).Load();

            Assert.AreEqual(SourceEditorKind.NormalOpen, preference.Kind);
            Assert.IsNull(preference.ExecutablePath);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
