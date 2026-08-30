using ConflictStudio.App;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
    [TestMethod]
    public void ApplicationAssemblyUsesThePublishedProductVersion()
    {
        Assert.AreEqual("0.1.7", typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
    }

    [TestMethod]
    public void ApplicationAssemblyUsesThePublicExecutableName()
    {
        Assert.AreEqual("ConflictStudio", typeof(MainWindow).Assembly.GetName().Name);
    }

    [TestMethod]
    public void WriteRecordsOperationAndExceptionDetails()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagnosticLog log = new(root);

            log.Write("archive-scan", new InvalidDataException("broken index"));

            string text = File.ReadAllText(Path.Combine(root, "diagnostics.log"));
            StringAssert.Contains(text, "archive-scan");
            StringAssert.Contains(text, "broken index");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadRecentReturnsTheLatestDiagnosticText()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-log-read-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagnosticLog log = new(root);
            log.Write("profile-scan", new InvalidOperationException("thread failure"));

            string text = log.ReadRecent();

            StringAssert.Contains(text, "profile-scan");
            StringAssert.Contains(text, "thread failure");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ConstructorDoesNotTouchTheFileSystem()
    {
        DiagnosticLog log = new("Z:\\missing-drive\\conflict-studio");

        Assert.IsFalse(log.TryWrite("startup", new IOException("unavailable")));
    }

    [TestMethod]
    public void ActionTrailRecordsOutcomeAndRemovesPrivatePaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-action-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagnosticLog log = new(root);

            Assert.IsTrue(log.TryWriteAction("open-provider", "failed", @"Missing C:\private\mods\Alpha"));

            string text = File.ReadAllText(Path.Combine(root, "activity.log"));
            StringAssert.Contains(text, "open-provider");
            StringAssert.Contains(text, "failed");
            StringAssert.Contains(text, "[private path]");
            Assert.IsFalse(text.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PersistentLogsRotateBeforeTheyGrowWithoutBound()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rotation-" + Guid.NewGuid().ToString("N"));
        try
        {
            DiagnosticLog log = new(root, 240);
            for (int index = 0; index < 20; index++) log.TryWriteAction("profile-scan", "completed", new string('x', 40));

            string current = Path.Combine(root, "activity.log");
            string previous = Path.Combine(root, "activity.previous.log");
            Assert.IsTrue(File.Exists(current));
            Assert.IsTrue(File.Exists(previous));
            Assert.IsLessThanOrEqualTo(240, new FileInfo(current).Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PortableReportContainsActionTrailAndRedactsAllPhysicalPaths()
    {
        DiagnosticAction[] actions =
        [
            new(new DateTimeOffset(2026, 8, 29, 16, 0, 0, TimeSpan.Zero), "profile-scan", "completed", "731 archive conflicts"),
            new(new DateTimeOffset(2026, 8, 29, 16, 1, 0, TimeSpan.Zero), "open-provider", "failed", @"Missing D:\ExampleMO2\mods\Alpha")
        ];

        string report = DiagnosticReportBuilder.Build("0.1.0", "Standard", "Scan complete", "archive order: managed", actions, @"C:\Users\person\diagnostics.log failed");

        StringAssert.Contains(report, "WHAT HAPPENED");
        StringAssert.Contains(report, "open-provider · failed");
        StringAssert.Contains(report, "[private path]");
        Assert.IsFalse(report.Contains(@"D:\ExampleMO2", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(report.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SessionViewStaysFocusedOnCurrentActionsAndScanState()
    {
        DiagnosticAction action = new(new DateTimeOffset(2026, 8, 29, 16, 0, 0, TimeSpan.Zero), "profile-scan", "completed", "731 archive conflicts");

        string view = DiagnosticReportBuilder.BuildSessionView("Scan complete", "archive order: managed", [action]);

        StringAssert.Contains(view, "CURRENT SESSION");
        StringAssert.Contains(view, "profile-scan · completed");
        StringAssert.Contains(view, "CURRENT SCAN");
        Assert.IsFalse(view.Contains("APPLICATION ERRORS", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PortableReportIdentifiesTheManagerThatProducedTheProfile()
    {
        string report = DiagnosticReportBuilder.Build("0.1.0", "Standard", "Ready", "No scan", [], "No errors", "Vortex");

        StringAssert.Contains(report, "Manager: Vortex");
    }
}
