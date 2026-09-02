using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2InstancePathResolverTests
{
    [TestMethod]
    public void ResolveHonorsConfiguredInstanceDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Standard Profile)\nbase_directory=instance\nmod_directory=G:\\Shared MO2 Mods\nprofiles_directory=%BASE_DIR%\\custom-profiles\noverwrite_directory=%BASE_DIR%\\custom-overwrite\ngamePath=@ByteArray(game)\n");

            Mo2InstancePaths paths = Mo2InstancePathResolver.Resolve(root);

            Assert.AreEqual(Path.GetFullPath("G:\\Shared MO2 Mods"), paths.ModsRoot);
            Assert.AreEqual(Path.Combine(root, "instance", "custom-profiles"), paths.ProfilesRoot);
            Assert.AreEqual(Path.Combine(root, "instance", "custom-overwrite"), paths.OverwriteRoot);
            Assert.AreEqual(Path.Combine(root, "instance", "game"), paths.GameRoot);
            Assert.AreEqual("Standard Profile", paths.SelectedProfile);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveAcceptsLegacyPluralModsDirectoryKey()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-legacy-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "mods_directory=legacy-mods\n");

            Mo2InstancePaths paths = Mo2InstancePathResolver.Resolve(root);

            Assert.AreEqual(Path.Combine(root, "legacy-mods"), paths.ModsRoot);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LaunchContextFindsPortableInstanceFromWorkingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            string working = Path.Combine(root, "tools", "Conflict Studio");
            Directory.CreateDirectory(working);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Standard)\n");

            Mo2LaunchContext? context = Mo2LaunchContextResolver.TryResolve(working, Path.Combine("C:\\", "external", "ConflictStudio.exe"));

            Assert.IsNotNull(context);
            Assert.AreEqual(Path.GetFullPath(root), context.Root);
            Assert.AreEqual("Standard", context.SelectedProfile);
            Assert.AreEqual(Mo2LaunchEvidence.WorkingDirectory, context.Evidence);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LaunchContextFindsInstanceWhenExecutableIsInstalledAsATool()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-tool-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            string executable = Path.Combine(root, "tools", "Conflict Studio", "ConflictStudio.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Immersive)\n");

            Mo2LaunchContext? context = Mo2LaunchContextResolver.TryResolve(Path.Combine("C:\\", "game", "archive", "pc", "mod"), executable);

            Assert.IsNotNull(context);
            Assert.AreEqual(Path.GetFullPath(root), context.Root);
            Assert.AreEqual("Immersive", context.SelectedProfile);
            Assert.AreEqual(Mo2LaunchEvidence.ExecutableDirectory, context.Evidence);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LaunchContextDoesNotPretendAGameWorkingDirectoryIdentifiesMO2()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-game-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            Mo2LaunchContext? context = Mo2LaunchContextResolver.TryResolve(root, Path.Combine(root, "ConflictStudio.exe"));

            Assert.IsNull(context);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveReadsNativeArchiveOrderEnforcementSetting()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-enforcement-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "Cyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=true\n");

            Mo2InstancePaths paths = Mo2InstancePathResolver.Resolve(root);

            Assert.IsTrue(paths.EnforcesArchiveOrder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
