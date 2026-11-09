using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureViews(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<vw_MentorAvgRating>(entity =>
        {
            entity.ToView("vw_MentorAvgRating");
        });



        modelBuilder.Entity<vw_MentorPayoutTotal>(entity =>
        {
            entity.ToView("vw_MentorPayoutTotals");
        });



        modelBuilder.Entity<vw_UserCategoryAccuracy>(entity =>
        {
            entity.ToView("vw_UserCategoryAccuracy");
        });



        modelBuilder.Entity<vw_UserCreditBalance>(entity =>
        {
            entity.ToView("vw_UserCreditBalance");
        });



        modelBuilder.Entity<vw_UserCreditSummary>(entity =>
        {
            entity.ToView("vw_UserCreditSummary");
        });



        modelBuilder.Entity<vw_UserPurchaseTotal>(entity =>
        {
            entity.ToView("vw_UserPurchaseTotals");
        });



        modelBuilder.Entity<vw_UserSectionAccuracy>(entity =>
        {
            entity.ToView("vw_UserSectionAccuracy");
        });



        modelBuilder.Entity<vw_UserSectionAccuracyAll>(entity =>
        {
            entity.ToView("vw_UserSectionAccuracyAll");
        });



        // #202: S-01's matrix. Keyless and read-only like every other view here; the deficit is the
        // database's arithmetic, never recomputed in C#.
        modelBuilder.Entity<vw_CoverageMatrix>(entity =>
        {
            entity.ToView("vw_CoverageMatrix");
        });

        // #235: the daily ceiling's own arithmetic, so no service recomputes it (D12 / NFI-05).
        modelBuilder.Entity<vw_DailyPracticeVolume>(entity =>
        {
            entity.ToView("vw_DailyPracticeVolume");
        });

        // #243 / D3: accuracy per (topic, level). Keyless and read-only like every other view here;
        // the percentage is the database's arithmetic, never recomputed in C#.
        modelBuilder.Entity<vw_UserTopicAccuracy>(entity =>
        {
            entity.ToView("vw_UserTopicAccuracy");
        });
    }

}
