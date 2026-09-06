using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveResourceIndexBuilderTests
{
    [TestMethod]
    public void BuildGroupsExactWinningLosingRedundantAndUniqueResourcesByArchive()
    {
        ResourceProvider[] resources =
        [
            Provider("High.archive", 1, "base\\shared.mesh", "a"),
            Provider("Low.archive", 1, "base\\shared.mesh", "b"),
            Provider("High.archive", 2, "base\\same.inkatlas", "c"),
            Provider("Low.archive", 2, "base\\same.inkatlas", "c"),
            Provider("High.archive", 3, "base\\high-only.ent", "d"),
            Provider("Low.archive", 4, "base\\low-only.ent", "e")
        ];
        Mo2Archive[] archives = [new("High Mod", "High.archive", "high", 1, new string('a', 64)), new("Low Mod", "Low.archive", "low", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Low.archive"]);

        ArchiveConflictSummary high = summaries.Single(value => value.ArchiveName == "High.archive");
        ArchiveConflictSummary low = summaries.Single(value => value.ArchiveName == "Low.archive");
        Assert.AreEqual("High Mod", high.Provider);
        Assert.AreEqual(2, high.Winning.Length);
        Assert.AreEqual(0, high.Losing.Length);
        Assert.AreEqual(0, high.Redundant.Length);
        Assert.AreEqual(1, high.Unique.Length);
        Assert.AreEqual(0, low.Winning.Length);
        Assert.AreEqual(2, low.Losing.Length);
        Assert.AreEqual(1, low.Redundant.Length);
        Assert.AreEqual("High.archive", low.Losing.Single(value => value.DisplayName == "base\\shared.mesh").WinnerArchive);
        Assert.IsTrue(low.Losing.Any(value => value.DisplayName == "base\\shared.mesh" && value.PayloadRelation == ArchivePayloadRelation.Different));
        Assert.IsTrue(low.Redundant.Any(value => value.DisplayName == "base\\same.inkatlas" && value.PayloadRelation == ArchivePayloadRelation.Identical));
    }

    [TestMethod]
    public void BuildCountsMiddleProviderAsBothWinningAndLosing()
    {
        ResourceProvider[] resources = [Provider("High.archive", 9, "base\\chain.mesh", "a"), Provider("Middle.archive", 9, "base\\chain.mesh", "b"), Provider("Low.archive", 9, "base\\chain.mesh", "c")];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Middle", "Middle.archive", "middle", 1, new string('b', 64)), new("Low", "Low.archive", "low", 1, new string('c', 64))];

        ArchiveConflictSummary middle = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Middle.archive", "Low.archive"]).Single(value => value.ArchiveName == "Middle.archive");

        Assert.AreEqual(1, middle.Winning.Length);
        Assert.AreEqual(1, middle.Losing.Length);
        Assert.AreEqual(ArchiveResourceDisposition.WinningAndLosing, middle.Winning.Single().Disposition);
        Assert.AreEqual("High.archive", middle.Winning.Single().WinnerArchive);
    }

    [TestMethod]
    public void BuildClassifiesEachProviderAgainstTheEffectiveWinner()
    {
        ResourceProvider[] resources = [Provider("High.archive", 9, "base\\chain.mesh", "a"), Provider("Middle.archive", 9, "base\\chain.mesh", "a"), Provider("Low.archive", 9, "base\\chain.mesh", "b")];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Middle", "Middle.archive", "middle", 1, new string('b', 64)), new("Low", "Low.archive", "low", 1, new string('c', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Middle.archive", "Low.archive"]);

        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "High.archive").Winning.Length);
        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "Middle.archive").Redundant.Length);
        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "Middle.archive").Losing.Length);
        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "Low.archive").Losing.Length);
    }

    [TestMethod]
    public void BuildMarksUnreadableArchiveAsUnresolved()
    {
        Mo2Archive archive = new("Broken Mod", "Broken.archive", "broken", 1, new string('a', 64));

        ArchiveConflictSummary summary = ArchiveResourceIndexBuilder.Build([], [archive], ["Broken.archive"], [new RdarArchiveFailure("Broken Mod", "Broken.archive", "Invalid RDAR index")]).Single();

        Assert.AreEqual(1, summary.Unresolved.Length);
        Assert.IsTrue(summary.Unresolved.Single().DisplayName.Contains("Invalid RDAR index", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildDoesNotClaimWinnersWhenAnyArchiveCouldNotBeRead()
    {
        ResourceProvider[] resources = [Provider("High.archive", 9, "base\\shared.mesh", "a"), Provider("Low.archive", 9, "base\\shared.mesh", "b")];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Low", "Low.archive", "low", 1, new string('b', 64)), new("Broken", "Broken.archive", "broken", 1, new string('c', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, [], [new RdarArchiveFailure("Broken", "Broken.archive", "Invalid RDAR index")]);

        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Losing.Length));
        Assert.AreEqual(3, summaries.Sum(value => value.Unresolved.Length));
    }

    [TestMethod]
    public void BuildKeepsAReadableWinnerWhenTheUnreadableArchiveIsLowerPriority()
    {
        ResourceProvider[] resources = [Provider("Readable.archive", 9, "base\\apparently-unique.mesh", "a")];
        Mo2Archive[] archives = [new("Readable", "Readable.archive", "readable", 1, new string('a', 64)), new("Broken", "Broken.archive", "broken", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["Readable.archive", "Broken.archive"], [new RdarArchiveFailure("Broken", "Broken.archive", "Invalid RDAR index")]);

        Assert.AreEqual(0, summaries.Sum(value => value.Unique.Length));
        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "Readable.archive").Winning.Length);
        Assert.AreEqual("Readable.archive", summaries.Single(value => value.ArchiveName == "Readable.archive").Winning.Single().WinnerArchive);
        Assert.AreEqual(1, summaries.Sum(value => value.Unresolved.Length));
    }

    [TestMethod]
    public void BuildLeavesKnownChainUnresolvedWhenUnreadableArchiveCrossesIt()
    {
        ResourceProvider[] resources = [Provider("High.archive", 9, "base\\shared.mesh", "a"), Provider("Low.archive", 9, "base\\shared.mesh", "b")];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Middle", "Middle.archive", "middle", 1, new string('b', 64)), new("Low", "Low.archive", "low", 1, new string('c', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Middle.archive", "Low.archive"], [new RdarArchiveFailure("Middle", "Middle.archive", "Invalid RDAR index")]);

        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Losing.Length));
        Assert.AreEqual(3, summaries.Sum(value => value.Unresolved.Length));
    }

    [TestMethod]
    public void BuildKeepsWinningAndLosingWhenPayloadComparisonIsUnavailable()
    {
        ResourceProvider[] resources = [new("High.archive", 9, "base\\shared.mesh", null, ProviderName: "High"), new("Low.archive", 9, "base\\shared.mesh", null, ProviderName: "Low")];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Low", "Low.archive", "low", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Low.archive"]);

        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "High.archive").Winning.Length);
        Assert.AreEqual(1, summaries.Single(value => value.ArchiveName == "Low.archive").Losing.Length);
        Assert.AreEqual(0, summaries.Sum(value => value.Unresolved.Length));
        Assert.IsTrue(summaries.SelectMany(value => value.Winning.Concat(value.Losing)).All(value => value.PayloadRelation == ArchivePayloadRelation.Unknown));
    }

    [TestMethod]
    public void BuildDoesNotCompareCookedSha256ToRdarSha1()
    {
        ResourceProvider[] resources =
        [
            new("High.archive", 9, "base\\shared.mesh", null, ProviderName: "High", CookedPayloadSha256: new string('a', 64)),
            new("Low.archive", 9, "base\\shared.mesh", new string('b', 40), ProviderName: "Low")
        ];
        Mo2Archive[] archives = [new("High", "High.archive", "high", 1, new string('a', 64)), new("Low", "Low.archive", "low", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["High.archive", "Low.archive"]);

        Assert.AreEqual(ArchivePayloadRelation.Unknown, summaries.Single(value => value.ArchiveName == "Low.archive").Losing.Single().PayloadRelation);
        Assert.AreEqual(new string('a', 64), summaries.Single(value => value.ArchiveName == "High.archive").Winning.Single().CookedPayloadSha256);
        Assert.IsNull(summaries.Single(value => value.ArchiveName == "High.archive").Winning.Single().RdarPayloadSha1);
        Assert.AreEqual(new string('b', 40), summaries.Single(value => value.ArchiveName == "Low.archive").Losing.Single().RdarPayloadSha1);
        Assert.IsNull(summaries.Single(value => value.ArchiveName == "Low.archive").Losing.Single().CookedPayloadSha256);
        Assert.AreEqual(0, summaries.Sum(value => value.Redundant.Length));
    }

    [TestMethod]
    public void UnknownRedmodOrderOnlyBlocksRedmodRelationships()
    {
        string redmod = "REDmod/Beta/Beta.archive";
        ResourceProvider[] resources = [Provider("Alpha.archive", 9, "base\\shared.mesh", "a"), Provider(redmod, 9, "base\\shared.mesh", "b")];
        Mo2Archive[] archives = [new("Alpha", "Alpha.archive", "alpha", 1, new string('a', 64)), new("Beta", redmod, "beta", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["Alpha.archive", redmod], unresolvedLane: ArchiveOrderProblemLane.Redmod);

        Assert.AreEqual(1, summaries.Sum(value => value.Winning.Length));
        Assert.AreEqual(1, summaries.Sum(value => value.Losing.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Unresolved.Length));
    }

    [TestMethod]
    public void UnknownRedmodOrderDoesNotObscureAUniqueRedmodFile()
    {
        string redmod = "REDmod/Alpha/Alpha.archive";
        ResourceProvider[] resources = [Provider(redmod, 9, "base\\unique.mesh", "a")];
        Mo2Archive[] archives = [new("Alpha", redmod, "alpha", 1, new string('a', 64))];

        ArchiveConflictSummary summary = ArchiveResourceIndexBuilder.Build(resources, archives, [redmod], unresolvedLane: ArchiveOrderProblemLane.Redmod).Single();

        Assert.AreEqual(1, summary.Unique.Length);
        Assert.AreEqual(0, summary.Unresolved.Length);
    }

    [TestMethod]
    public void MissingRedmodInputDoesNotObscureAUniqueLegacyFile()
    {
        ResourceProvider[] resources = [Provider("Alpha.archive", 9, "base\\unique.mesh", "a")];
        Mo2Archive[] archives = [new("Alpha", "Alpha.archive", "alpha", 1, new string('a', 64))];

        ArchiveConflictSummary summary = ArchiveResourceIndexBuilder.Build(resources, archives, ["Alpha.archive"], incompleteLane: ArchiveOrderProblemLane.Redmod).Single();

        Assert.AreEqual(1, summary.Unique.Length);
        Assert.AreEqual(0, summary.Unresolved.Length);
    }

    [TestMethod]
    public void MissingRedmodInputObscuresAUniqueRedmodFile()
    {
        string redmod = "REDmod/Alpha/Alpha.archive";
        ResourceProvider[] resources = [Provider(redmod, 9, "base\\unique.mesh", "a")];
        Mo2Archive[] archives = [new("Alpha", redmod, "alpha", 1, new string('a', 64))];

        ArchiveConflictSummary summary = ArchiveResourceIndexBuilder.Build(resources, archives, [redmod], incompleteLane: ArchiveOrderProblemLane.Redmod).Single();

        Assert.AreEqual(0, summary.Unique.Length);
        Assert.AreEqual(1, summary.Unresolved.Length);
    }

    [TestMethod]
    public void UnknownLegacyOrderDoesNotClassifyMixedLaneProvidersAgainstAGuessedWinner()
    {
        string redmod = "REDmod/Gamma/Gamma.archive";
        ResourceProvider[] resources =
        [
            Provider("Alpha.archive", 9, "base\\shared.mesh", "a"),
            Provider("Beta.archive", 9, "base\\shared.mesh", "b"),
            Provider(redmod, 9, "base\\shared.mesh", "c")
        ];
        Mo2Archive[] archives =
        [
            new("Alpha", "Alpha.archive", "alpha", 1, new string('a', 64)),
            new("Beta", "Beta.archive", "beta", 1, new string('b', 64)),
            new("Gamma", redmod, "gamma", 1, new string('c', 64))
        ];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["Alpha.archive", "Beta.archive", redmod], unresolvedLane: ArchiveOrderProblemLane.Legacy);

        Assert.AreEqual(3, summaries.Sum(value => value.Unresolved.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length + value.Losing.Length));
    }

    [TestMethod]
    public void ConflictBetweenUnlistedArchivesStaysUnresolvedInArchiveSummaries()
    {
        ResourceProvider[] resources = [Provider("Alpha.archive", 9, "base\\shared.mesh", "a"), Provider("Beta.archive", 9, "base\\shared.mesh", "b")];
        Mo2Archive[] archives = [new("Alpha", "Alpha.archive", "alpha", 1, new string('a', 64)), new("Beta", "Beta.archive", "beta", 1, new string('b', 64))];

        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, ["Alpha.archive", "Beta.archive"], unlistedArchives: ["Alpha.archive", "Beta.archive"]);

        Assert.AreEqual(2, summaries.Sum(value => value.Unresolved.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length + value.Losing.Length));
    }

    private static ResourceProvider Provider(string archive, ulong hash, string path, string payload)
        => new(archive, hash, path, payload.PadRight(40, payload[0]), ResourceType: Path.GetExtension(path).TrimStart('.'), PathConfidence: ResourcePathConfidence.ResolvedIndex, ProviderName: archive + " provider");
}
