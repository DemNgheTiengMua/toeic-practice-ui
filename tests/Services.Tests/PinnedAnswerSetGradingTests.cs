using BusinessObjects;
using BusinessObjects.Entities;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269: <b>the exam grader and the drill grader must agree about one item.</b>
/// <para>
/// <b>Why this file exists.</b> The two paths had separate graders, and they drifted twice, both
/// silently:
/// </para>
/// <list type="bullet">
/// <item>
/// A text completion (no label) was graded correct on save and then re-graded to FALSE on submit,
/// because <c>EvaluateAnswer</c> compared a null label against the key. The sitting showed the answer as
/// right and the review showed it as wrong, from one row. Fixed in the same change that added
/// <c>EvaluatePinnedText</c>.
/// </item>
/// <item>
/// A <c>multi_select</c> keyed <c>A,C</c> was graded FALSE on every submission, because a set answer
/// records its choices in <c>SelectedOptionIdsJson</c> and leaves the single label null - so
/// <c>EvaluateAnswer</c> compared a null label against a two-label key. Measured on the E2E fixture: a
/// learner who ticked both correct boxes was told they were wrong. The DRILL path had already been fixed;
/// the EXAM path had not.
/// </item>
/// </list>
/// <para>
/// <b>What a wrong implementation could still pass.</b> Asserting only "a right answer is right" passes on
/// a grader that always returns true. Every case below is paired with a wrong answer, and the set case is
/// paired with a near miss (both keyed labels plus one more) so a grader that only checks "contains the
/// key" fails too.
/// </para>
/// </summary>
public sealed class PinnedAnswerSetGradingTests
{
    private static ItemVersion Version(string keyLabel, int itemVersionId = 7) => new()
    {
        ItemVersionId = itemVersionId,
        ItemId = 1,
        VersionNumber = 1,
        Stem = "Which two statements are supported by the passage?",
        OptionA = "First correct",
        OptionB = "Second correct",
        OptionC = "Wrong",
        OptionD = "Also wrong",
        KeyLabel = keyLabel,
        MaxSelections = 2,
    };

    /// <summary>
    /// The option id a screen renders for a label: (version, index), the bijection the projection hands
    /// the client. Written out here rather than reaching for <c>AttemptEngine.PoolOptionId</c> (internal)
    /// because the NUMBER is the contract this test is about: an id that does not decode against the
    /// version must be dropped, and only the real formula proves that.
    /// </summary>
    private static int IdFor(ItemVersion version, int index) =>
        version.ItemVersionId * 10 + index + 1;

    [Fact]
    public void Both_keyed_labels_chosen_is_right()
    {
        var version = Version("A,B");

        var json = System.Text.Json.JsonSerializer.Serialize(
            new[] { IdFor(version, 0), IdFor(version, 1) });

        var answer = new AttemptAnswer
        {
            ItemVersionId = version.ItemVersionId,
            SelectedOptionIdsJson = json,
        };

        Assert.True(AttemptEngine.EvaluateAnswer(answer, new Dictionary<int, ItemVersion>
        {
            [version.ItemVersionId] = version,
        }));
    }

    [Fact]
    public void One_keyed_label_short_is_wrong()
    {
        var version = Version("A,B");

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { IdFor(version, 0) });

        var answer = new AttemptAnswer
        {
            ItemVersionId = version.ItemVersionId,
            SelectedOptionIdsJson = json,
        };

        Assert.False(AttemptEngine.EvaluateAnswer(answer, new Dictionary<int, ItemVersion>
        {
            [version.ItemVersionId] = version,
        }));
    }

    /// <summary>
    /// Both keyed labels PLUS one the key does not name. This is the case a "does the chosen set contain
    /// the key" grader gets wrong, which is why the comparison is <c>SetEquals</c>.
    /// </summary>
    [Fact]
    public void Both_keyed_labels_plus_an_extra_is_wrong()
    {
        var version = Version("A,B");

        var json = System.Text.Json.JsonSerializer.Serialize(
            new[] { IdFor(version, 0), IdFor(version, 1), IdFor(version, 3) });

        var answer = new AttemptAnswer
        {
            ItemVersionId = version.ItemVersionId,
            SelectedOptionIdsJson = json,
        };

        Assert.False(AttemptEngine.EvaluateAnswer(answer, new Dictionary<int, ItemVersion>
        {
            [version.ItemVersionId] = version,
        }));
    }

    /// <summary>
    /// An id from ANOTHER version decodes to -1 and must be dropped, not counted as a choice. A grader
    /// that counted decoded indices raw would let a foreign id satisfy nothing but also never fail
    /// loudly - here it must simply not help.
    /// </summary>
    [Fact]
    public void An_id_from_another_version_does_not_count()
    {
        var version = Version("A,B");
        var foreign = Version("A,B", itemVersionId: 9);

        var json = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            IdFor(version, 0),
            IdFor(foreign, 1),
        });

        var answer = new AttemptAnswer
        {
            ItemVersionId = version.ItemVersionId,
            SelectedOptionIdsJson = json,
        };

        Assert.False(AttemptEngine.EvaluateAnswer(answer, new Dictionary<int, ItemVersion>
        {
            [version.ItemVersionId] = version,
        }));
    }

    /// <summary>
    /// A single-choice answer still grades by its label, unchanged. The set branch must not swallow it.
    /// </summary>
    [Fact]
    public void A_single_label_still_grades_by_the_key()
    {
        var version = Version("A");

        Assert.True(AttemptEngine.EvaluateAnswer(
            new AttemptAnswer { ItemVersionId = version.ItemVersionId, SelectedLabel = "A" },
            new Dictionary<int, ItemVersion> { [version.ItemVersionId] = version }));

        Assert.False(AttemptEngine.EvaluateAnswer(
            new AttemptAnswer { ItemVersionId = version.ItemVersionId, SelectedLabel = "C" },
            new Dictionary<int, ItemVersion> { [version.ItemVersionId] = version }));
    }

    /// <summary>
    /// And a text answer is still judged by its accepted answers - the third arm of the same method.
    /// </summary>
    [Fact]
    public void A_text_answer_still_grades_by_its_accepted_answers()
    {
        var version = new ItemVersion
        {
            ItemVersionId = 11,
            ItemId = 1,
            VersionNumber = 1,
            Stem = "The team is currently ____ a new tool.",
            AcceptedAnswersJson = "[\"development\"]",
        };

        Assert.True(AttemptEngine.EvaluateAnswer(
            new AttemptAnswer { ItemVersionId = 11, ResponseText = "  DEVELOPMENT  " },
            new Dictionary<int, ItemVersion> { [11] = version }));

        Assert.False(AttemptEngine.EvaluateAnswer(
            new AttemptAnswer { ItemVersionId = 11, ResponseText = "deployment" },
            new Dictionary<int, ItemVersion> { [11] = version }));
    }
}
