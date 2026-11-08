using System.Linq;
using System.Reflection;
using System.Text.Json;
using BusinessObjects;
using Contracts;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #214 / U18: <b>an item's CEFR level is never sent to the client during a sitting.</b> Showing it
/// invites a learner to skip the items they find hard, and a learner who skips B2 items is measured as
/// A2 - the measurement is corrupted by the very field it was made visible with.
/// <para>
/// <b>Why this is asserted by shape and by an actual payload, not by reading the mapper.</b> The mapper
/// is one line; the risk is the next person adding a level for a harmless-looking reason ("the review
/// page needs it") and the sitting silently acquiring it. A reflection test over the wire type fails
/// on the day a field is added; a serialization test fails if one is smuggled in through an anonymous
/// object or a dictionary. Both are cheap, so both are here.
/// </para>
/// </summary>
public sealed class PlacementSittingCarriesNoLevelTests
{
    /// <summary>
    /// The wire types a placement sitting sends. <c>AttemptQuestionDto</c> is the one that carries the
    /// item, so it is the one that could leak; the rest are listed because a level added to any of them
    /// would reach the browser too.
    /// </summary>
    private static readonly System.Type[] SittingWireTypes =
    [
        typeof(PlacementTakeViewDto),
        typeof(AttemptQuestionDto),
        typeof(AttemptOptionDto),
        typeof(NavigatorItemDto),
        typeof(AttemptPhaseDto),
    ];

    [Fact]
    public void No_sitting_wire_type_exposes_a_level()
    {
        foreach (var type in SittingWireTypes)
        {
            var leaked = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => LooksLikeALevel(property.Name, property.PropertyType))
                .Select(property => $"{type.Name}.{property.Name}")
                .ToList();

            Assert.True(leaked.Count == 0,
                $"A sitting payload may not carry an item's level (U18): {string.Join(", ", leaked)}");
        }
    }

    /// <summary>
    /// The serialized payload of a real question, checked by name so a level arriving through a
    /// dictionary, an anonymous object or a new nested record is caught as well as one on the type.
    /// </summary>
    [Fact]
    public void A_serialized_sitting_payload_carries_no_level_key()
    {
        var question = Question(level: CefrLevel.B2);
        var take = new PlacementTakeViewDto(
            7, 100, 14, "Diagnostic", "TOEIC", PlacementStatusDto.InProgress,
            System.DateTimeOffset.UnixEpoch, System.DateTimeOffset.UnixEpoch.AddHours(72), 0, 1, 0, 0,
            question, [], false, [question], []);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(take));

        var keys = Flatten(json.RootElement).Select(pair => pair.Key).ToList();
        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => LooksLikeALevel(key, valueType: null));
    }

    private static bool LooksLikeALevel(string name, System.Type? valueType)
    {
        // A level is the CEFR kind or the string that spells one. Band codes are a certificate's own
        // scale, not a level, and are deliberately allowed - but a sitting sends none of those either.
        if (name.Contains("level", System.StringComparison.OrdinalIgnoreCase)
            || name.Contains("cefr", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var type = System.Nullable.GetUnderlyingType(valueType ?? typeof(object)) ?? valueType;
        return type == typeof(CefrLevel);
    }

    private static System.Collections.Generic.IEnumerable<(string Key, JsonElement Value)> Flatten(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return (property.Name, property.Value);
                    foreach (var nested in Flatten(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Flatten(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static AttemptQuestionDto Question(CefrLevel level) => new(
        10, 1, 1, 14, "R1", "Reading", "READ", "Reading", ModalityDto.Reading,
        // The level is written into the stem on purpose: the payload MAY carry the text a learner
        // reads, and this proves the assertion is about a field, not about the word appearing.
        $"Read this (authored at {level.Code}).",
        null, null, null, null, null, null, null, [], null, false);
}
