using System.Reflection;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;
using Xunit.Abstractions;

namespace Services.Tests;

/// <summary>
/// Removing a duplicated index attribute must not remove the index. This asserts the EF model still
/// carries all ten by name, so the dedupe deleted the *copy* and kept the declaration.
/// </summary>
public sealed class IndexDeclarationTests(ITestOutputHelper output)
{
    private static readonly string[] Deduped =
    [
        "UQ_Topics_Id_Modality",
        "IX_Items_Cell",
        "IX_Items_Selection",
        "IX_PracticeAnswers_Question",
        "UQ_ItemVersions_Item_Number",
        "UQ_ExamItems_Exam_Section_Order",
        "IX_RSD_Batch_Item",
        "IX_UCM_User_Due",
        "UQ_QGroups_Id_Cert",
        "IX_LSS_UserCert",
    ];

    [Fact]
    public void Every_index_whose_attribute_was_removed_is_still_in_the_model()
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=EeIndexProbe;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var db = new CompanionDbContext(options);

        var present = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetIndexes())
            .Select(i => i.GetDatabaseName())
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Deduped.Where(n => !present.Contains(n)).ToList();
        output.WriteLine($"model has {present.Count} named indexes");

        Assert.Empty(missing);
    }
}
