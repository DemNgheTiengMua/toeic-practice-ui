namespace WebMVC.Models;

public sealed record ProfilePageViewModel(ProfileViewModel Form, bool IsAdmin)
{
    public AudioEquipmentCheckModel Equipment { get; } = new(Title: "Audio & Equipment");
}
