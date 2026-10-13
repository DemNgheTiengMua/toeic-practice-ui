using System.Text.RegularExpressions;

namespace Services.Tests;

/// <summary>Records mail instead of sending it. The only double for <see cref="IEmailSender"/>.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new EmailDeliveryException("Mail drop unavailable.", new IOException());
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }

    public IEnumerable<EmailMessage> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));

    /// <summary>The token from the last link sent to <paramref name="email"/> on <paramref name="path"/>.</summary>
    public string LastToken(string email, string path)
    {
        var message = To(email).Last(m => m.Body.Contains(path, StringComparison.Ordinal));
        var match = Regex.Match(message.Body, Regex.Escape(path) + @"\?token=([A-Za-z0-9_\-]+)");
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No {path} link in: {message.Body}");
    }
}

/// <summary>A clock the test moves by hand, so expiry is observable without sleeping.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
