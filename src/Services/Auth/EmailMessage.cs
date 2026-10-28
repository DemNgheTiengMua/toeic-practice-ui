using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed record EmailMessage(string To, string Subject, string Body);
