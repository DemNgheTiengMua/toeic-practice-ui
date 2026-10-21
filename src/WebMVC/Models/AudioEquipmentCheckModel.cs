namespace WebMVC.Models;

public sealed record AudioEquipmentCheckModel(
    bool ShowInput = true,
    bool ShowOutput = true,
    bool Compact = false,
    string? Title = null);
