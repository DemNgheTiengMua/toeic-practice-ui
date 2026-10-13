namespace WebMVC.Models;

/// <param name="EmailVerified">Live from the API; null when the API couldn't be reached.</param>
/// <param name="Activation">"sent" / "failed" right after a resend, otherwise null.</param>
public sealed record DashboardViewModel(CurrentUser User, bool? EmailVerified, string? Activation)
{
    public bool ShowVerificationReminder => EmailVerified == false;
}
