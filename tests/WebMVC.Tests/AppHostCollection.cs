using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// Every test that boots the real application through <c>WebApplicationFactory&lt;Program&gt;</c> joins
/// this one collection, so those hosts are built and run one at a time.
///
/// Why: <c>Program</c> configures the app from process-wide state - environment variables and the
/// shared <c>ApiBaseUrl</c> setting - so two factories running concurrently can see each other's
/// configuration. The symptom is not a compile error but a rare wrong render: a goal screen that
/// occasionally shows a band it should not, roughly one run in eight. Measured on 2026-11-05 by
/// running <c>tests/WebMVC.Tests</c> eight times before and after the permission change that added a
/// third host: 8/8 green before, 7/8 after.
///
/// xUnit runs different collections in parallel, so grouping only these nine still leaves the rest of
/// the assembly concurrent. The serialised portion is small and the flake goes away.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppHostCollection
{
    public const string Name = "AppHost";
}
