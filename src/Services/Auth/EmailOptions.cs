using System.Text;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Where <see cref="DropFolderEmailSender"/> writes messages. Relative paths resolve
    /// against the API's content root.</summary>
    public string DropFolder { get; set; } = "mail-drop";

    public string From { get; set; } = "no-reply@extra-efficient.local";
}
