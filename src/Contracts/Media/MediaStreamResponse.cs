namespace Contracts;

/// <summary>A media body relayed from the API: 200 with the whole file, or 206 with the requested range.</summary>
public sealed record MediaStreamResponse(Stream Stream, string ContentType, int StatusCode, string? ContentRange, long? ContentLength);
