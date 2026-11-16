using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Every in-app path the backend hands the learner must name a route the frontend actually serves.
/// <para>
/// <b>What shipped instead, reported on 2026-11-16:</b> a learning path's first step (a concept lesson)
/// opened fine, while the second (a focus-practice drill) answered "Page not found". The backend
/// returned <c>/practice/{sessionId}</c> and <c>/practice/{sessionId}/summary</c>; the frontend serves
/// <c>/practice/session/{sessionId}</c> and <c>/practice/result/{sessionId}</c>. The mock-section step
/// was wrong the same way (<c>/exams/{id}/instructions</c> against a frontend route of
/// <c>/exams/{id}</c>). A learner saw a dead link and could not tell whether the drill existed at all.
/// </para>
/// <para>
/// <b>Why a table here instead of a test per call site:</b> the failure is not one wrong string, it is
/// that nothing checked a returned path against the router. A call-site test would pass again the next
/// time someone adds a path by hand. This walks the router file — the single source of truth the
/// browser navigates by — and asserts each returned path maps onto it.
/// </para>
/// </summary>
public sealed class BackendPathsMatchFrontendRoutesTests
{
    /// <summary>
    /// The in-app paths the backend builds today. Each entry is a template: <c>{x}</c> stands for a
    /// concrete identifier the caller substitutes.
    /// </summary>
    private static readonly string[] BackendPaths =
    [
        // LearningPathService.Steps.cs
        "/practice/session/{sessionId}",
        "/practice/result/{sessionId}",
        "/lessons/{lessonId}",
        "/exams/{examId}",
        // LearnerService.NextAction.cs
        "/placement",
        "/placement/target-band",
        "/learning-path/steps/{stepId}",
        "/practice",
        "/weakness-analysis",
        "/speaking",
        "/writing",
        "/exams",
    ];

    [Fact]
    public void Every_path_the_backend_returns_matches_a_frontend_route()
    {
        var routerPath = Path.Combine(DatabaseFixture.RepoRootPath, "src", "web", "src", "router.tsx");
        Assert.True(File.Exists(routerPath), $"router not found at {routerPath}");

        var routes = Regex
            .Matches(File.ReadAllText(routerPath), @"path:\s*'([^']+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(routes);

        var missing = new List<string>();
        foreach (var template in BackendPaths)
        {
            // "/lesson/{id}" and the router's "/lessons/$lessonId" are the same shape once parameters
            // are normalised, so compare the literal segments only.
            var wanted = Segments(template);
            var matched = routes.Any(route => Segments(route).SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase));
            if (!matched)
            {
                missing.Add($"{template}  (no route with segments [{string.Join(" / ", wanted)}])");
            }
        }

        Assert.True(missing.Count == 0,
            "The backend returns paths the frontend does not serve:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>A path's literal segments, with any parameter placeholder collapsed to one token.</summary>
    private static string[] Segments(string path) =>
        path.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Contains('{') || segment.StartsWith('$') ? "*" : segment)
            .ToArray();
}
