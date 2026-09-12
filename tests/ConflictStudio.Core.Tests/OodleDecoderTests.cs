using System.Diagnostics;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class OodleDecoderTests
{
    [TestMethod]
    public void MissingDecoderExportReleasesTheLoadedLibrary()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This test requires Windows module loading.");
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-decoder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "decoder-" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(Path.Combine(Environment.SystemDirectory, "version.dll"), path);
        try
        {
            Assert.IsNull(OodleDecoderSession.TryOpen(path));
            Assert.IsFalse(IsLoaded(path));
        }
        finally
        {
            if (!IsLoaded(path)) Directory.Delete(root, true);
        }
    }

    private static bool IsLoaded(string path)
    {
        using Process process = Process.GetCurrentProcess();
        return process.Modules.Cast<ProcessModule>().Any(module => string.Equals(module.FileName, path, StringComparison.OrdinalIgnoreCase));
    }
}
