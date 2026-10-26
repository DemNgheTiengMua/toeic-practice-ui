using System;
using DataAccess;

namespace Services;

public sealed partial class AttemptEngine(CompanionDbContext db, IFileStore? fileStore = null) : IAttemptEngine
{
    private readonly IFileStore _fileStore = fileStore ?? new LocalFileStore(Path.Combine(AppContext.BaseDirectory, "storage"));
}
