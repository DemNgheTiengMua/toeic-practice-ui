using System;
using DataAccess;

namespace Services;

public sealed partial class AttemptEngine(CompanionDbContext db, IFileStore? fileStore = null) : IAttemptEngine
{
    private readonly IFileStore _fileStore = fileStore ?? new LocalFileStore(Path.Combine(AppContext.BaseDirectory, "storage"));

    /// <summary>
    /// #212: the placement snapshot's one writer, for a sitting that produced one. Constructed on the
    /// same context because it must write inside this engine's transaction - a separate context would
    /// take a second connection and deadlock on the row this one is holding.
    /// </summary>
    internal IPlacementResultWriter PlacementResults { get; } = new PlacementResultWriter(db);
}
