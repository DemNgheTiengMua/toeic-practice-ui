using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;

namespace WebMVC.Models;

/// <summary>#204: one header + window pair the script renders for a type, with the hint painted beside it.</summary>
public sealed record ProductiveTypeOption(
    string Code,
    string Label,
    /// <summary>none, optional or required. The screen says "needs an image" rather than "optional".</summary>
    string Visual,
    string VisualLabel,
    string PrepHint,
    string ResponseHint,
    string WordHint);

/// <summary>#204: one part slot the screen pre-creates. A writing task opens with one; it may add a second.</summary>
public sealed record ProductivePartSlot(int Position, string ItemType, string ItemTypeLabel);

/// <summary>
/// #204 / S-07: everything the productive studio renders. Built from the API's own options, so the page
/// holds no window, no word bound and no cap of its own - it groups labels and pre-creates the parts.
/// <para>
/// <see cref="PlacementRecordingCap"/> is the ticket's fourth criterion in the model rather than only in
/// a refusal: the screen prints the cap beside the recording field from the moment placement is
/// selected, so the author meets it before doing the work.
/// </para>
/// </summary>
public sealed record ProductiveStudioViewModel(
    string SkillCode,
    string SkillLabel,
    IReadOnlyList<ProductiveLevelOption> Levels,
    IReadOnlyList<ProductiveTypeOption> Types,
    IReadOnlyList<ProductiveTopicOption> Topics,
    IReadOnlyList<ProductivePartSlot> Parts,
    string? ErrorMessage,
    string? ConfigurationJson)
{
    public bool HasOptions => Levels.Count > 0;
}

public sealed record ProductiveLevelOption(string Code, string Label);

public sealed record ProductiveTopicOption(int TopicId, string Code, string Name);

/// <summary>
/// The page's one translation step: codes from the API become words a reader sees (D19), and D0.4's
/// numeric windows become the hint printed beside each field. Nothing else is derived.
/// </summary>
public static class ProductiveStudioPresentation
{
    /// <summary>D0.2 and D0.7: placement's recording cap, shown before the author types anything.</summary>
    public const int PlacementRecordingCapSeconds = 60;

    /// <summary>The studio opens on one part: a one-part task is legal for both skills.</summary>
    public static ProductiveStudioViewModel Blank(
        string skillCode,
        ProductiveStudioOptionsDto options,
        Func<string, string> levelLabel,
        Func<string, string> skillLabel) =>
        new(
            skillCode,
            skillLabel(skillCode),
            options.Levels.Select(code => new ProductiveLevelOption(code, levelLabel(code))).ToList(),
            options.Types.Select(type => TypeOption(type)).ToList(),
            options.Topics.Select(topic => new ProductiveTopicOption(topic.TopicId, topic.Code, topic.Name)).ToList(),
            [new ProductivePartSlot(1, options.Types.FirstOrDefault()?.Code ?? string.Empty, TypeLabel(options.Types.FirstOrDefault()?.Code))],
            null,
            ConfigurationJson(skillCode, options));

    public static ProductiveTypeOption TypeOption(ProductiveTypeOptionDto type) =>
        new(
            type.Code,
            TypeLabel(type.Code),
            type.Visual,
            type.Visual switch
            {
                "required" => "A visual stimulus is required",
                "optional" => "A visual stimulus may be added",
                _ => "No visual stimulus",
            },
            WindowHint(type.MinimumPrepSeconds, type.MaximumPrepSeconds, "preparation"),
            WindowHint(type.MinimumResponseSeconds, type.MaximumResponseSeconds,
                string.Equals(type.Code, "descriptive_monologue", StringComparison.Ordinal)
                    || type.MaximumWords is null ? "recording" : "writing"),
            type.MinimumWords is int floor
                ? $"{floor}-{type.MaximumWords} words by band"
                : "Bounded by its recording window");

    /// <summary>
    /// The JSON the page's script reads. It carries the bounds and the caps the server computed, so a
    /// window change is a lookup rather than a second copy of D0.4 in JavaScript.
    /// </summary>
    public static string ConfigurationJson(string skillCode, ProductiveStudioOptionsDto options) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            skill = skillCode,
            placementRecordingCap = PlacementRecordingCapSeconds,
            types = options.Types.Select(type => new
            {
                code = type.Code,
                visual = type.Visual,
                minimumPrepSeconds = type.MinimumPrepSeconds,
                maximumPrepSeconds = type.MaximumPrepSeconds,
                minimumResponseSeconds = type.MinimumResponseSeconds,
                maximumResponseSeconds = type.MaximumResponseSeconds,
                minimumWords = type.MinimumWords,
                maximumWords = type.MaximumWords,
            }),
            topics = options.Topics.Select(topic => new { topicId = topic.TopicId, topic.Code }),
        });

    private static string WindowHint(int? minimum, int? maximum, string noun)
    {
        if (minimum is not int low || maximum is not int high)
        {
            return $"No {noun} window in D0.4";
        }

        return low == high
            ? $"{noun}: {low} s"
            : $"{noun}: {low}-{high} s";
    }

    /// <summary>D0.4's productive codes as the words an author reads. No raw code reaches the page (D19).</summary>
    public static string TypeLabel(string? code) => code switch
    {
        "descriptive_monologue" => "Descriptive monologue",
        "situational_negotiation" => "Situational negotiation",
        "argumentative_monologue" => "Argumentative monologue",
        "visual_data_synthesis" => "Visual data synthesis",
        "transactional_correspondence" => "Transactional correspondence",
        "persuasive_essay" => "Persuasive essay",
        "analytical_evaluation_essay" => "Analytical evaluation essay",
        _ => "Task type not reported",
    };
}
