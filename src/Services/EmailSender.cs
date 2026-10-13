using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>The one failure every <see cref="IEmailSender"/> raises when a message can't go out,
/// whatever the transport, so callers never catch transport-specific exceptions.</summary>
public sealed class EmailDeliveryException(string message, Exception inner) : Exception(message, inner);

/// <summary>Outbound mail. The dev implementation drops each message in a folder; tests use a
/// recording fake. A real SMTP or provider adapter is a new implementation, nothing else changes.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Where <see cref="DropFolderEmailSender"/> writes messages. Relative paths resolve
    /// against the API's content root.</summary>
    public string DropFolder { get; set; } = "mail-drop";

    public string From { get; set; } = "no-reply@extra-efficient.local";
}

/// <summary>Writes each message as a .eml file (opens in any mail client) and logs where it went,
/// so a demo can follow an activation or reset link without a mail server.</summary>
public sealed class DropFolderEmailSender(string folder, string from, ILogger<DropFolderEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await WriteAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EmailDeliveryException($"Could not write mail to {folder}.", ex);
        }
    }

    private async Task WriteAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        var eml = new StringBuilder()
            .Append("From: ").Append(from).Append("\r\n")
            .Append("To: ").Append(message.To).Append("\r\n")
            .Append("Subject: ").Append(message.Subject).Append("\r\n")
            .Append("Date: ").Append(DateTimeOffset.UtcNow.ToString("r")).Append("\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n")
            .Append("\r\n")
            .Append(message.Body)
            .ToString();
        await File.WriteAllTextAsync(path, eml, new UTF8Encoding(false), cancellationToken);
        logger.LogInformation("Mail to {To} ({Subject}) written to {Path}", message.To, message.Subject, path);
    }
}
