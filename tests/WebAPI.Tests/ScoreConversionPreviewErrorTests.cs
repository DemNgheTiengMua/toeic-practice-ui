using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Services;
using WebAPI.Endpoints;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// #146: a skill with no configured scale is an authoring mistake the admin can fix, so the score
/// preview must answer 400 naming the skill. Before this, the ScoreConversionException escaped the
/// endpoint and the admin saw an opaque 500 saying nothing about which skill was unconfigured.
/// <para>
/// Maps the real <c>MapScoreConversion</c> group — the actual endpoint code and its catch — into a
/// minimal app, so the assertion is about production behaviour, not a reimplementation of it.
/// Only the scoring service is replaced.
/// </para>
/// </summary>
public sealed class ScoreConversionPreviewErrorTests
{
    [Fact]
    public async Task An_unconfigured_skill_answers_400_not_500()
    {
        var (client, dispose) = await PreviewAppAsync();
        using (dispose)
        {
            var response = await client.PostAsJsonAsync("/api/admin/score-conversion/preview", PreviewBody());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_refusal_names_the_skill_and_carries_the_score_conversion_code()
    {
        var (client, dispose) = await PreviewAppAsync();
        using (dispose)
        {
            var response = await client.PostAsJsonAsync("/api/admin/score-conversion/preview", PreviewBody());
            var body = await response.Content.ReadAsStringAsync();

            Assert.Contains("SCORE_CONVERSION_INVALID", body, StringComparison.Ordinal);
            Assert.Contains("LIST", body, StringComparison.Ordinal);
        }
    }

    /// <summary>A well-formed preview request; the stub refuses every one.</summary>
    private static ScorePreviewRequestDto PreviewBody() => new(
        1, "TOEIC", "sum",
        [new SkillScoreInputDto(1, "LIST", 50, null)]);

    private static async Task<(HttpClient Client, WebApplication App)> PreviewAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        builder.Services.AddSingleton<IScoreConversionService>(new UnconfiguredSkillService());
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseExceptionHandler();
        // The real endpoint group, unchanged: this is the code whose catch is under test.
        var admin = app.MapGroup("/api/admin");
        typeof(AdminEndpoints)
            .GetMethod("MapScoreConversion", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [admin]);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.First();

        return (new HttpClient { BaseAddress = new Uri(address) }, app);
    }

    /// <summary>Always fails the way an unconfigured skill does — the case the endpoint must catch.</summary>
    private sealed class UnconfiguredSkillService : IScoreConversionService
    {
        public Task<ScorePreviewResult> CalculateScorePreviewAsync(
            ScorePreviewRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ScorePreviewResult>(new ScoreConversionException(
                ScoreConversionException.SkillScaleNotConfigured,
                "Skill 'LIST' has no score scale configured (MinScaledScore, MaxScaledScore and ScoreStep must all be set)."));

        public Task<AdminScoreConversionView> GetScoreConversionAsync(
            string? certCode, int? examId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminScoreConversionView> UpdateScoreConversionAsync(
            int certId, UpdateScoreConversionCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ScorePreviewResult CalculateScorePreview(
            ScorePreviewRequest request,
            IReadOnlyList<ScoreScaleRowItem>? scales = null,
            IReadOnlyList<ScoreBandConfigItem>? bands = null,
            IReadOnlyDictionary<int, (int Min, int Max, int Step)>? skillRanges = null) =>
            throw new NotSupportedException();
    }
}
