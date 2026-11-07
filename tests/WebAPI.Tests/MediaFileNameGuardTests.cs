using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// The media filename guard. It lived only on the imports route; the exams route accepted anything the
/// route could carry, so a traversal name reached <c>LocalFileStore</c> with only the store's own check
/// behind it. Both routes now share this one rule.
/// </summary>
public sealed class MediaFileNameGuardTests
{
    /// <summary>A name the store can have produced: 32 hex characters and an allow-listed extension.</summary>
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.mp3")]
    [InlineData("0123456789abcdef0123456789abcdef.wav")]
    [InlineData("0123456789abcdef0123456789abcdef.jpg")]
    [InlineData("0123456789abcdef0123456789abcdef.jpeg")]
    [InlineData("0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.mp3")]
    public void A_name_the_store_generates_is_accepted(string fileName) =>
        Assert.True(EndpointHelpers.IsGeneratedName(fileName));

    /// <summary>
    /// Each of these passes a weaker guard. `../` needs no hex body at all; a 32-character stem with a
    /// traversal tail defeats a "long enough and has an extension" check; `.txt` defeats an extension
    /// check that only looks for a dot.
    /// </summary>
    [Theory]
    [InlineData("../media-secret/leak.txt")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("0123456789abcdef0123456789abcdef.mp3/../../secret.mp3")]
    [InlineData("0123456789abcdef0123456789abcde.mp3")]      // 31 hex: one short
    [InlineData("0123456789abcdef0123456789abcdefg.mp3")]     // 32 chars, one not hex
    [InlineData("0123456789abcdef0123456789abcdef.exe")]      // extension not served
    [InlineData("0123456789abcdef0123456789abcdef")]          // no extension at all
    [InlineData("")]
    [InlineData(null)]
    public void A_name_the_store_could_not_have_generated_is_refused(string? fileName) =>
        Assert.False(EndpointHelpers.IsGeneratedName(fileName));
}
