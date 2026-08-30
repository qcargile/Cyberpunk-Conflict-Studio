using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileInputGuardTests
{
    [TestMethod]
    public void RequireUnchangedRejectsAProfileChangedDuringScan()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-input-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "+Alpha\n");
            ProfileInputSnapshot snapshot = ProfileInputGuard.Capture(path);
            File.WriteAllText(path, "+Beta\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileInputGuard.RequireUnchanged(snapshot));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RequireStillAbsentRejectsAnOrderSourceCreatedDuringScan()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-order-appeared-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Assert.IsFalse(File.Exists(path));
            File.WriteAllText(path, "Alpha.archive\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileInputGuard.RequireStillAbsent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
