using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>The one failure every <see cref="IEmailSender"/> raises when a message can't go out,
/// whatever the transport, so callers never catch transport-specific exceptions.</summary>
public sealed class EmailDeliveryException(string message, Exception inner) : Exception(message, inner);
