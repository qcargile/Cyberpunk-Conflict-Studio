using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class PrivatePathRedactorTests
{
    [TestMethod]
    public void RedactKeepsDiagnosticCauseAfterAnUnquotedPath()
    {
        string message = @"Failed to open C:\Users\Quentin\Cyberpunk 2077\archive\pc\mod\broken.archive: Invalid RDAR index.";

        string redacted = PrivatePathRedactor.Redact(message);

        Assert.AreEqual("Failed to open [private path]: Invalid RDAR index.", redacted);
        Assert.IsFalse(redacted.Contains("Quentin", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RedactDoesNotLeakAPathContainingCommaAndSpace()
    {
        string message = @"Could not open C:\Users\Quentin\Mods, Private\secret.reds: access was denied";

        string redacted = PrivatePathRedactor.Redact(message);

        Assert.AreEqual("Could not open [private path]: access was denied", redacted);
        Assert.IsFalse(redacted.Contains("Private", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RedactPrefersPrivacyWhenPeriodDoesNotProveThePathBoundary()
    {
        string message = @"Could not open C:\Users\Quentin\secret.reds. Access denied.";

        string redacted = PrivatePathRedactor.Redact(message);

        Assert.AreEqual("Could not open [private path]", redacted);
    }

    [TestMethod]
    public void RedactDoesNotTreatProseKeywordsInsideAPathAsABoundary()
    {
        string message = @"Could not open C:\Users\Quentin\Mods. Access Control\secret.reds: denied";

        string redacted = PrivatePathRedactor.Redact(message);

        Assert.AreEqual("Could not open [private path]: denied", redacted);
        Assert.IsFalse(redacted.Contains("Access Control", StringComparison.Ordinal));
    }
}
