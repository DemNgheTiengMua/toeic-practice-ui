namespace BusinessObjects;

/// <summary>
/// #289: the skill scope chosen when starting a placement sitting.
/// First-time placements normally use <see cref="AllSkills"/>, while a learner retaking
/// placement may choose either all four skills or the faster Listening &amp; Reading path.
/// </summary>
public enum PlacementScope
{
    AllSkills,
    ListeningAndReading,
}
