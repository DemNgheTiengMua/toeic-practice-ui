using System.Collections.Generic;
using System.Text.Json;

namespace BusinessObjects;

/// <summary>
/// #219 / D5: one topic the placement touched, and how it went — a hint for the first week's ordering
/// and <b>explicitly not mastery</b>.
/// <para>
/// The distinction is the whole point of the type. A planning decision may say "week one starts with
/// R-INF because two of three items there were wrong"; it may never say "R-INF is learned", because
/// three items cannot support that judgement (mastery needs twelve). The signal is therefore a coarse
/// two-value routing hint, and the mastery path reads none of this.
/// </para>
/// </summary>
/// <param name="TopicId">The canonical topic the items belonged to.</param>
/// <param name="EvaluatedBand">The band the items sat at, as the level code.</param>
/// <param name="ItemsSeen">How many items of this topic the paper delivered and the learner answered.</param>
/// <param name="ItemsCorrect">How many of those were right. Never greater than <paramref name="ItemsSeen"/>.</param>
/// <param name="RoutingSignal"><see cref="Weak"/> or <see cref="OnLevel"/>.</param>
public sealed record PlacementTopicPrior(
    int TopicId,
    string EvaluatedBand,
    int ItemsSeen,
    int ItemsCorrect,
    string RoutingSignal)
{
    /// <summary>Enough evidence was missed that the topic is worth ordering earlier.</summary>
    public const string Weak = "weak";

    /// <summary>The learner was right at or near the level; the topic can wait.</summary>
    public const string OnLevel = "on_level";

    private static readonly JsonSerializerOptions Storage = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyList<PlacementTopicPrior> priors) =>
        priors.Count == 0 ? null : JsonSerializer.Serialize(priors, Storage);

    /// <summary>
    /// An unreadable row yields no hints rather than taking the planner down — the same rule
    /// <see cref="AiErrorSpan.Deserialize"/> uses. The column's ISJSON CHECK makes this a defence, not
    /// the guard.
    /// </summary>
    public static IReadOnlyList<PlacementTopicPrior> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<PlacementTopicPrior>>(json, Storage) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
