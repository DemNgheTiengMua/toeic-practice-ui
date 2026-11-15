using System.Net;
using System.Net.Mail;
using System.Text;

namespace Services;

/// <summary>
/// ADR-0018: real outbound mail over SMTP, so an activation link reaches an actual inbox.
/// <para>
/// <see cref="SmtpClient"/> is in the BCL and is NOT obsolete on .NET 10 (verified: no
/// <c>[Obsolete]</c> attribute, and <c>SendMailAsync(MailMessage, CancellationToken)</c> exists), so
/// this needs no package. It sends with a user name and password, which every transactional relay
/// offers; it does not do OAuth, so a mailbox requiring that needs a different sender, not a change
/// to this one.
/// </para>
/// <para>
/// Every failure is rethrown as <see cref="EmailDeliveryException"/> so callers see one exception
/// type regardless of transport, and so a credential mistake is logged with its SMTP status rather
/// than surfacing as a bare SmtpException in the request log.
/// </para>
/// </summary>
public sealed class SmtpEmailSender(SmtpEmailOptions options, string from) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            // Explicit credentials, never the process account: a container has no interactive user.
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(options.UserName, options.Password),
            Timeout = options.TimeoutSeconds * 1000,
        };

        using var mail = new MailMessage(from, message.To, message.Subject, message.Body)
        {
            BodyEncoding = new UTF8Encoding(false),
            SubjectEncoding = new UTF8Encoding(false),
            // Plain text on purpose: the bodies are built as text in AuthService, and a text part
            // alone is far less likely to be filed as spam than an html/plain multipart.
            IsBodyHtml = false,
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away; not a delivery fault, and not worth wrapping.
            throw;
        }
        catch (Exception ex)
        {
            throw new EmailDeliveryException(
                $"SMTP delivery to {options.Host}:{options.Port} failed for {message.To}: {ex.Message}", ex);
        }
    }
}

/// <summary>ADR-0018: where <see cref="SmtpEmailSender"/> connects. Bound from the <c>Email:Smtp</c>
/// section, so a relay can be swapped by configuration and nothing in code changes.</summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "";

    /// <summary>587 is the submission port every relay publishes for STARTTLS.</summary>
    public int Port { get; set; } = 587;

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    /// <summary>
    /// True = STARTTLS (RFC 3207): connect unencrypted, then upgrade. This is the ONLY encrypted mode
    /// <see cref="SmtpClient"/> supports; it cannot do implicit TLS (SMTPS), so port 465 will not work
    /// — use 587 instead. False sends credentials in clear text, which only a local test sink
    /// (MailHog on 1025) should ever accept.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>True when enough is configured to attempt a send. A transport chosen without these
    /// fails the boot rather than every registration at run time.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Port 465 is implicit TLS (SMTPS), which <see cref="SmtpClient"/> cannot speak: it only
    /// does STARTTLS. Left alone this fails as an opaque timeout on every send, so it is caught at
    /// boot with the fix in the message.</summary>
    public bool UsesUnsupportedPort => Port == 465;
}
