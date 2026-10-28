
namespace BusinessObjects;

// Domain enums for the schema's CHECK-constrained codes (issue #2: enums for status strings).
// Entities stay string, as scaffolded; Services converts with EnumCodes, where each member's
// snake_case name is the database code. Keep members in step with docs/database-schema.sql
// and with the wire mirrors (<Name>Dto) in Contracts/Enums.cs.

/// <summary>CK_Skills_Modality: listening, reading, speaking, writing.</summary>
public enum Modality
{
    Listening,
    Reading,
    Speaking,
    Writing,
}
