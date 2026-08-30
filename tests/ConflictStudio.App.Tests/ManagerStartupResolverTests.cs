using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ManagerStartupResolverTests
{
    [TestMethod]
    public void ParseAcceptsMO2RootAndCurrentProfileArguments()
    {
        ApplicationLaunchOptions options = ApplicationLaunchOptions.Parse(["ConflictStudio.exe", "--manager", "mo2", "--root", "D:\\MO2", "--profile", "current"]);

        Assert.AreEqual("mo2", options.Manager);
        Assert.AreEqual("D:\\MO2", options.Root);
        Assert.AreEqual("current", options.Profile);
    }

    [TestMethod]
    public void ResolveUsesDetectedSelectedProfileBeforeSavedProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Current"));
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Old"));
            File.WriteAllText(Path.Combine(root, "profiles", "Current", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles", "Old", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Current)\n");
            WorkspacePreference preference = new(root, "Old");

            ManagerStartupSelection? selection = ManagerStartupResolver.ResolveMo2(ApplicationLaunchOptions.Parse(["app.exe"]), root, Path.Combine(root, "tools", "app.exe"), preference);

            Assert.IsNotNull(selection);
            Assert.AreEqual(Path.GetFullPath(root), selection.Root);
            Assert.AreEqual("Current", selection.ProfileName);
            Assert.AreEqual(Mo2LaunchEvidence.WorkingDirectory, selection.Evidence);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveUsesExplicitNamedProfileInsteadOfCurrentProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-startup-explicit-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Current"));
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Authoring"));
            File.WriteAllText(Path.Combine(root, "profiles", "Current", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles", "Authoring", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Current)\n");
            ApplicationLaunchOptions options = ApplicationLaunchOptions.Parse(["app.exe", "--manager", "mo2", "--root", root, "--profile", "Authoring"]);

            ManagerStartupSelection? selection = ManagerStartupResolver.ResolveMo2(options, "C:\\game", "C:\\tool\\app.exe", null);

            Assert.IsNotNull(selection);
            Assert.AreEqual("Authoring", selection.ProfileName);
            Assert.AreEqual(Mo2LaunchEvidence.ExplicitRoot, selection.Evidence);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveVortexUsesExplicitBridgeContextRegardlessOfExecutableLocation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            string contextPath = Path.Combine(root, "context.json");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Vortex Profile", game, staging, true, [], [], [], null);
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
            ApplicationLaunchOptions options = ApplicationLaunchOptions.Parse(["app.exe", "--manager", "vortex", "--vortex-context", contextPath]);

            ManagerStartupSelection? selection = ManagerStartupResolver.ResolveVortex(options, Path.Combine(root, "missing.json"));

            Assert.IsNotNull(selection);
            Assert.AreEqual(ModManagerKind.Vortex, selection.ManagerKind);
            Assert.AreEqual("Vortex Profile", selection.ProfileName);
            Assert.AreEqual(Path.GetFullPath(contextPath), selection.ContextPath);
            Assert.AreEqual(Path.GetFullPath(game), selection.Root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveManualUsesAnExplicitCyberpunkInstallation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            ApplicationLaunchOptions options = ApplicationLaunchOptions.Parse(["app.exe", "--manager", "manual", "--root", root]);

            ManagerStartupSelection? selection = ManagerStartupResolver.ResolveManual(options, null);

            Assert.IsNotNull(selection);
            Assert.AreEqual(ModManagerKind.Manual, selection.ManagerKind);
            Assert.AreEqual(Path.GetFullPath(root), selection.Root);
            Assert.AreEqual("Deployed game", selection.ProfileName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
