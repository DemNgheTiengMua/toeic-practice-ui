namespace WebMVC.Models;

public sealed record ProfilePageViewModel(ProfileViewModel Form, bool IsAdmin)
{
    public AudioEquipmentCheckModel Equipment { get; } = new(Title: "Audio & Equipment");

    /// <summary>Set when the profile could not be loaded at all, so the form is hidden rather than
    /// shown blank — a blank form would overwrite the stored profile on save.</summary>
    public string? LoadError { get; init; }
}
