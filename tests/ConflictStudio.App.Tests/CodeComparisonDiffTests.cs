using ConflictStudio.App;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeComparisonDiffTests
{
    [TestMethod]
    public void EmptyExcerptsProduceEmptyAlignedSides()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare([], 5, [], 19);

        Assert.IsEmpty(comparison.Left);
        Assert.IsEmpty(comparison.Right);
        Assert.IsFalse(comparison.LineAlignmentLimited);
    }

    [TestMethod]
    public void IdenticalLinesKeepTheirSourceLineNumbersAndNoHighlights()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["first", "second"], 5, ["first", "second"], 19);

        CollectionAssert.AreEqual(new int?[] { 5, 6 }, comparison.Left.Select(line => line.LineNumber).ToArray());
        CollectionAssert.AreEqual(new int?[] { 19, 20 }, comparison.Right.Select(line => line.LineNumber).ToArray());
        Assert.IsFalse(comparison.LineAlignmentLimited);
        Assert.IsTrue(comparison.Left.Concat(comparison.Right).All(line => !line.IsDifferent && line.DifferenceStart == 0 && line.DifferenceLength == 0));
    }

    [TestMethod]
    public void AddedLineUsesNeutralPaddingWithoutInventingALeftSourceLine()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["before", "after"], 5, ["before", "added", "after"], 19);

        CollectionAssert.AreEqual(new int?[] { 5, null, 6 }, comparison.Left.Select(line => line.LineNumber).ToArray());
        CollectionAssert.AreEqual(new int?[] { 19, 20, 21 }, comparison.Right.Select(line => line.LineNumber).ToArray());
        Assert.AreEqual(string.Empty, comparison.Left[1].Text);
        Assert.IsTrue(comparison.Left[1].IsDifferent);
        Assert.AreEqual("added", comparison.Right[1].Text);
        Assert.IsFalse(comparison.Left[0].IsDifferent);
        Assert.IsFalse(comparison.Left[2].IsDifferent);
    }

    [TestMethod]
    public void RemovedLineUsesNeutralPaddingWithoutInventingARightSourceLine()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["before", "removed", "after"], 5, ["before", "after"], 19);

        CollectionAssert.AreEqual(new int?[] { 5, 6, 7 }, comparison.Left.Select(line => line.LineNumber).ToArray());
        CollectionAssert.AreEqual(new int?[] { 19, null, 20 }, comparison.Right.Select(line => line.LineNumber).ToArray());
        Assert.AreEqual("removed", comparison.Left[1].Text);
        Assert.AreEqual(string.Empty, comparison.Right[1].Text);
        Assert.IsTrue(comparison.Right[1].IsDifferent);
    }

    [TestMethod]
    public void ChangedLinesHighlightOnlyTheChangedValueCharacters()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["let value = 10;"], 5, ["let value = 19;"], 19);
        CodeComparisonLine left = comparison.Left.Single();
        CodeComparisonLine right = comparison.Right.Single();

        Assert.IsTrue(left.IsDifferent);
        Assert.AreEqual("0", left.Text.Substring(left.DifferenceStart, left.DifferenceLength));
        Assert.AreEqual("9", right.Text.Substring(right.DifferenceStart, right.DifferenceLength));
    }

    [TestMethod]
    public void ChangedLinesKeepUnchangedContextAligned()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["context", "left change", "shared", "tail"], 5, ["context", "right change", "shared", "tail"], 19);

        CollectionAssert.AreEqual(new int?[] { 5, 6, 7, 8 }, comparison.Left.Select(line => line.LineNumber).ToArray());
        CollectionAssert.AreEqual(new int?[] { 19, 20, 21, 22 }, comparison.Right.Select(line => line.LineNumber).ToArray());
        Assert.IsTrue(comparison.Left[1].IsDifferent);
        Assert.IsFalse(comparison.Left[2].IsDifferent);
        Assert.IsFalse(comparison.Left[3].IsDifferent);
    }

    [TestMethod]
    public void RecordedFocusRangesAlignTheirTargetLinesDespiteDifferentContext()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["record", "$type", "playerAttack: A", "playerTime: X"], 1, ["record", "playerAttack: B", "playerTime: Y"], 1, 3, 3, 2, 2);

        int focusRow = Array.FindIndex(comparison.Left, line => line.LineNumber == 3);
        Assert.AreEqual(2, focusRow);
        Assert.AreEqual(2, comparison.Right[focusRow].LineNumber);
        Assert.AreEqual("A", comparison.Left[focusRow].Text.Substring(comparison.Left[focusRow].DifferenceStart, comparison.Left[focusRow].DifferenceLength));
        Assert.AreEqual("B", comparison.Right[focusRow].Text.Substring(comparison.Right[focusRow].DifferenceStart, comparison.Right[focusRow].DifferenceLength));
    }

    [TestMethod]
    public void SurrogatePairDifferencesDoNotHighlightHalfAnEmoji()
    {
        CodeComparisonLines comparison = CodeComparisonDiff.Compare(["value 😀;"], 5, ["value 😁;"], 19);
        CodeComparisonLine left = comparison.Left.Single();
        CodeComparisonLine right = comparison.Right.Single();

        Assert.IsTrue(char.IsHighSurrogate(left.Text[left.DifferenceStart]));
        Assert.AreEqual(2, left.DifferenceLength);
        Assert.IsTrue(char.IsHighSurrogate(right.Text[right.DifferenceStart]));
        Assert.AreEqual(2, right.DifferenceLength);
    }

    [TestMethod]
    public void LargeInputsFallBackByPositionAndPreserveEveryLine()
    {
        string[] left = Enumerable.Range(0, 201).Select(index => "line " + index).ToArray();
        string[] right = Enumerable.Range(0, 200).Select(index => "line " + index).ToArray();

        CodeComparisonLines comparison = CodeComparisonDiff.Compare(left, 1, right, 1);

        Assert.IsTrue(comparison.LineAlignmentLimited);
        Assert.AreEqual(201, comparison.Left.Length);
        Assert.AreEqual(201, comparison.Right.Length);
        Assert.AreEqual(201, comparison.Left[^1].LineNumber);
        Assert.IsNull(comparison.Right[^1].LineNumber);
    }

    [TestMethod]
    public void LcsGuardUsesWideArithmeticForLargeLineCounts()
    {
        string[] lines = Enumerable.Repeat("same", 50000).ToArray();

        CodeComparisonLines comparison = CodeComparisonDiff.Compare(lines, 1, lines, 1);

        Assert.IsTrue(comparison.LineAlignmentLimited);
        Assert.AreEqual(50000, comparison.Left.Length);
        Assert.IsFalse(comparison.Left[0].IsDifferent);
    }

    [TestMethod]
    public void VeryLongLinesKeepOnlyTheirFinalCharacterHighlighted()
    {
        string prefix = new('a', 100000);
        CodeComparisonLines comparison = CodeComparisonDiff.Compare([prefix + "0"], 5, [prefix + "1"], 19);

        Assert.AreEqual(100000, comparison.Left[0].DifferenceStart);
        Assert.AreEqual(1, comparison.Left[0].DifferenceLength);
        Assert.AreEqual(1, comparison.Right[0].DifferenceLength);
    }
}
