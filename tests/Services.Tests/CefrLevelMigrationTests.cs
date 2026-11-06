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
/// #228 batch one: a domain entity and a wire contract begin carrying the level beside the old
/// five-point difficulty. The column and the integer member stay until the contract ticket, so the
/// guards here are co-existence, fidelity, and — the one that matters — that nothing derives one from
/// the other. A <c>DifficultyLevel 5 → C2</c> mapping looks like a finished retype and is the
/// invention D1 forbids.
/// </summary>
public sealed class CefrLevelMigrationTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>The authored ladder, written out here independently of the type's own list.</summary>
    private static readonly string[] Ladder = ["A1", "A2", "B1", "B2", "C1", "C2"];

    private static readonly Dictionary<Type, Func<string, object>> WireContracts = new()
    {
        [typeof(CreateQuestionRequest)] = code => new CreateQuestionRequest(
            GroupId: 1, Stem: "Stem", DifficultyLevel: 3, QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: code),

        [typeof(UpdateQuestionRequest)] = code => new UpdateQuestionRequest(
            GroupId: 1, Stem: "Stem", DifficultyLevel: 3, QuestionType: QuestionTypeDto.Mcq,
            CefrLevelCode: code),

        [typeof(AdminQuestionDto)] = code => new AdminQuestionDto(
            QuestionId: 1, GroupId: 2, ExamId: 3, SectionId: 4, SectionCode: "R", SectionName: "Reading",
            SkillId: 5, SkillCode: "reading", Stem: "Stem", DifficultyLevel: 3,
            QuestionType: QuestionTypeDto.Mcq, Explanation: null, CategoryId: null, CategoryName: null,
            PrepSeconds: null, ResponseSeconds: null, MinWords: null, MaxWords: null, Hint: null,
            DisplayOrder: 1, UpdatedAt: null, Options: [], HasAnswers: false, CefrLevelCode: code),

        [typeof(ExamStudioQuestionDto)] = code => new ExamStudioQuestionDto(
            QuestionId: 1, GroupId: 2, ExamId: 3, SectionId: 4, SectionCode: "R", SectionName: "Reading",
            SkillId: 5, SkillCode: "reading", Stem: "Stem", DifficultyLevel: 3,
            QuestionType: QuestionTypeDto.Mcq, Explanation: null, CategoryId: null, CategoryCode: null,
            CategoryName: null, PrepSeconds: null, ResponseSeconds: null, MinWords: null, MaxWords: null,
            Hint: null, DisplayOrder: 1, UpdatedAt: null, Options: [], Criteria: [], HasAnswers: false,
            CefrLevelCode: code),
    };

    public static TheoryData<Type> MigratedEntities => new() { typeof(Question), typeof(PathModuleItem) };

    [Theory]
    [MemberData(nameof(MigratedEntities))]
    public void The_entity_carries_a_level_member_beside_the_difficulty(Type entity)
    {
        var level = entity.GetProperty("CefrLevel", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(level);
        Assert.Equal(typeof(CefrLevel?), level.PropertyType);

        // The old member still compiles beside it, so nothing downstream has to change yet.
        var difficulty = entity.GetProperty("DifficultyLevel", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(difficulty);
        Assert.Contains(difficulty.PropertyType, new[] { typeof(int), typeof(int?) });
    }

    /// <summary>
    /// The level is a fact of its own, not a projection of the difficulty, in either direction. This
    /// fails the moment one member is computed from the other — the lossy 1..5 → A1..C1 mapping that
    /// would look like a mechanical retype and would invent a level the author never chose.
    /// </summary>
    [Fact]
    public void A_level_and_a_difficulty_never_derive_from_one_another()
    {
        var questions = new Question { DifficultyLevel = 5 };
        Assert.Null(questions.CefrLevel);

        questions.CefrLevel = CefrLevel.A1;
        Assert.Equal(5, questions.DifficultyLevel);

        questions.DifficultyLevel = 1;
        Assert.Equal(CefrLevel.A1, questions.CefrLevel);

        var item = new PathModuleItem { DifficultyLevel = 5 };
        Assert.Null(item.CefrLevel);

        item.CefrLevel = CefrLevel.C2;
        Assert.Equal(5, item.DifficultyLevel);
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
        var offenders = typeof(CreateQuestionRequest).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.Name.Contains("CefrLevel", StringComparison.OrdinalIgnoreCase))
            .Where(property => property.PropertyType != typeof(string))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}:{property.PropertyType.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The schema keeps <c>Questions.DifficultyLevel</c> and gains no level column until the contract
    /// ticket, so a mapped level member would make every save demand a column that is not there.
    /// </summary>
    [Fact]
    public void The_entity_level_member_is_not_mapped_to_a_column_that_does_not_exist()
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Trusted_Connection=True;")
            .Options;

        using var db = new CompanionDbContext(options);

        foreach (var entity in new[] { typeof(Question), typeof(PathModuleItem) })
        {
            var model = db.Model.FindEntityType(entity);
            Assert.NotNull(model);
            Assert.NotNull(model.FindProperty("DifficultyLevel"));
            Assert.Null(model.FindProperty("CefrLevel"));
        }
    }
}
