namespace BusinessObjects;

/// <summary>
/// #190: the two content tiers, as one vocabulary. Mirrors <c>CK_Items_ContentTier</c> and
/// <c>CK_Exams_ContentTier</c>, which both accept exactly these two.
/// <para>
/// A string rather than an enum on purpose: <see cref="Item.ContentTier"/> and
/// <see cref="Exam.ContentTier"/> are stored as codes and read straight through into the admin DTOs,
/// and an enum here would add a second vocabulary to keep in step with the first. What this type buys
/// is the one thing neither column can say — which of the two a caller means when it says nothing.
/// </para>
/// </summary>
public static class ContentTiers
{
    public const string Production = "production";

    public const string Demo = "demo";

    public static bool IsKnown(string? tier) =>
        string.Equals(tier, Production, StringComparison.Ordinal)
        || string.Equals(tier, Demo, StringComparison.Ordinal);
}
