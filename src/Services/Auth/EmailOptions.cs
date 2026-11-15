using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>ADR-0018: which <see cref="IEmailSender"/> the API wires. <c>dropfolder</c> writes
    /// .eml files and sends nothing (tests, offline dev, the manual-test scripts); <c>smtp</c> posts
    /// to a real relay so an activation link reaches an inbox. Chosen by configuration so the test
    /// suite never touches the network.</summary>
    public string Transport { get; set; } = "dropfolder";

    public bool UsesSmtp => string.Equals(Transport, "smtp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where <see cref="DropFolderEmailSender"/> writes messages. Relative paths resolve
    /// against the API's content root.</summary>
    public string DropFolder { get; set; } = "mail-drop";

    public string From { get; set; } = "no-reply@extra-efficient.local";
}
