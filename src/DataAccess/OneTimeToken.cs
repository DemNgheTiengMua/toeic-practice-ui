namespace DataAccess.Scaffold;

/// <summary>The shape EmailVerificationTokens and PasswordResetTokens share: a hashed, expiring,
/// single-use link for one user. Lives outside the scaffold so re-scaffolding keeps it.</summary>
public interface IOneTimeToken
{
    int UserId { get; set; }

    byte[] TokenHash { get; set; }

    DateTimeOffset ExpiresAt { get; set; }

    DateTimeOffset? UsedAt { get; set; }

    User User { get; set; }
}

public partial class EmailVerificationToken : IOneTimeToken;

public partial class PasswordResetToken : IOneTimeToken;
