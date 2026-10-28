using BusinessObjects;

namespace Services;

public sealed class PackageNotFoundException(int packageId)
    : Exception($"Package '{packageId}' was not found.")
{
    public int PackageId { get; } = packageId;
}
