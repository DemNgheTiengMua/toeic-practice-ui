using System;
using System.Collections.Generic;

namespace Services;

/// <summary>
/// #198: the seam's own rules - the ones no gate owns, because they are about the file rather than
/// about an item.
/// </summary>
public static class IngestionRules
{
    /// <summary>The format's version is one this importer reads.</summary>
    public const string SchemaVersion = "ingest.schema_version";

    /// <summary>The file is for one skill, and it is the skill the upload names (CNT-02).</summary>
    public const string Skill = "ingest.skill";

    /// <summary>The record's id prefix encodes its content tier, and the two must agree (CNT-03).</summary>
    public const string TierPrefix = "ingest.tier_prefix";

    /// <summary>A record is missing a field the pool needs to hold it at all.</summary>
    public const string RecordShape = "ingest.record.shape";

    /// <summary>This seam does not ingest the record's kind - a productive task needs its rubric.</summary>
    public const string UnsupportedRecord = "ingest.record.unsupported";
}

/// <summary>
/// #198: one record's verdict. <see cref="Line"/> is where the record opens in the uploaded file,
/// because "record 247 is wrong" is not actionable in a 300-record document and "line 812" is.
/// </summary>
public sealed record IngestionRecordResult(
    int Line,
    string RecordId,
    bool Accepted,
    IReadOnlyList<IngestionRefusal> Refusals)
{
    /// <summary>
    /// The sentence a workbench shows in one table row: the first rule that failed, or an empty string.
    /// <see cref="Refusals"/> keeps the whole set so a remediating author fixes everything in one pass
    /// rather than one rule per upload.
    /// </summary>
    public string Message => Refusals.Count == 0 ? string.Empty : Refusals[0].Message;
}

/// <summary>One rule, broken. <see cref="Rule"/> is a gate's own code or one of <see cref="IngestionRules"/>.</summary>
public sealed record IngestionRefusal(string Rule, string Message);

/// <summary>
/// #198: what one upload produced. <see cref="Accepted"/> and <see cref="Rejected"/> are disjoint and
/// together account for every record in the file, which is what makes "a bad line does not lose the
/// good ones" checkable rather than asserted.
/// </summary>
public sealed record SkillContentPoolIngestionResult(
    string Skill,
    string? BatchTag,
    IReadOnlyList<IngestionRecordResult> Records,
    int ItemsWritten,
    int VersionsWritten)
{
    public IReadOnlyList<IngestionRecordResult> Accepted
    {
        get
        {
            var accepted = new List<IngestionRecordResult>();
            foreach (var record in Records)
            {
                if (record.Accepted)
                {
                    accepted.Add(record);
                }
            }

            return accepted;
        }
    }

    public IReadOnlyList<IngestionRecordResult> Rejected
    {
        get
        {
            var rejected = new List<IngestionRecordResult>();
            foreach (var record in Records)
            {
                if (!record.Accepted)
                {
                    rejected.Add(record);
                }
            }

            return rejected;
        }
    }
}
