using System.Reflection;
using System.Text.Json;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #228 batch one began carrying the level beside the old five-point difficulty; #231 finished the
/// retirement. What survives here is the guard that matters: nothing derives a level from a
/// difficulty, and no wire contract carries a level as a number. The co-existence assertions that
/// proved the two members sat beside each other are gone with the difficulty column — a test that a
/// deleted member exists cannot be kept, only replaced by the scan below.
/// </summary>
public sealed class CefrLevelMigrationTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>The authored ladder, written out here independently of the type's own list.</summary>
    private static readonly string[] Ladder = ["A1", "A2", "B1", "B2", "C1", "C2"];

    private static readonly Dictionary<Type, Func<string, object>> WireContracts = new()
    {
        [typeof(CreateQuestionRequest)] = code => new CreateQuestionRequest(
            GroupId: 1, Stem: "Stem", QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: code),

        [typeof(UpdateQuestionRequest)] = code => new UpdateQuestionRequest(
            GroupId: 1, Stem: "Stem", QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: code),

        [typeof(AdminQuestionDto)] = code => new AdminQuestionDto(
            QuestionId: 1, GroupId: 2, ExamId: 3, SectionId: 4, SectionCode: "R", SectionName: "Reading",
            SkillId: 5, SkillCode: "reading", Stem: "Stem",
            QuestionType: QuestionTypeDto.Mcq, Explanation: null, CategoryId: null, CategoryName: null,
            PrepSeconds: null, ResponseSeconds: null, MinWords: null, MaxWords: null, Hint: null,
            DisplayOrder: 1, UpdatedAt: null, Options: [], HasAnswers: false, CefrLevelCode: code),

        [typeof(ExamStudioQuestionDto)] = code => new ExamStudioQuestionDto(
            QuestionId: 1, GroupId: 2, ExamId: 3, SectionId: 4, SectionCode: "R", SectionName: "Reading",
            SkillId: 5, SkillCode: "reading", Stem: "Stem",
            QuestionType: QuestionTypeDto.Mcq, Explanation: null, CategoryId: null, CategoryCode: null,
            CategoryName: null, PrepSeconds: null, ResponseSeconds: null, MinWords: null, MaxWords: null,
            Hint: null, DisplayOrder: 1, UpdatedAt: null, Options: [], Criteria: [], HasAnswers: false,
            CefrLevelCode: code),
    };

    public static TheoryData<Type> MigratedEntities => new() { typeof(Question), typeof(PathModuleItem) };

    [Theory]
    [MemberData(nameof(MigratedEntities))]
    public void The_entity_carries_a_level_member_and_no_difficulty(Type entity)
    {
        var level = entity.GetProperty("CefrLevel", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(level);
        Assert.Equal(typeof(CefrLevel?), level.PropertyType);

        // #231 deleted the 1-5 member rather than leaving it beside the level. A type that grew one
        // back would reinstate the second scale D1 forbids, so its absence is the guard.
        Assert.Null(entity.GetProperty("DifficultyLevel", BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// Nothing in the four assemblies turns a difficulty into a level. A bare "no operator" check
    /// would be vacuous — the difficulty is an <see cref="int"/>, and an int is legal in a hundred
    /// places — so this scans for the three shapes that WOULD be the mapping: a method that returns a
    /// level and names a difficulty (in its own name, its declaring type, or a parameter), a field or
    /// property typed level whose name mentions a difficulty, and a level-to-difficulty table.
    /// </summary>
    [Fact]
    public void No_code_maps_a_difficulty_to_a_level()
    {
        var assemblies = new[]
        {
            typeof(CefrLevel).Assembly, typeof(CreateQuestionRequest).Assembly,
            typeof(CompanionDbContext).Assembly, typeof(Services.AdminExamService).Assembly,
        };

        var offenders = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(Bridges)
            .ToList();

        // A scan that found no CefrLevel mention at all proves nothing; the type is used in all four.
        Assert.Contains(assemblies, assembly =>
            assembly.GetTypes().Any(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance).Any(p => Includes(p.PropertyType, typeof(CefrLevel)))));
        Assert.Empty(offenders);

        static IEnumerable<string> Bridges(Type type)
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(All))
            {
                var producesLevel = Includes(method.ReturnType, typeof(CefrLevel));
                var namesDifficulty = NamesDifficulty(method.Name) || NamesDifficulty(type.Name)
                    || method.GetParameters().Any(p => NamesDifficulty(p.Name));

                if (producesLevel && namesDifficulty)
                {
                    yield return $"{type.FullName}.{method.Name}()";
                }
            }

            foreach (var property in type.GetProperties(All).Cast<MemberInfo>()
                .Concat(type.GetFields(All)))
            {
                var holdsLevel = property switch
                {
                    PropertyInfo p => Includes(p.PropertyType, typeof(CefrLevel)),
                    FieldInfo f => Includes(f.FieldType, typeof(CefrLevel)),
                    _ => false,
                };

                if (holdsLevel && NamesDifficulty(property.Name))
                {
                    yield return $"{type.FullName}.{property.Name}";
                }
            }
        }
    }

    private static bool Includes(Type type, Type target) =>
        type == target
        || Nullable.GetUnderlyingType(type) == target
        || type.GetGenericArguments().Any(argument => Includes(argument, target));

    private static bool NamesDifficulty(string? name) =>
        name?.Contains("ifficulty", StringComparison.OrdinalIgnoreCase) == true;

    [Fact]
    public void Every_level_round_trips_through_the_wire_by_name()
    {
        var transmitted = new List<string>();

        foreach (var (contract, build) in WireContracts)
        {
            foreach (var level in CefrLevel.AuthoredLevels)
            {
                var json = JsonSerializer.Serialize(build(level.Code), contract, Wire);

                // On the wire the value is the code, as a string: "B1", never 4 and never "4".
                Assert.Contains($"\"cefrLevelCode\":\"{level.Code}\"", json, StringComparison.Ordinal);

                var back = JsonSerializer.Deserialize(json, contract, Wire)!;
                var code = (string?)contract.GetProperty("CefrLevelCode")!.GetValue(back);

                // ... and it reads back as the same level, not merely the same string.
                Assert.True(CefrLevel.TryParseAuthored(code, out var parsed));
                Assert.Equal(level, parsed);

                transmitted.Add(code!);
            }
        }

        // Drift: the set the four contracts can carry is exactly the authored ladder. A level added
        // to the type that no contract can transmit lands here, as does a contract that drops one.
        Assert.Equal(4, WireContracts.Count);
        Assert.Equal(
            Ladder,
            transmitted.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_wire_contract_carries_the_level_as_a_number()
    {
        // A level typed int or an enum with an underlying number is a second 1-6 scale wearing the
        // level's name. This is the check that would pass a "mechanical retype" that only renamed the
        // member.
        var levelled = typeof(CreateQuestionRequest).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.Name.Contains("CefrLevel", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Without this, an empty match set would make the assertion below vacuously true.
        Assert.True(levelled.Count > 0, "no wire property carries a level; the scan found the wrong assembly");

        var offenders = levelled
            .Where(property => property.PropertyType != typeof(string))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}:{property.PropertyType.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// #229 added the level column; #231 removed the integer beside it. What remains worth guarding is
    /// that the level is a mapped string code and the difficulty is gone from the model entirely — a
    /// model that still mapped a 1-5 column would let a writer put a rank back where a level belongs.
    /// </summary>
    [Fact]
    public void The_question_level_is_mapped_and_no_difficulty_is()
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True;")
            .Options;

        using var db = new CompanionDbContext(options);

        var question = db.Model.FindEntityType(typeof(Question));
        Assert.NotNull(question);
        Assert.Null(question.FindProperty("DifficultyLevel"));
        Assert.NotNull(question.FindProperty("CefrLevel"));

        // A converted nullable struct: the column is a string code, not a second integer scale. Without
        // this the mapping would compile and store a rank, which is the exact defect D1 forbids.
        var mapped = question.FindProperty("CefrLevel")!;
        Assert.Equal(typeof(CefrLevel?), mapped.ClrType);
        Assert.Equal("varchar(10)", mapped.GetColumnType());
        Assert.Equal(typeof(string), mapped.GetValueConverter()!.ProviderClrType);

        // PathModuleItem has no level column: #231 removed the difficulty rather than inventing one, and
        // the level a step is prescribed at arrives with the path rebuild (#232).
        var pathItem = db.Model.FindEntityType(typeof(PathModuleItem));
        Assert.NotNull(pathItem);
        Assert.Null(pathItem.FindProperty("DifficultyLevel"));
        Assert.Null(pathItem.FindProperty("CefrLevel"));
    }
}
