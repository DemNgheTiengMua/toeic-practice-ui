using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace DataAccess;

public partial class CompanionDbContext
{
    private static void ConfigureViews(ModelBuilder modelBuilder)
    {


        modelBuilder.Entity<vw_LearnerPathIsPremium>(entity =>
        {
            entity.ToView("vw_LearnerPathIsPremium");

            entity.Property(e => e.LearnerPathId).ValueGeneratedOnAdd();
        });



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
    }
}
