using System.Text.Json;
using BusinessObjects;
using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>
/// ADR-0006's rule is that every code vocabulary that crosses a layer boundary carries a drift test.
/// <see cref="CefrLevel"/> is not an enum — a closed set with no way to construct outside it — so it
/// cannot join <c>EnumCodesTests</c>. It gets the same three checks here, held against the schema's
/// own CHECK constraints rather than against a second list in code: an added level that no column
/// accepts, a level whose code no longer serialises, and a level the wire refuses to parse back.
/// </summary>
public sealed class CefrLevelCodesTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>What a DTO member typed <see cref="CefrLevel"/> is sent as.</summary>
    private sealed record LevelCarrier(CefrLevel? Level);

    [Fact]
    public void The_authored_ladder_matches_the_level_check_constraint_on_every_levelled_column()
    {
        var schema = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "database-schema.sql"));
        var constraints = new[] { "CK_Stimuli_Level", "CK_Items_Level", "CK_Practice_Level" };

        var ladder = CefrLevel.AuthoredLevels.Select(level => level.Code).Order(StringComparer.Ordinal);

        foreach (var constraint in constraints)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                schema, $@"CONSTRAINT {constraint} CHECK \(CefrLevel IN \((?<codes>[^)]*)\)\)");

            Assert.True(match.Success, $"{constraint} not found in docs/database-schema.sql");

            var schemaCodes = System.Text.RegularExpressions.Regex
                .Matches(match.Groups["codes"].Value, "'([A-Za-z0-9_]+)'")
                .Select(m => m.Groups[1].Value)
                .Order(StringComparer.Ordinal);

            Assert.Equal(schemaCodes, ladder);
        }
    }

    [Fact]
    public void A_level_serialises_by_name_not_by_rank()
    {
        foreach (var level in CefrLevel.Reportable)
        {
            var json = JsonSerializer.Serialize(new LevelCarrier(level), Wire);

            Assert.Equal($"{{\"level\":\"{level.Code}\"}}", json);

            var back = JsonSerializer.Deserialize<LevelCarrier>(json, Wire)!;
            Assert.Equal(level, back.Level);
        }
    }

    /// <summary>
    /// The floor is reportable and the schema refuses it, so it must cross the wire but not be
    /// readable as an authored level at any boundary that writes content.
    /// </summary>
    [Fact]
    public void The_floor_crosses_the_wire_and_still_refuses_to_be_authored()
    {
        var json = JsonSerializer.Serialize(new LevelCarrier(CefrLevel.PreA1), Wire);

        Assert.Equal("{\"level\":\"pre_a1\"}", json);
        Assert.Equal(CefrLevel.PreA1, JsonSerializer.Deserialize<LevelCarrier>(json, Wire)!.Level);

        Assert.False(CefrLevel.TryParseAuthored("pre_a1", out _));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExtraEfficiency.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
