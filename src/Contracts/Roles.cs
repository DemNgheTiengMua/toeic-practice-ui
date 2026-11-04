namespace Contracts;

/// <summary>The role codes the system knows. One home so a rename is a one-line change instead
/// of a hunt across Services, both apps, the seed script and the tests.
/// <para>
/// D16 / ADM-01: <see cref="Admin"/> is no longer a gate. Staff authority is the five
/// <see cref="Permissions"/> namespaces, and every staff route names one. The code stays because
/// <c>Users.RoleId</c> and the seeded row carry it, and because the cookie re-validation compares
/// the live role against the one it issued; using it to decide what an account may <em>do</em>
/// would reintroduce the job-title check this ticket removed.
/// </para></summary>
public static class Roles
{
    public const string Learner = "learner";

    public const string Admin = "admin";
}
