using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOverviewProjectionTests
{
    private static readonly string[] ExpectedCombinedOrder = ["B.archive", "A.archive", "REDmod/Example/content.archive"];

    [TestMethod]
    public void CombinedRailKeepsProposedLegacyOrderBeforeFixedRedmods()
    {
        string[] combined = ArchiveOverviewProjection.ComposeCombinedOrder(
            ["B.archive", "A.archive"],
            ["A.archive", "B.archive", "REDmod/Example/content.archive"]);

        CollectionAssert.AreEqual(ExpectedCombinedOrder, combined);
    }

    [TestMethod]
    public void WinningSelectionMarksOnlyTheArchiveItOverridesGreen()
    {
        ResourceProvider[] resources = Shared("Alpha.archive", "Beta.archive");

        ArchiveOverviewEntry[] entries = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive", "Unrelated.archive"],
            ["Alpha.archive", "Beta.archive", "Unrelated.archive"],
            resources,
            ["Alpha.archive"],
            ArchiveOrderProblemLane.None);

        ArchiveOverviewEntry related = entries.Single(value => value.ArchiveName == "Beta.archive");
        Assert.IsTrue(related.SelectedWins);
        Assert.IsFalse(related.SelectedLoses);
        Assert.IsFalse(entries.Any(value => value.ArchiveName == "Unrelated.archive"));
    }

    [TestMethod]
    public void NoSelectionProducesNoConflictMarkers()
    {
        ArchiveOverviewEntry[] entries = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            Shared("Alpha.archive", "Beta.archive"),
            [],
            ArchiveOrderProblemLane.None);

        Assert.AreEqual(0, entries.Length);
    }

    [TestMethod]
    public void LosingSelectionMarksOnlyTheArchiveThatOverridesItRed()
    {
        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            Shared("Alpha.archive", "Beta.archive"),
            ["Beta.archive"],
            ArchiveOrderProblemLane.None).Single(value => value.ArchiveName == "Alpha.archive");

        Assert.IsFalse(related.SelectedWins);
        Assert.IsTrue(related.SelectedLoses);
    }

    [TestMethod]
    public void MiddleSelectionSeparatesItsWinnerFromTheArchiveItOverrides()
    {
        ResourceProvider[] resources = Shared("Alpha.archive", "Beta.archive", "Gamma.archive");

        ArchiveOverviewEntry[] entries = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive", "Gamma.archive"],
            ["Alpha.archive", "Beta.archive", "Gamma.archive"],
            resources,
            ["Beta.archive"],
            ArchiveOrderProblemLane.None);

        ArchiveOverviewEntry alpha = entries.Single(value => value.ArchiveName == "Alpha.archive");
        ArchiveOverviewEntry gamma = entries.Single(value => value.ArchiveName == "Gamma.archive");
        Assert.IsTrue(alpha.SelectedLoses);
        Assert.IsFalse(alpha.SelectedWins);
        Assert.IsTrue(gamma.SelectedWins);
        Assert.IsFalse(gamma.SelectedLoses);
    }

    [TestMethod]
    public void IdenticalPayloadUsesBlueWithoutAFalseDirectionalMarker()
    {
        string payload = new('a', 64);
        ResourceProvider[] resources = [Provider("Alpha.archive", payload), Provider("Beta.archive", payload)];

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            resources,
            ["Alpha.archive"],
            ArchiveOrderProblemLane.None).Single(value => value.ArchiveName == "Beta.archive");

        Assert.IsTrue(related.HasSame);
        Assert.IsFalse(related.SelectedWins);
        Assert.IsFalse(related.SelectedLoses);
    }

    [TestMethod]
    public void IdenticalPayloadStaysBlueWhenSameLaneOrderIsUnresolved()
    {
        string payload = new('a', 64);

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            [Provider("Alpha.archive", payload), Provider("Beta.archive", payload)],
            ["Alpha.archive"],
            ArchiveOrderProblemLane.Legacy).Single(value => value.ArchiveName == "Beta.archive");

        Assert.IsTrue(related.HasSame);
        Assert.IsFalse(related.HasUnknown);
        Assert.IsFalse(related.SelectedWins);
        Assert.IsFalse(related.SelectedLoses);
    }

    [TestMethod]
    public void MissingPayloadStillUsesProvenOrderDirection()
    {
        ResourceProvider[] resources = [Provider("Alpha.archive", null), Provider("Beta.archive", null)];

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            resources,
            ["Alpha.archive"],
            ArchiveOrderProblemLane.None).Single(value => value.ArchiveName == "Beta.archive");

        Assert.IsTrue(related.SelectedWins);
        Assert.IsFalse(related.HasUnknown);
    }

    [TestMethod]
    public void SharedRdarHashRemainsIdenticalWhenOnlyOneCookedHashIsAvailable()
    {
        string sha1 = new('a', 40);
        ResourceProvider alpha = new("Alpha.archive", 1, "base\\shared.mesh", sha1, CookedPayloadSha256: new string('b', 64));
        ResourceProvider beta = new("Beta.archive", 1, "base\\shared.mesh", sha1);

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            [alpha, beta],
            ["Alpha.archive"],
            ArchiveOrderProblemLane.None).Single(value => value.ArchiveName == "Beta.archive");

        Assert.IsTrue(related.HasSame);
        Assert.IsFalse(related.SelectedWins);
    }

    [TestMethod]
    public void UnresolvedRedmodLaneDoesNotHideTheFixedLegacyToRedmodDirection()
    {
        string redmod = "REDmod/Beta/Beta.archive";

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", redmod],
            ["Alpha.archive", redmod],
            Shared("Alpha.archive", redmod),
            ["Alpha.archive"],
            ArchiveOrderProblemLane.Redmod).Single(value => value.ArchiveName == redmod);

        Assert.IsTrue(related.SelectedWins);
        Assert.IsFalse(related.HasUnknown);
    }

    [TestMethod]
    public void CombinedUnresolvedLanesDoNotHideTheFixedLegacyToRedmodDirection()
    {
        string redmod = "REDmod/Beta/Beta.archive";

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", redmod],
            ["Alpha.archive", redmod],
            Shared("Alpha.archive", redmod),
            ["Alpha.archive"],
            ArchiveOrderProblemLane.Combined).Single(value => value.ArchiveName == redmod);

        Assert.IsTrue(related.SelectedWins);
        Assert.IsFalse(related.HasUnknown);
    }

    [TestMethod]
    public void AmbiguousRedmodProviderDoesNotHideItsFixedDirectionAgainstLegacy()
    {
        ResourceProvider legacy = Provider("Alpha.archive", new string('a', 64));
        ResourceProvider redmodA = new("REDmod/Pack/A.archive", 1, "base\\shared.mesh", new string('b', 64), ProviderName: "REDmod: Pack");
        ResourceProvider redmodB = new("REDmod/Pack/B.archive", 1, "base\\shared.mesh", new string('c', 64), ProviderName: "REDmod: Pack");

        ArchiveOverviewEntry[] entries = ArchiveOverviewProjection.BuildRelationships(
            [legacy.ArchiveName, redmodA.ArchiveName, redmodB.ArchiveName],
            [legacy.ArchiveName, redmodA.ArchiveName, redmodB.ArchiveName],
            [legacy, redmodA, redmodB],
            [legacy.ArchiveName],
            ArchiveOrderProblemLane.Redmod);

        Assert.IsTrue(entries.Single(value => value.ArchiveName == redmodA.ArchiveName).SelectedWins);
        Assert.IsTrue(entries.Single(value => value.ArchiveName == redmodB.ArchiveName).SelectedWins);
        Assert.IsFalse(entries.Any(value => value.HasUnknown));
    }

    [TestMethod]
    public void AmbiguousRedmodArchivesWithDifferentPayloadsStayYellow()
    {
        ResourceProvider alpha = new("REDmod/Pack/Alpha.archive", 1, "base\\shared.mesh", new string('a', 64), ProviderName: "REDmod: Pack");
        ResourceProvider beta = new("REDmod/Pack/Beta.archive", 1, "base\\shared.mesh", new string('b', 64), ProviderName: "REDmod: Pack");

        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            [alpha.ArchiveName, beta.ArchiveName],
            [alpha.ArchiveName, beta.ArchiveName],
            [alpha, beta],
            [alpha.ArchiveName],
            ArchiveOrderProblemLane.None).Single(value => value.ArchiveName == beta.ArchiveName);

        Assert.IsTrue(related.HasUnknown);
        Assert.IsFalse(related.SelectedWins);
        Assert.IsFalse(related.SelectedLoses);
    }

    [TestMethod]
    public void MultiSelectionUnionsOpposingRelationshipsWithoutColoringSelections()
    {
        ResourceProvider[] resources = Shared("Alpha.archive", "Beta.archive", "Gamma.archive");

        ArchiveOverviewEntry[] entries = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive", "Gamma.archive"],
            ["Alpha.archive", "Beta.archive", "Gamma.archive"],
            resources,
            ["Alpha.archive", "Gamma.archive"],
            ArchiveOrderProblemLane.None);

        ArchiveOverviewEntry beta = entries.Single(value => value.ArchiveName == "Beta.archive");
        Assert.IsTrue(beta.SelectedWins);
        Assert.IsTrue(beta.SelectedLoses);
        Assert.IsFalse(entries.Any(value => value.ArchiveName is "Alpha.archive" or "Gamma.archive" && (value.SelectedWins || value.SelectedLoses || value.HasSame || value.HasUnknown)));
    }

    [TestMethod]
    public void UnresolvedOrderUsesYellowInsteadOfGuessingDirection()
    {
        ArchiveOverviewEntry related = ArchiveOverviewProjection.BuildRelationships(
            ["Alpha.archive", "Beta.archive"],
            ["Alpha.archive", "Beta.archive"],
            Shared("Alpha.archive", "Beta.archive"),
            ["Alpha.archive"],
            ArchiveOrderProblemLane.Legacy).Single(value => value.ArchiveName == "Beta.archive");

        Assert.IsTrue(related.HasUnknown);
        Assert.IsFalse(related.SelectedWins);
        Assert.IsFalse(related.SelectedLoses);
    }

    private static ResourceProvider[] Shared(params string[] archives) => archives.Select((value, index) => Provider(value, new((char)('a' + index), 64))).ToArray();

    private static ResourceProvider Provider(string archive, string? payload) => new(archive, 1, "base\\shared.mesh", payload);
}
