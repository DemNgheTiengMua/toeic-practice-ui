using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>Outbound mail. The dev implementation drops each message in a folder; tests use a
/// recording fake. A real SMTP or provider adapter is a new implementation, nothing else changes.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
