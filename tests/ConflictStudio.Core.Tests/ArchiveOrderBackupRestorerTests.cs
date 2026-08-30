using ConflictStudio.Core;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveOrderBackupRestorerTests
{
    [TestMethod]
    public void RestoreReplacesOnlyTheExpectedCurrentOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(root, "modlist.txt");
            string backup = Path.Combine(root, "modlist.txt.bak");
            File.WriteAllText(target, "new");
            File.WriteAllText(backup, "old");
            string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("new")));

            ArchiveOrderBackupRestorer.Restore(backup, target, expected);

            Assert.AreEqual("old", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RollbackRemovesAFirstTimeManagedOrderOnlyWhenWrittenBytesStillMatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string target = Path.Combine(root, "modlist.txt");
            File.WriteAllText(target, "Alpha.archive\r\n");
            string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));

            ArchiveOrderRollback.RestorePrevious(new ArchiveOrderApplyResult(null, true, false, hash), target);

            Assert.IsFalse(File.Exists(target));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
