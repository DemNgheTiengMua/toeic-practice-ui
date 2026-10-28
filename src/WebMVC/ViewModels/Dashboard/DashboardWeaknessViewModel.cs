using Contracts;
namespace WebMVC.Models;

public sealed record DashboardWeaknessViewModel(WeaknessStripDto Strip, string CertificateCode)
{
    public bool IsUntested => Strip.Tier == "UNTESTED" || Strip.Answered == 0;
    public string BadgeClass => Strip.Tier switch { "CRITICAL" => "badge--danger", "BORDER" => "badge--warning", "STABLE" => "badge--info", "MASTERY" => "badge--success", _ => "badge--muted" };
    public string BarClass => Strip.Tier switch { "CRITICAL" => "progress__bar--danger", "BORDER" => "progress__bar--warning", "MASTERY" => "progress__bar--success", _ => "" };
    public string ScopeEyebrow => Strip.Scope == "category" ? $"CATEGORY · {Strip.Code} · {Strip.SectionName}" : $"{(CertificateCode == "TOEIC" ? "PART" : "SECTION")} {Strip.Code}";
}
