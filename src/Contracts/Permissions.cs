namespace Contracts;

/// <summary>D16 / ADM-01: the five composable staff namespaces, and the exact codes the spec names.
/// A role holds any combination; every staff gate names one of these instead of a role code. The
/// schema's <c>CK_Permissions_Code</c> is the other copy of this list and must stay in step.</summary>
public static class Permissions
{
    public const string ContentAuthor = "pool.content.author";

    public const string ContentReview = "pool.content.review";

    public const string BlueprintDesign = "assessment.blueprint.design";

    public const string SupportTriage = "operations.support.triage";

    public const string SystemAdmin = "system.governance.admin";

    public static IReadOnlyList<string> All { get; } =
        [ContentAuthor, ContentReview, BlueprintDesign, SupportTriage, SystemAdmin];
}
