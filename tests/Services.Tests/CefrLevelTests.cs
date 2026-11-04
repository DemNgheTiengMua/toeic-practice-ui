using System.Reflection;
using BusinessObjects;
using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #182: one level type, and no second scale beside it. A level is A1 to C2; Pre-A1 exists so a
/// result can say "below A1" honestly, and it is not a level an author may choose. A certificate
/// band is a different quantity — a score range the certificate defines — and the two must never be
/// convertible, because labelling a band "B2" is how the product claimed a level it never measured.
/// </summary>
public sealed class CefrLevelTests
{
    // The ladder as the specification writes it (D0.1), independent of the implementation's spelling.
    private static readonly string[] Ladder = ["A1", "A2", "B1", "B2", "C1", "C2"];

    [Fact]
    public void The_authored_levels_are_A1_to_C2_in_ascending_order()
    {
        Assert.Equal(Ladder, CefrLevel.AuthoredLevels.Select(level => level.Code));
        Assert.Equal(6, CefrLevel.AuthoredLevels.Count);

        for (var i = 1; i < CefrLevel.AuthoredLevels.Count; i++)
        {
            Assert.True(CefrLevel.AuthoredLevels[i - 1] < CefrLevel.AuthoredLevels[i],
                $"{CefrLevel.AuthoredLevels[i - 1]} must sort below {CefrLevel.AuthoredLevels[i]}");
        }
    }

    [Fact]
    public void The_floor_sorts_below_A1_and_is_reportable_but_never_authored()
    {
        var floor = CefrLevel.PreA1;

        Assert.True(floor.IsReportableFloor);
        Assert.True(floor < CefrLevel.A1);
        // The floor is the type's default, so an unset level reports honestly rather than claiming A1.
        Assert.Equal(floor, default(CefrLevel));
        Assert.Equal(floor, CefrLevel.Reportable.Min());
        Assert.All(CefrLevel.AuthoredLevels, level => Assert.False(level.IsReportableFloor));

        // A result may report the floor ...
        Assert.True(CefrLevel.TryParse(floor.Code, out var reported));
        Assert.Equal(floor, reported);
        // ... but an author may not name it as an item's or a lesson's level.
        Assert.False(CefrLevel.TryParseAuthored(floor.Code, out _));
        Assert.All(CefrLevel.AuthoredLevels, level => Assert.True(CefrLevel.TryParseAuthored(level.Code, out _)));
    }

    [Fact]
    public void No_value_outside_the_closed_set_can_be_constructed()
    {
        Assert.True(typeof(CefrLevel).IsValueType);
        Assert.Empty(typeof(CefrLevel).GetConstructors());

        foreach (var outside in new[] { "A0", "A3", "C3", "D1", "below_A1", "pre_a1 ", "7", "", "  ", null })
        {
            Assert.False(CefrLevel.TryParse(outside, out _), $"'{outside}' must not be a level");
        }

        // The floor is reportable, so only the reported parse accepts it.
        Assert.False(CefrLevel.TryParseAuthored("pre_a1", out _));
        Assert.True(CefrLevel.TryParse("pre_a1", out var floor));
        Assert.True(floor.IsReportableFloor);
        // A band code is a band, not a level: 'below_A1' is the certificate band table's key.
        Assert.False(CefrLevel.TryParse("below_A1", out _));
    }

    [Fact]
    public void Ordering_is_strict_and_total_and_sorting_any_order_yields_the_ladder()
    {
        var shuffled = new[] { CefrLevel.C1, CefrLevel.A2, CefrLevel.C2, CefrLevel.B1, CefrLevel.A1, CefrLevel.B2 };

        Assert.Equal(Ladder, shuffled.Order().Select(level => level.Code));
        Assert.Equal(CefrLevel.A1, shuffled.Min());
        Assert.Equal(CefrLevel.C2, shuffled.Max());
        Assert.True(CefrLevel.C1 > CefrLevel.B2);
        Assert.Equal(0, CefrLevel.B1.CompareTo(CefrLevel.B1));
        Assert.NotEqual(CefrLevel.B1, CefrLevel.B2);

        // Strict on both sides: a level is neither below nor above itself, which is the difference
        // between ordering by rank and merely comparing ranks with a <=. The second operand is parsed
        // back from the code so the compiler cannot fold the comparison as a self-comparison.
        foreach (var level in CefrLevel.Reportable)
        {
            Assert.True(CefrLevel.TryParse(level.Code, out var same));

            Assert.False(level < same, $"{level} must not sort below itself");
            Assert.False(level > same, $"{level} must not sort above itself");
            Assert.True(level <= same);
            Assert.True(level >= same);
        }
    }

    [Fact]
    public void A_certificate_band_and_a_level_cannot_be_converted_to_one_another()
    {
        // A band is a scored range a certificate defines; a level is a measured ability. If either
        // could convert, a TOEIC total of 785 would become "B2" in code, which is exactly the claim
        // this product may not make (D1). A bare IsAssignableFrom would be vacuous here - a struct is
        // never assignable from a class - so this scans the real assemblies for the two things that
        // WOULD make the assignment compile: a conversion operator, or a helper on either side.
        var bandTypes = new[]
        {
            typeof(ScoreBand), typeof(BandCatalog), typeof(CefrBandInfo), typeof(CefrBandInfoDto),
            typeof(TargetBandOption), typeof(TargetBandView), typeof(ScoreBandConfigItem)
        };

        var bridges = new[] { typeof(CefrLevel).Assembly, typeof(CefrBandInfoDto).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => Mentions(method, typeof(CefrLevel)) && bandTypes.Any(band => Mentions(method, band)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToList();

        Assert.Empty(bridges);

        static bool Mentions(MethodInfo method, Type type) =>
            method.ReturnType == type || method.GetParameters().Any(parameter => parameter.ParameterType == type);
    }
}
