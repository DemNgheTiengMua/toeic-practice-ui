using Contracts;

namespace WebMVC.Models;

public sealed record ProfilePageViewModel(ProfileViewModel Form, bool IsStaff)
{
    public AudioEquipmentCheckModel Equipment { get; } = new(Title: "Audio & Equipment");

    /// <summary>Set when the profile could not be loaded at all, so the form is hidden rather than
    /// shown blank — a blank form would overwrite the stored profile on save.</summary>
    public string? LoadError { get; init; }

    /// <summary>#242 / LRN-09: the one place a certificate changes. Null for staff (who have no
    /// active certificate) and when the shell could not be loaded, in which case the card is not
    /// rendered at all rather than rendered with an empty choice.</summary>
    public IReadOnlyList<CertificateOptionDto>? Certificates { get; init; }

    public string? ActiveCertCode { get; init; }
}
