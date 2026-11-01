using BusinessObjects;

namespace Services.Tests;

/// <summary>#155: the Smart Practice mix, checked without a database. The ticket's numbers are the
/// assertions: ten questions, at most four seen, at least six unseen, and the two exclusions.</summary>
public sealed class SmartPracticePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static PracticeCandidate Unseen(int id, int? category = 1) => new(id, category, null, null);

    private static PracticeCandidate Seen(int id, int? category = 1, int answeredHoursAgo = 5) =>
        new(id, category, Now.AddHours(-answeredHoursAgo), null);

    private static PracticeCandidate Cooling(int id, int? category = 1) =>
        new(id, category, Now.AddHours(-1), Now.AddHours(23));

    private static readonly Dictionary<int, MasteryMachine.Snapshot> None = new();

    private static Dictionary<int, MasteryMachine.Snapshot> Due(int category = 1) => new()
    {
        [category] = new MasteryMachine.Snapshot(MasteryState.Learning, 1, 0, 0, 1, 1, Now.AddHours(-1), null),
    };

    private static Dictionary<int, MasteryMachine.Snapshot> NotDue(int category = 1) => new()
    {
        [category] = new MasteryMachine.Snapshot(MasteryState.Mastered, 10, 9, 3, 0, 2, Now.AddDays(14), Now),
    };

    [Fact]
    public void A_full_session_is_ten_questions_of_which_at_most_four_are_seen()
    {
        var candidates = Enumerable.Range(1, 20).Select(i => Unseen(i))
            .Concat(Enumerable.Range(101, 20).Select(i => Seen(i, answeredHoursAgo: i)))
            .ToList();

        var plan = SmartPracticePlanner.Plan(candidates, None, Now);

        Assert.Equal(10, plan.Count);
        Assert.Equal(10, plan.Distinct().Count());
        Assert.Equal(4, plan.Count(id => id >= 101)); // the seen cap
        Assert.Equal(6, plan.Count(id => id < 101));  // the unseen floor
    }

    [Fact]
    public void The_seen_questions_chosen_are_the_ones_answered_longest_ago()
    {
        // Seven seen questions answered from 1 to 7 hours ago, plus ten unseen so the seen quota really
        // is four. The three most recent lose; the most forgotten are the ones kept.
        var candidates = Enumerable.Range(1, 7)
            .Select(i => Seen(100 + i, answeredHoursAgo: i))
            .Concat(Enumerable.Range(1, 10).Select(i => Unseen(i)))
            .ToList();

        var plan = SmartPracticePlanner.Plan(candidates, None, Now);

        var seenPlanned = plan.Where(id => id >= 100).ToList();
        Assert.Equal(4, seenPlanned.Count);
        Assert.DoesNotContain(101, seenPlanned); // 1 hour ago: most recent, dropped
        Assert.DoesNotContain(102, seenPlanned);
        Assert.Contains(107, seenPlanned);       // 7 hours ago: most forgotten, kept
    }

    [Fact]
    public void Unseen_stock_running_short_is_made_up_from_the_seen_side()
    {
        // Three unseen and twelve seen: still ten questions, still no duplicates.
        var candidates = Enumerable.Range(1, 3).Select(i => Unseen(i))
            .Concat(Enumerable.Range(101, 12).Select(i => Seen(i, answeredHoursAgo: 10 + i)))
            .ToList();

        var plan = SmartPracticePlanner.Plan(candidates, None, Now);

        Assert.Equal(10, plan.Count);
        Assert.Equal(10, plan.Distinct().Count());
        Assert.Equal(3, plan.Count(id => id < 101));
    }

    [Fact]
    public void A_question_in_its_own_cooldown_is_never_planned()
    {
        var candidates = new List<PracticeCandidate> { Cooling(1), Unseen(2), Unseen(3) };

        var plan = SmartPracticePlanner.Plan(candidates, None, Now);

        Assert.DoesNotContain(1, plan);
        Assert.Equal(new[] { 2, 3 }, plan);
    }

    [Fact]
    public void A_category_that_is_not_due_contributes_nothing()
    {
        var candidates = new List<PracticeCandidate> { Unseen(1, category: 7), Unseen(2, category: 8) };

        var plan = SmartPracticePlanner.Plan(candidates, NotDue(7), Now);

        Assert.Equal(new[] { 2 }, plan);
    }

    [Fact]
    public void A_category_that_is_due_is_planned()
    {
        var candidates = new List<PracticeCandidate> { Unseen(1, category: 7) };

        var plan = SmartPracticePlanner.Plan(candidates, Due(7), Now);

        Assert.Equal(new[] { 1 }, plan);
    }

    [Fact]
    public void Five_eligible_questions_make_a_session_of_five_not_a_padded_ten()
    {
        var candidates = Enumerable.Range(1, 5).Select(i => Unseen(i)).ToList();

        var plan = SmartPracticePlanner.Plan(candidates, None, Now);

        Assert.Equal(5, plan.Count);
        Assert.Equal(5, plan.Distinct().Count());
    }

    [Fact]
    public void No_eligible_questions_makes_an_empty_plan_rather_than_an_exception()
    {
        Assert.Empty(SmartPracticePlanner.Plan([Cooling(1)], None, Now));
    }
}
