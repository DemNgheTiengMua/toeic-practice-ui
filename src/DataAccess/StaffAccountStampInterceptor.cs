using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DataAccess;

/// <summary>
/// #201 / ADM-02 / ADM-03: stamps the acting staff account onto every write to a guarded table, so the
/// two database triggers have something true to read.
///
/// This exists because the guard would otherwise be dead in production: a trigger that refuses a
/// triage-only account by reading <c>LastWrittenBy</c> refuses nothing at all if no code ever assigns
/// it. Stamping it here rather than in each service means the next endpoint someone writes inherits
/// the declaration - which is the whole point ADM-04 makes about a controller-only check.
///
/// A write with no acting account (a seed, a fixture, a migration, a background worker) stamps
/// nothing. That is not a bypass: NULL names no account, so no invariant can be violated by it.
/// </summary>
public sealed class StaffAccountStampInterceptor(IActingStaffAccount acting) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> GuardedTypes =
    [
        typeof(ItemVersion),
        typeof(ExamItem),
        typeof(Section),
        typeof(GradingCriterion),
        typeof(GradingCriterionDescriptor),
        // Review finding: ADM-03's "exam structure" is not only the assembly rows. A triage-only
        // account could publish an exam and activate an item, so `Exams.Status` and
        // `Items.LifecycleStatus` are guarded too and must be stamped here, or every application
        // write to them would be refused for declaring nobody.
        typeof(Exam),
        typeof(Item),
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private const string StampProperty = "LastWrittenBy";

    private void Stamp(DbContext? context)
    {
        if (context is null || acting.UserId is not int userId)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)
                || !GuardedTypes.Any(t => t.IsInstanceOfType(entry.Entity)))
            {
                continue;
            }

            var property = entry.Property(StampProperty);
            property.CurrentValue = userId;

            // An UPDATE must ASSIGN the column even when the value is unchanged: the trigger's
            // UPDATE(LastWrittenBy) check reads whether the statement declared an actor, not whether
            // the value moved. A row that already names an author is re-declared by whoever writes it
            // next, so nobody rides on an author who did not make this edit.
            if (entry.State == EntityState.Modified)
            {
                property.IsModified = true;
            }
        }
    }
}
