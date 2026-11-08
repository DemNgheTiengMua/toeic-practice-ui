using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #238: the four-state estimate of one cell, as a pure function. These name each state at its own
/// threshold, so the input that would pass a weaker test — a single bool "is it mastered" — fails:
/// under a bool, Reviewing and Learning are the same answer.
/// <para>
/// The numbers are <see cref="MasterySchedule"/>'s own, and the same function backs both the path's
/// step sizing and the practice hub's matrix, so a change to the estimate surfaces here once.
/// </para>
/// </summary>
public sealed class CellMasteryTests
{
    [Fact]
    public void No_answers_is_untested()
    {
        Assert.Equal(MasteryState.Untested, CellMastery.Estimate(answered: 0, correct: 0));
    }

    /// <summary>Below the reviewing floor by count: five answers are needed, four are not.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(4, 3)]
    public void Fewer_than_five_answers_is_learning_even_at_perfect_accuracy(int answered, int correct)
    {
        Assert.Equal(MasteryState.Learning, CellMastery.Estimate(answered, correct));
    }

    /// <summary>
    /// The guard against the bool. Nine of nine is 100% — above the mastered accuracy floor — but ten
    /// answers are required, so it is Reviewing, not Mastered. Under a formula that asked only "is the
    /// accuracy high enough", this would pass as Mastered.
    /// </summary>
    [Fact]
    public void Nine_correct_of_nine_is_reviewing_not_mastered()
    {
        Assert.Equal(MasteryState.Reviewing, CellMastery.Estimate(answered: 9, correct: 9));
    }

    [Fact]
    public void Five_of_five_is_reviewing()
    {
        Assert.Equal(MasteryState.Reviewing, CellMastery.Estimate(answered: 5, correct: 5));
    }

    /// <summary>
    /// The other half of the bool guard: 73% over 20 answers clears no floor, so it is Learning even
    /// though the count is high. A formula that asked only "enough answers" would wrongly promote it.
    /// </summary>
    [Fact]
    public void Fourteen_of_twenty_is_learning_despite_the_count()
    {
        Assert.Equal(MasteryState.Learning, CellMastery.Estimate(answered: 20, correct: 14));
    }

    [Fact]
    public void Seventeen_of_twenty_is_mastered()
    {
        Assert.Equal(MasteryState.Mastered, CellMastery.Estimate(answered: 20, correct: 17));
    }

    /// <summary>The four states are all reachable, which a bool cannot be.</summary>
    [Fact]
    public void All_four_states_are_reachable()
    {
        var states = new[]
        {
            CellMastery.Estimate(0, 0),
            CellMastery.Estimate(2, 0),
            CellMastery.Estimate(9, 9),
            CellMastery.Estimate(20, 17),
        };

        Assert.Equal(
            [MasteryState.Untested, MasteryState.Learning, MasteryState.Reviewing, MasteryState.Mastered],
            states);
    }
}
