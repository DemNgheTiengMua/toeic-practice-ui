using System;
using System.Collections.Generic;
using System.Linq;
using Contracts;

namespace WebMVC.Models;

/// <summary>One child slot on the right-hand stack.</summary>
public sealed record StimulusChildSlot(
    int Position,
    string Level,
    string TopicCode,
    /// <summary>
    /// The child's own colour, as a stable index 1-4 the CSS maps to a signage-adjacent hue. A colour
    /// per child is what makes "which sentences belong to which question" answerable at a glance, and an
    /// index rather than a hex value keeps the palette in one stylesheet.
    /// </summary>
    int ColourIndex);

/// <summary>
/// #203 / S-05: everything the studio screen renders. Built from the API's own options and analysis, so
/// the page holds no threshold and no quota of its own - it groups labels and pre-creates the slots the
/// quota names.
/// </summary>
public sealed record StimulusStudioViewModel(
    string SkillCode,
    string SkillLabel,
    IReadOnlyList<StimulusStudioLevelOption> Levels,
    IReadOnlyList<StimulusStudioTypeOption> Types,
    IReadOnlyList<StimulusStudioTopicOption> Topics,
    IReadOnlyList<StimulusChildSlot> Slots,
    string? ErrorMessage,
    string? ConfigurationJson)
{
    public bool HasOptions => Levels.Count > 0;
}

public sealed record StimulusStudioLevelOption(string Code, string Label);

public sealed record StimulusStudioTypeOption(string Code, string Label);

public sealed record StimulusStudioTopicOption(int TopicId, string Code, string Name);

/// <summary>
/// The page's one translation step: codes from the API become words a reader sees (D19), and a quota's
/// locked count becomes that many slots. Nothing else is derived.
/// </summary>
public static class StimulusStudioPresentation
{
    /// <summary>The studio opens on no bundle: the author picks a level, which is what pre-creates slots.</summary>
    public static StimulusStudioViewModel Blank(
        string skillCode,
        StimulusStudioOptionsDto options,
        Func<string, string> levelLabel,
        Func<string, string> skillLabel) =>
        new(
            skillCode,
            skillLabel(skillCode),
            options.Levels.Select(code => new StimulusStudioLevelOption(code, levelLabel(code))).ToList(),
            options.ItemTypes.Select(code => new StimulusStudioTypeOption(code, TypeLabel(code))).ToList(),
            options.Topics.Select(topic => new StimulusStudioTopicOption(topic.TopicId, topic.Code, topic.Name)).ToList(),
            [],
            null,
            ConfigurationJson(skillCode, options));

    /// <summary>
    /// The slots one level and type lock. Read from the API's quota rather than recomputed, so the
    /// screen cannot pre-create a count the server would refuse. Even indices 0-3 are stable per
    /// position, which is what makes a child's colour survive a re-render.
    /// </summary>
    public static IReadOnlyList<StimulusChildSlot> Slots(
        StimulusStudioOptionsDto options,
        string level,
        string itemType,
        Func<string, string> levelLabel)
    {
        var quota = options.Quotas.FirstOrDefault(candidate =>
            string.Equals(candidate.Level, level, StringComparison.Ordinal)
            && string.Equals(candidate.ItemType, itemType, StringComparison.Ordinal));

        var count = quota?.Locked ?? 0;

        return Enumerable.Range(0, count)
            .Select(index => new StimulusChildSlot(
                index + 1,
                levelLabel(level),
                options.Topics.FirstOrDefault()?.Code ?? string.Empty,
                index + 1))
            .ToList();
    }

    /// <summary>
    /// The JSON the page's script reads. It carries the quotas and the allowed levels the server
    /// computed, so a level change is a lookup rather than a second copy of D0.3 in JavaScript.
    /// </summary>
    public static string ConfigurationJson(string skillCode, StimulusStudioOptionsDto options) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            skill = skillCode,
            spanUnit = options.SpanUnit,
            quotas = options.Quotas.Select(quota => new
            {
                level = quota.Level,
                itemType = quota.ItemType,
                locked = quota.Locked,
                minimum = quota.Minimum,
                maximum = quota.Maximum,
                allowedChildLevels = quota.AllowedChildLevels,
                minimumAtLevel = quota.MinimumAtLevel,
            }),
            topics = options.Topics.Select(topic => new { topicId = topic.TopicId, topic.Code }),
        });

    /// <summary>D0.4's codes as the words an author reads. No raw code reaches the page (D19).</summary>
    public static string TypeLabel(string code) => code switch
    {
        "short_exchange" => "Short exchange",
        "extended_dialogue" => "Extended dialogue",
        "monologue_talk" => "Monologue or talk",
        "sentence_cloze" => "Sentence cloze",
        "passage_comprehension" => "Passage comprehension",
        "text_insertion_cohesion" => "Text insertion (cohesion)",
        _ => "Item type not reported",
    };
}
