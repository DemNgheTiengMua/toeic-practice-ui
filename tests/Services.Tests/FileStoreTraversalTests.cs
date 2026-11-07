using Services;

namespace Services.Tests;

/// <summary>
/// Path traversal in <see cref="LocalFileStore"/>. The guard compared the resolved path against the
/// base directory as a plain string prefix, with no trailing separator, so a sibling directory whose
/// name merely *starts with* the base was readable: base <c>media</c> also matched
/// <c>media-secret\leak.txt</c>. Proved by experiment before the fix.
/// </summary>
public sealed class FileStoreTraversalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eefs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _base;
    private readonly string _sibling;

    public FileStoreTraversalTests()
    {
        _base = Path.Combine(_root, "media");
        _sibling = Path.Combine(_root, "media-secret");
        Directory.CreateDirectory(_base);
        Directory.CreateDirectory(_sibling);
        File.WriteAllText(Path.Combine(_sibling, "leak.txt"), "SECRET");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A failed cleanup must not fail the test that proved the refusal.
        }
    }

    /// <summary>
    /// The defect, as a test. A sibling directory that shares the base's name as a prefix was reachable.
    /// A guard that only checks `StartsWith(base)` passes `Inside_the_base_is_still_readable` and fails
    /// this one, which is the point: the accepting control below would not catch it on its own.
    /// </summary>
    [Theory]
    [InlineData("../media-secret/leak.txt")]
    [InlineData("..\\media-secret\\leak.txt")]
    [InlineData("media/../../media-secret/leak.txt")]
    public async Task A_sibling_directory_that_shares_the_bases_prefix_is_not_readable(string relativePath)
    {
        IFileStore store = new LocalFileStore(_base);

        var info = await store.GetMediaAsync(relativePath);

        Assert.Null(info);
    }

    /// <summary>
    /// The accepting control. Without it a store that refused every path would pass the theory above
    /// and destroy media serving entirely.
    /// </summary>
    [Fact]
    public async Task A_file_inside_the_base_is_still_readable()
    {
        File.WriteAllText(Path.Combine(_base, "clip.mp3"), "audio");
        IFileStore store = new LocalFileStore(_base);

        var info = await store.GetMediaAsync("clip.mp3");

        Assert.NotNull(info);
        info!.Stream.Dispose();
    }

    /// <summary>A nested path inside the base is also legitimate: imports/{batchId}/ and exams/{id}/ are
    /// both two levels deep.</summary>
    [Fact]
    public async Task A_nested_path_inside_the_base_is_still_readable()
    {
        var nested = Path.Combine(_base, "exams", "7");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "clip.mp3"), "audio");
        IFileStore store = new LocalFileStore(_base);

        var info = await store.GetMediaAsync("exams/7/clip.mp3");

        Assert.NotNull(info);
        info!.Stream.Dispose();
    }

    /// <summary>A climb that leaves the root entirely is refused too, so the fix is not merely the
    /// sibling case.</summary>
    [Fact]
    public async Task A_climb_above_the_root_is_refused()
    {
        IFileStore store = new LocalFileStore(_base);

        var info = await store.GetMediaAsync("../../../../etc/passwd");

        Assert.Null(info);
    }
}
