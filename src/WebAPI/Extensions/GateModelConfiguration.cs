using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace WebAPI.Extensions;

/// <summary>
/// #258: which language models the content gates are configured with, resolved from configuration
/// rather than hard-coded.
/// <para>
/// <b>Why this exists as its own type.</b> Before this, <c>PoolIngestionGates</c> was registered with
/// no arguments at all, so it received no verifier and no classifier in every deployment — gate three
/// could never run, and gate four always reported "no level classifier is configured". Both studios
/// hid that by filtering the refusals out as "checks that did not run", so an author saw a working
/// screen over a pipeline that certified nothing.
/// </para>
/// <para>
/// <b>Two distinct families or none.</b> <see cref="GateThree"/> compares declared family names and
/// refuses an item whose author, verifier one and verifier two are not three different families.
/// Registering the same family twice would satisfy a count while giving one opinion twice, which is
/// the failure the rule exists to catch — so a single configured family yields <b>no</b> verifiers at
/// all rather than one. <c>HasNoVerifiers</c> then stays true and the screens keep saying so.
/// </para>
/// </summary>
public sealed record GateModelConfiguration
{
    /// <summary>The sentence a deployment needs to read when the gates are not wired.</summary>
    public const string Remedy =
        "Configure Claude:GateThree:VerifierFamilies with two distinct family names and "
        + "Claude:GateFour:ClassifierFamily with one, then restart.";

    private GateModelConfiguration(IReadOnlyList<string> verifierFamilies, string? classifierFamily)
    {
        VerifierFamilies = verifierFamilies;
        ClassifierFamily = classifierFamily;
    }

    /// <summary>The distinct verifier families, in configured order. Empty when fewer than two were given.</summary>
    public IReadOnlyList<string> VerifierFamilies { get; }

    /// <summary>The classifier's family, or null when none is configured.</summary>
    public string? ClassifierFamily { get; }

    /// <summary>True when gate three has two distinct families to compare.</summary>
    public bool IsGateThreeEnabled => VerifierFamilies.Count >= 2;

    /// <summary>True when both gates can actually run.</summary>
    public bool IsFullyConfigured => IsGateThreeEnabled && ClassifierFamily is not null;

    /// <summary>
    /// Reads the configuration. Blank entries are dropped and duplicates collapse case-insensitively,
    /// so <c>["opus", "OPUS"]</c> is one family - and one family is not enough.
    /// </summary>
    public static GateModelConfiguration Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var families = (configuration.GetSection("Claude:GateThree:VerifierFamilies").Get<string[]>() ?? [])
            .Where(family => !string.IsNullOrWhiteSpace(family))
            .Select(family => family.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var classifier = configuration["Claude:GateFour:ClassifierFamily"];
        var classifierFamily = string.IsNullOrWhiteSpace(classifier) ? null : classifier.Trim();

        // Fewer than two distinct families means gate three cannot judge, so it is given none. A single
        // verifier would make `HasNoVerifiers` true anyway, and passing it would only move the refusal
        // from "did not run" to "families not distinct" - two sentences for one deployment mistake.
        return new GateModelConfiguration(
            families.Count >= 2 ? families : [],
            classifierFamily);
    }
}
