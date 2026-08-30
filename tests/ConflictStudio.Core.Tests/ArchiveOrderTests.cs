using ConflictStudio.Core;
using System.Security.Cryptography;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveOrderTests
{
    [TestMethod]
    public void ScanUsesFilenameOrderWhenModlistDoesNotExist()
    {
        using ArchiveDirectory directory = new();
        directory.Write("zeta.archive", "zeta");
        directory.Write("Alpha.archive", "alpha");

        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);

        CollectionAssert.AreEqual(Alphabetical, observation.EffectiveOrder);
        Assert.IsNull(observation.OrderFileSha256);
    }

    [TestMethod]
    public void ScanUsesACompleteModlistOrder()
    {
        using ArchiveDirectory directory = new();
        directory.Write("zeta.archive", "zeta");
        directory.Write("Alpha.archive", "alpha");
        directory.Write("modlist.txt", "zeta.archive\r\nAlpha.archive\r\n");

        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);

        CollectionAssert.AreEqual(Explicit, observation.EffectiveOrder);
        Assert.IsNotNull(observation.OrderFileSha256);
    }

    [TestMethod]
    public void PreviewRejectsAnOrderThatOmitsAnArchive()
    {
        ArchiveOrderObservation observation = new("standard", @"C:\\fixture", null, [Fingerprint("Alpha.archive", 'a'), Fingerprint("zeta.archive", 'b')], Alphabetical);

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => ArchiveOrderPlanner.CreatePreview(observation, ["zeta.archive"]));

        StringAssert.Contains(exception.Message, "exactly once");
    }

    [TestMethod]
    public void ApplyBacksUpAndReplacesAnUnchangedOrderFile()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        directory.Write("modlist.txt", "Alpha.archive\r\nzeta.archive\r\n");
        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);

        ArchiveOrderApplyResult result = new ArchiveOrderWriter(() => new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), NoProcesses).Apply(preview);

        Assert.IsTrue(result.Verified);
        Assert.IsTrue(File.Exists(result.BackupPath!));
        Assert.AreEqual("zeta.archive\r\nAlpha.archive\r\n", File.ReadAllText(System.IO.Path.Combine(directory.Path, "modlist.txt")));
    }

    [TestMethod]
    public void ApplyReordersArchiveSlotsWithoutRemovingManagerOwnedLines()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        directory.Write("modlist.txt", "Alpha.archive\r\nshared.archive.xl\r\nzeta.archive\r\n");
        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);

        new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, NoProcesses).Apply(preview);

        CollectionAssert.AreEqual(Alphabetical, observation.EffectiveOrder);
        Assert.AreEqual("zeta.archive\r\nshared.archive.xl\r\nAlpha.archive\r\n", File.ReadAllText(Path.Combine(directory.Path, "modlist.txt")));
    }

    [TestMethod]
    public void ManagedObservationIgnoresNonArchiveManagerLines()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        directory.Write("modlist.txt", "zeta.archive\r\nhelper.archive.xl\r\nAlpha.archive\r\n");
        string alphaPath = Path.Combine(directory.Path, "Alpha.archive");
        string zetaPath = Path.Combine(directory.Path, "zeta.archive");
        string orderPath = Path.Combine(directory.Path, "modlist.txt");
        Mo2ArchiveProfile profile = new("Standard", "profile.txt", [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, Fingerprint("Alpha.archive", 'a').Sha256), new Mo2Archive("Zeta", "zeta.archive", zetaPath, 4, Fingerprint("zeta.archive", 'b').Sha256)], Alphabetical);

        ArchiveOrderObservation observation = ManagedArchiveOrderObserver.Observe(profile, new Mo2ArchiveWriteTarget(orderPath));

        CollectionAssert.AreEqual(Explicit, observation.EffectiveOrder);
    }

    [TestMethod]
    public void ManagedApplyRemovesStaleArchivesAndPreservesManagerOwnedLines()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        directory.Write("modlist.txt", "Alpha.archive\r\nstale.archive\r\nhelper.archive.xl\r\nzeta.archive\r\n");
        string alphaPath = Path.Combine(directory.Path, "Alpha.archive");
        string zetaPath = Path.Combine(directory.Path, "zeta.archive");
        string orderPath = Path.Combine(directory.Path, "modlist.txt");
        Mo2ArchiveProfile profile = new("Standard", "profile.txt", [new Mo2Archive("Alpha", "Alpha.archive", alphaPath, 5, Fingerprint("Alpha.archive", 'a').Sha256), new Mo2Archive("Zeta", "zeta.archive", zetaPath, 4, Fingerprint("zeta.archive", 'b').Sha256)], Alphabetical);
        ArchiveOrderObservation observation = ManagedArchiveOrderObserver.Observe(profile, new Mo2ArchiveWriteTarget(orderPath));
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);

        new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, NoProcesses).Apply(preview);

        Assert.AreEqual("zeta.archive\r\nAlpha.archive\r\nhelper.archive.xl\r\n", File.ReadAllText(orderPath));
    }

    [TestMethod]
    public void ApplyCreatesAMissingManagedTargetDirectory()
    {
        using ArchiveDirectory directory = new();
        string targetDirectory = System.IO.Path.Combine(directory.Path, "overwrite", "archive", "pc", "mod");
        ArchiveOrderObservation observation = new("standard", targetDirectory, null, [Fingerprint("Alpha.archive", 'a'), Fingerprint("zeta.archive", 'b')], Alphabetical);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);

        ArchiveOrderApplyResult result = new ArchiveOrderWriter(() => new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), NoProcesses).Apply(preview);

        Assert.IsTrue(result.Verified);
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(targetDirectory, "modlist.txt")));
    }

    [TestMethod]
    public void ApplyRejectsWhileTheGameIsRunning()
    {
        using ArchiveDirectory directory = new();
        ArchiveOrderObservation observation = new("standard", directory.Path, null, [Fingerprint("Alpha.archive", 'a'), Fingerprint("zeta.archive", 'b')], Alphabetical);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, () => ["Cyberpunk2077"]);

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => writer.Apply(preview));

        StringAssert.Contains(exception.Message, "running");
    }

    [TestMethod]
    public void ApplyAllowsModOrganizerToRemainOpen()
    {
        using ArchiveDirectory directory = new();
        ArchiveOrderObservation observation = new("standard", directory.Path, null, [Fingerprint("Alpha.archive", 'a'), Fingerprint("zeta.archive", 'b')], Alphabetical);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, () => ["ModOrganizer"]);

        ArchiveOrderApplyResult result = writer.Apply(preview);

        Assert.IsTrue(result.Verified);
        Assert.AreEqual("zeta.archive\r\nAlpha.archive\r\n", File.ReadAllText(Path.Combine(directory.Path, "modlist.txt")));
    }

    [TestMethod]
    public void UndoRejectsWhileTheGameIsRunning()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        directory.Write("modlist.txt", "Alpha.archive\r\nzeta.archive\r\n");
        bool gameRunning = false;
        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, () => gameRunning ? ["Cyberpunk2077"] : []);
        ArchiveOrderApplyResult result = writer.Apply(preview);
        gameRunning = true;

        Assert.ThrowsExactly<ArchiveOrderException>(() => writer.RestorePrevious(result, Path.Combine(directory.Path, "modlist.txt")));
        Assert.AreEqual("zeta.archive\r\nAlpha.archive\r\n", File.ReadAllText(Path.Combine(directory.Path, "modlist.txt")));
    }

    [TestMethod]
    public void UndoRejectsWhileAnotherProcessOwnsTheOrderLock()
    {
        using ArchiveDirectory directory = new();
        string orderPath = Path.Combine(directory.Path, "modlist.txt");
        directory.Write("modlist.txt", "zeta.archive\r\nAlpha.archive\r\n");
        string backupPath = orderPath + ".bak";
        File.WriteAllText(backupPath, "Alpha.archive\r\nzeta.archive\r\n");
        ArchiveOrderApplyResult result = new(backupPath, true, true, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(orderPath))));
        using FileStream operationLock = new(orderPath + ".conflictstudio.lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

        Assert.ThrowsExactly<ArchiveOrderException>(() => new ArchiveOrderWriter(() => DateTimeOffset.UtcNow, NoProcesses).RestorePrevious(result, orderPath));
        Assert.AreEqual("zeta.archive\r\nAlpha.archive\r\n", File.ReadAllText(orderPath));
    }

    [TestMethod]
    public void FailedPostWriteVerificationRestoresThePreviousBytes()
    {
        using ArchiveDirectory directory = new();
        directory.Write("Alpha.archive", "alpha");
        directory.Write("zeta.archive", "zeta");
        string orderPath = Path.Combine(directory.Path, "modlist.txt");
        directory.Write("modlist.txt", "Alpha.archive\r\nzeta.archive\r\n");
        byte[] before = File.ReadAllBytes(orderPath);
        ArchiveOrderObservation observation = ArchiveOrderScanner.Scan("standard", directory.Path);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);
        int reads = 0;
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, NoProcesses, path => ++reads >= 2 ? [0xFF] : File.ReadAllBytes(path));

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => writer.Apply(preview));

        StringAssert.Contains(exception.Message, "restored");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(orderPath));
    }

    [TestMethod]
    public void ApplyRejectsWhenTheArchiveInventoryChangedAfterPreview()
    {
        using ArchiveDirectory directory = new();
        ArchiveFingerprint[] observed = [Fingerprint("Alpha.archive", 'a'), Fingerprint("zeta.archive", 'b')];
        ArchiveOrderObservation observation = new("standard", directory.Path, null, observed, Alphabetical);
        ArchiveOrderPreview preview = ArchiveOrderPlanner.CreatePreview(observation, Explicit);
        ArchiveFingerprint[] changed = [Fingerprint("Alpha.archive", 'c'), Fingerprint("zeta.archive", 'b')];
        ArchiveOrderWriter writer = new(() => DateTimeOffset.UtcNow, NoProcesses);

        ArchiveOrderException exception = Assert.ThrowsExactly<ArchiveOrderException>(() => writer.Apply(preview, changed));

        StringAssert.Contains(exception.Message, "inventory changed");
    }

    private static readonly string[] Alphabetical = ["Alpha.archive", "zeta.archive"];
    private static readonly string[] Explicit = ["zeta.archive", "Alpha.archive"];

    private static IReadOnlyList<string> NoProcesses() => [];

    private static ArchiveFingerprint Fingerprint(string name, char hash) => new(name, 1, new string(hash, 64));

    private sealed class ArchiveDirectory : IDisposable
    {
        public ArchiveDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "conflict-studio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string name, string contents) => File.WriteAllText(System.IO.Path.Combine(Path, name), contents);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
