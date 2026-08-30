using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ProfileReaderTests
{
    private static readonly string[] ExpectedProviders = ["Alpha", "Gamma"];

    [TestMethod]
    public void ReadActiveProvidersUsesOnlyEnabledProfileEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "+Alpha\r\n-Beta\r\n+Gamma\r\n");

            string[] providers = Mo2ProfileReader.ReadActiveProviders(path);

            CollectionAssert.AreEqual(ExpectedProviders, providers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadActiveProviderEntriesUsesMo2DisplayedPriority()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-priority-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "+High\r\n-Disabled\r\n+Low\r\n");

            Mo2ActiveProvider[] providers = Mo2ProfileReader.ReadActiveProviderEntries(path);

            Assert.AreEqual(2, providers.Single(value => value.Name == "High").Priority);
            Assert.AreEqual(0, providers.Single(value => value.Name == "Low").Priority);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
