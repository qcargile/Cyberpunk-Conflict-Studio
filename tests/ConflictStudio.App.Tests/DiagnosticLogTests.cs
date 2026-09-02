using ConflictStudio.App;
using System.Diagnostics;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
    [TestMethod]
    public void CorePublisherFailureStopsBeforePublishAndPackageCreation()
        => AssertPublisherStopsBeforePublishAndPackageCreation("tests\\ConflictStudio.Core.Tests\\ConflictStudio.Core.Tests.csproj", "Core regression tests failed.", 1);

    [TestMethod]
    public void AppPublisherFailureStopsBeforePublishAndPackageCreation()
        => AssertPublisherStopsBeforePublishAndPackageCreation("tests\\ConflictStudio.App.Tests\\ConflictStudio.App.Tests.csproj", "App regression tests failed.", 2);

    [TestMethod]
    public void PublisherRunnerOutputPreservesBothTestSuccessesUntilRestoreFails()
        => AssertPublisherStopsBeforePublishAndPackageCreation(string.Empty, "dotnet restore failed.", 3);

    private static void AssertPublisherStopsBeforePublishAndPackageCreation(string failedProject, string expectedFailure, int expectedCommandCount)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-publisher-gate-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(Path.GetTempPath(), "conflict-studio-publisher-output-" + Guid.NewGuid().ToString("N"));
        string harness = Path.Combine(Path.GetTempPath(), "conflict-studio-publisher-harness-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "release"));
            Directory.CreateDirectory(Path.Combine(root, "src", "ConflictStudio.App"));
            Directory.CreateDirectory(Path.Combine(root, "tests", "ConflictStudio.Core.Tests"));
            Directory.CreateDirectory(Path.Combine(root, "tests", "ConflictStudio.App.Tests"));
            File.WriteAllText(Path.Combine(root, "release", "0.4.0.json"), "{}");
            File.WriteAllText(Path.Combine(root, "src", "ConflictStudio.App", "ConflictStudio.App.csproj"), string.Empty);
            File.WriteAllText(Path.Combine(root, "tests", "ConflictStudio.Core.Tests", "ConflictStudio.Core.Tests.csproj"), string.Empty);
            File.WriteAllText(Path.Combine(root, "tests", "ConflictStudio.App.Tests", "ConflictStudio.App.Tests.csproj"), string.Empty);
            Run("git", $"init -q \"{root}\"");
            Run("git", $"-C \"{root}\" config user.email test@example.invalid");
            Run("git", $"-C \"{root}\" config user.name Test");
            Run("git", $"-C \"{root}\" add -A");
            Run("git", $"-C \"{root}\" commit -q -m fixture");
            File.WriteAllText(harness, $$"""
$scriptPath = '{{Path.Combine(RepositoryRoot(), "scripts", "publish-win-x64.ps1")}}'
$repositoryRoot = '{{root}}'
$outputRoot = '{{output}}'
$failedProject = '{{failedProject}}'
if ($failedProject.Length -gt 0) { $failedProject = Join-Path $repositoryRoot $failedProject }
$expectedFailure = '{{expectedFailure}}'
$expectedCommandCount = {{expectedCommandCount}}
$global:publisherCommands = @()
$runner = {
    param([string]$command, [string]$arguments)
    $global:publisherCommands += $command + '|' + $arguments
    Write-Output 'diagnostic command output'
    if ($failedProject.Length -gt 0 -and $arguments.Split("`n") -contains $failedProject) { return 1 }
    if ($failedProject.Length -eq 0 -and $arguments.Split("`n") -contains 'restore') { return 1 }
    return 0
}
try {
    & $scriptPath -RepositoryRoot $repositoryRoot -OutputRoot $outputRoot -CommandRunner $runner
    exit 9
}
catch {
    if ($_.Exception.Message -ne $expectedFailure) { Write-Error $_.Exception.Message; exit 7 }
    if ($global:publisherCommands.Count -ne $expectedCommandCount) { exit 8 }
    if ($global:publisherCommands -match '^dotnet\|publish') { exit 7 }
    if (Test-Path (Join-Path $outputRoot '0.4.0\\win-x64')) { exit 6 }
    exit 0
}
""");

            (int exitCode, string error) = RunWithError("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{harness}\"");
            Assert.AreEqual(0, exitCode, error);
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(output);
            if (File.Exists(harness)) File.Delete(harness);
        }
    }

    [TestMethod]
    public void ApplicationAssemblyUsesThePublishedProductVersion()
    {
        Assert.AreEqual("0.4.0", typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "publish-win-x64.ps1"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Conflict Studio repository root was not found.");
    }

    private static int Run(string fileName, string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static (int ExitCode, string Error) RunWithError(string fileName, string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true })!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
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
