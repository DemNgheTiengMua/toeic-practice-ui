using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

internal static class ItemVersionQuery
{
    public static T? Newest<T>(IEnumerable<T> versions, Func<T, int> versionNumber) where T : class =>
        versions.OrderByDescending(versionNumber).FirstOrDefault();

    public static IReadOnlyList<T> NewestPerItem<T>(
        IEnumerable<T> versions, Func<T, int> itemId, Func<T, int> versionNumber) where T : class =>
        versions
            .GroupBy(itemId)
            .Select(g => g.OrderByDescending(versionNumber).First())
            .ToList();

    public static Task<Stimulus?> FindStimulusAsync(
        CompanionDbContext db, int stimulusId, CancellationToken cancellationToken) =>
        db.Stimuli.AsNoTracking().FirstOrDefaultAsync(s => s.StimulusId == stimulusId, cancellationToken);
}
