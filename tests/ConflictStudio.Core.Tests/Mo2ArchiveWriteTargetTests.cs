using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ArchiveWriteTargetTests
{
    [TestMethod]
    public void ResolveUsesTheMO2OverwriteVirtualProviderPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);

            Assert.AreEqual(Path.Combine(root, "overwrite", "archive", "pc", "mod", "modlist.txt"), target.ModlistPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveUpdatesThePhysicalProviderThatOwnsTheActiveOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "mods", "Modlist Settings", "archive", "pc", "mod", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Alpha.archive\n");
            ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.ManagedModlist, "Modlist Settings", path, "managed");

            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root, evidence);

            Assert.AreEqual(Path.GetFullPath(path), target.ModlistPath);
            Assert.AreEqual("Modlist Settings", target.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveUsesOverwriteWhenNoActiveOrderExists()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-no-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "filename order");

            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root, evidence);

            Assert.AreEqual(Path.Combine(root, "overwrite", "archive", "pc", "mod", "modlist.txt"), target.ModlistPath);
            Assert.AreEqual("Overwrite", target.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveBlocksCustomWritesWhenMO2WillRegenerateArchiveOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-enforced-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "Cyberpunk%202077%20Support%20Plugin\\enforce_archive_load_order=true\n");

            Mo2ArchiveWriteTarget target = Mo2ArchiveWriteTargetResolver.Resolve(root);

            Assert.IsNotNull(target.WriteBlockedReason);
            StringAssert.Contains(target.WriteBlockedReason, "enforce archive load order");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexTargetCarriesTheExactScannedContextAndProfileIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            string contextPath = Path.Combine(root, "context.json");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [], [], [], null);

            Mo2ArchiveWriteTarget target = VortexArchiveWriteTargetResolver.Resolve(contextPath, context);

            Assert.IsTrue(string.Equals(context.ContextId, target.ExpectedContextId, StringComparison.Ordinal));
            Assert.IsTrue(string.Equals(context.ProfileId, target.ExpectedProfileId, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexTargetBlocksApplyUntilDeploymentIsCurrent()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-pending-target-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, false, [], [], [], null);

            Mo2ArchiveWriteTarget target = VortexArchiveWriteTargetResolver.Resolve(Path.Combine(root, "context.json"), context);

            Assert.IsNotNull(target.WriteBlockedReason);
            StringAssert.Contains(target.WriteBlockedReason, "Deploy");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
