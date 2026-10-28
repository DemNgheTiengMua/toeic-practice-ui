using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

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
