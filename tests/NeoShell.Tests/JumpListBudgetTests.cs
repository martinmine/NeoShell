using NeoShell.Interop.Shell;

namespace NeoShell.Tests;

public sealed class JumpListBudgetTests
{
    [Fact]
    public void RecentGetsWhatThePinsLeave()
    {
        // Measured on Explorer: 13 recent files, then 12 beside one pin, 11 beside two.
        for (int pins = 0; pins <= 2; pins++)
        {
            (int pinned, int[] categories) = Split(13, pins, [null]);
            Assert.Equal(pins, pinned);
            Assert.Equal([13 - pins], categories);
        }
    }

    [Fact]
    public void PinsComeFirstAndCanTakeEverything()
    {
        (int pinned, int[] categories) = Split(13, 20, 5, null);
        Assert.Equal(13, pinned);
        Assert.Equal([0, 0], categories);
    }

    [Fact]
    public void OwnCategoriesTakeTheirsInOrderBeforeTheKnownOnes()
    {
        (int pinned, int[] categories) = Split(13, 2, 4, null, 9);
        Assert.Equal(2, pinned);
        Assert.Equal([4, 0, 7], categories);
    }

    [Fact]
    public void KnownCategoriesShareWhatsLeftTheFirstGettingTheOddOne()
    {
        (_, int[] categories) = Split(13, 0, null, null);
        Assert.Equal([7, 6], categories);
    }

    [Theory]
    [InlineData(null, 13)]
    [InlineData(-1, 13)]
    [InlineData(0, 0)]
    [InlineData(20, 20)]
    [InlineData(99, 60)]
    public void MaximumFollowsTheSetting(int? setting, int expected) => Assert.Equal(expected, JumpListBudget.Maximum(setting));

    [Fact]
    public void AKnownCategoryTheAppAskedForTakesOne()
    {
        // Measured on Explorer: two pins, a category of three links and Recent show 7 recent files of 13.
        (int pinned, int[] categories) = JumpListBudget.Split(13, 2, [3, null], knownAskedFor: true);
        Assert.Equal(2, pinned);
        Assert.Equal([3, 7], categories);
    }

    private static (int Pinned, int[] Categories) Split(int maximum, int pinned, params int?[] categories) =>
        JumpListBudget.Split(maximum, pinned, categories, knownAskedFor: false);
}
