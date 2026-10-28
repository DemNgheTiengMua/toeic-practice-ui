using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public sealed record FileStreamResultInfo(Stream Stream, string ContentType, string FileName) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}
