using Contracts;
using System.Text.Json;
using WebMVC.Models;
using System.Text.RegularExpressions;

namespace WebMVC.Tests;

public sealed class PlacementSittingViewTests
{
    [Fact]
    public void View_and_partials_only_render_markup_and_reference_external_assets()
    {
        var root = TestPathHelper.FindRepoRoot();
        var directory = Path.Combine(root, "src", "WebMVC", "Views", "Placement");
        var view = File.ReadAllText(Path.Combine(directory, "Take.cshtml"));
        var partials = Directory.GetFiles(directory, "_Take*.cshtml");
        Assert.NotEmpty(partials);
        var markup = view + string.Join("\n", partials.Select(File.ReadAllText));

        Assert.Contains("~/js/placement/take.js", view);
        Assert.Contains("~/css/placement/take.css", view);
        Assert.Contains("type=\"application/json\" id=\"placement-take-data\"", markup);
        Assert.DoesNotMatch(@"<style\b|\bstyle\s*=|\bon(?:click|change|submit|load)\s*=", markup);
        Assert.DoesNotMatch(@"@(inject|functions)\b|\bnew\s+|\b(?:Math|JsonSerializer)\.|\.(?:GroupBy|Select|Where|OrderBy|TakeLast|Sum|Max|FirstOrDefault)\s*\(", markup);
        foreach (Match script in Regex.Matches(markup, @"<script\b(?<attributes>[^>]*)>(?<body>[\s\S]*?)</script>"))
        {
            var attributes = script.Groups["attributes"].Value;
            Assert.True(attributes.Contains("src=") || attributes.Contains("type=\"application/json\""),
                "Executable scripts must live in external JavaScript files.");
        }
    }

    [Theory]
    [InlineData(1, 3, null, "/placement/take/42?q=2")]
    [InlineData(2, 3, "/placement/take/42?q=1", "/placement/take/42?q=3")]
    [InlineData(3, 3, "/placement/take/42?q=2", null)]
    public void Model_preserves_question_navigation_boundaries(
        int order, int total, string? previous, string? next)
    {
        var take = Take(Question() with { DisplayOrder = order }, totalQuestions: total);
        var model = new PlacementTakeViewModel(take, "Save failed");

        Assert.Same(take, model.Take);
        Assert.Equal(previous, model.PreviousUrl);
        Assert.Equal(next, model.NextUrl);
        Assert.Equal("/placement/take/42/submit", model.SubmitUrl);
        Assert.True(model.HasError);
        Assert.Equal("Save failed", model.ErrorMessage);
    }

    [Theory]
    [InlineData("AUDIO_DRIVEN", 2, false, true)]
    [InlineData("AUDIO_DRIVEN", 1, false, true)]
    [InlineData("SYSTEM_PACED_AUTO", 4, false, true)]
    [InlineData("SYSTEM_PACED_AUTO", 1, false, true)]
    [InlineData("FULL_SANDBOX", 2, true, false)]
    [InlineData("UNIT_LOCKED_SANDBOX", 3, true, false)]
    [InlineData("HYBRID_GROUP_LOCKED", 2, true, false)]
    [InlineData(null, 2, true, false)]
    public void Model_never_offers_previous_in_one_way_navigation_modes(
        string? navigationMode, int order, bool canGoPrevious, bool hidePrevious)
    {
        // D3 invariant: placement sittings mirror ExamTakeQuestionPresentation — an AUDIO_DRIVEN or
        // SYSTEM_PACED_AUTO question is one-way regardless of how far into the set it sits.
        var take = Take(Question() with { DisplayOrder = order, NavigationMode = navigationMode },
            totalQuestions: 10);
        var model = new PlacementTakeViewModel(take);

        Assert.Equal(canGoPrevious, model.CanGoPrevious);
        Assert.Equal(hidePrevious, model.HidePrevious);
        Assert.Equal(canGoPrevious ? $"/placement/take/42?q={order - 1}" : null, model.PreviousUrl);
        Assert.Equal("/placement/take/42?q=" + (order + 1), model.NextUrl);
    }

    [Fact]
    public void Model_exposes_the_confirmed_display_order_for_client_history_reconciliation()
    {
        var model = new PlacementTakeViewModel(Take(Question() with { DisplayOrder = 4 }));
        using var json = JsonDocument.Parse(model.PageDataJson);
        Assert.Equal(4, json.RootElement.GetProperty("displayOrder").GetInt32());
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(1, 8, 12)]
    [InlineData(8, 8, 100)]
    public void Model_preserves_progress_rounding_and_empty_denominator(int answered, int total, int expected)
    {
        var model = new PlacementTakeViewModel(Take(totalQuestions: total, answeredCount: answered));
        Assert.Equal(expected, model.ProgressPercent);
        using var json = JsonDocument.Parse(model.PageDataJson);
        Assert.Equal(expected, json.RootElement.GetProperty("progressPercent").GetInt32());
    }

    [Fact]
    public void Model_groups_navigator_in_display_order_and_preserves_selection_and_phase_states()
    {
        var question = Question() with { SelectedOptionId = 12, IsFlagged = true };
        var take = Take(question, navigator:
        [
            new(20, 3, 2, "R1", ModalityDto.Reading, false, false, false, true),
            new(11, 2, 1, "L1", ModalityDto.Listening, false, true, false),
            new(10, 1, 1, "L1", ModalityDto.Listening, true, true, true)
        ]) with { Phases = [new(1, "listening", "Listening", 1, 0, 600), new(2, "reading", "Reading", 2, 600, 600)] };
        var model = new PlacementTakeViewModel(take);

        Assert.Equal(2, model.NavigatorGroups.Count);
        var listening = model.NavigatorGroups[0];
        Assert.Equal("Listening", listening.Title);
        Assert.Equal("1–2", listening.RangeLabel);
        Assert.Equal("Questions 1 to 2", listening.AriaLabel);
        Assert.Equal([1, 2], listening.Items.Select(item => item.DisplayOrder));
        Assert.Equal("navigator__cell is-current is-answered is-marked", listening.Items[0].CssClass);
        Assert.Equal("Question 1 answered", listening.Items[0].AriaLabel);
        Assert.Equal("true", listening.Items[0].AriaCurrent);
        Assert.Equal("/placement/take/42?q=1", listening.Items[0].Url);
        Assert.Equal("navigator__cell is-unanswered is-marked", listening.Items[1].CssClass);
        Assert.False(listening.Items[1].IsLocked);
        Assert.Equal("Reading", model.NavigatorGroups[1].Title);
        Assert.True(model.NavigatorGroups[1].Items[0].IsLocked);
        Assert.Contains("is-locked", model.NavigatorGroups[1].Items[0].CssClass);
        Assert.False(model.Options[0].IsSelected);
        Assert.True(model.Options[1].IsSelected);
        Assert.Equal("is-selected", model.Options[1].CssClass);
        Assert.Equal("true", model.FlagPressed);
        Assert.Equal("btn--primary", model.FlagButtonClass);
        Assert.Equal("Flagged for review", model.FlagLabel);
        Assert.True(model.ShowPhases);
        Assert.Equal("step", model.Phases[0].AriaCurrent);
        Assert.Equal("false", model.Phases[1].AriaCurrent);
    }

    [Fact]
    public void Page_data_round_trips_hostile_audio_paths_without_closing_the_json_island()
    {
        const string path = "/audio/quote'\"</script><script>alert('x')</script>&.mp3";
        var question = Question() with { AudioPath = path, AudioStartMs = 125, AudioEndMs = null };
        var model = new PlacementTakeViewModel(Take(question));

        Assert.DoesNotContain("<", model.PageDataJson);
        Assert.DoesNotContain("&", model.PageDataJson);
        using var json = JsonDocument.Parse(model.PageDataJson);
        Assert.Equal(path + ":125-0", json.RootElement.GetProperty("audioKey").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("placementId").GetInt32());
        Assert.Equal(10, json.RootElement.GetProperty("questionId").GetInt32());
        Assert.Equal(1200, json.RootElement.GetProperty("remainingSeconds").GetInt32());
        Assert.False(json.RootElement.GetProperty("isFlagged").GetBoolean());
        Assert.NotNull(model.AudioPlayer);
        Assert.Equal(path, model.AudioPlayer.AudioPath);
        Assert.Equal("exam", model.AudioPlayer.Mode);
        Assert.False(model.AudioPlayer.AllowReplay);
        Assert.Equal(125, model.AudioPlayer.StartMs);
        Assert.Null(model.AudioPlayer.EndMs);
    }

    [Theory]
    [InlineData("writing", true)]
    [InlineData("free_response", true)]
    [InlineData("mcq", false)]
    public void Model_keeps_writing_and_optional_content_visibility(string questionType, bool writing)
    {
        var model = new PlacementTakeViewModel(Take(Question() with { QuestionType = questionType }));
        Assert.Equal(writing, model.IsWriting);
        Assert.False(model.HasPassage);
        Assert.False(model.HasImage);
        Assert.Equal("Question illustration", model.ImageAlt);
        Assert.Null(model.AudioPlayer);
        Assert.Empty(model.Phases);
        Assert.False(model.ShowPhases);
        Assert.Empty(model.NavigatorGroups);
    }

    [Fact]
    public void Sitting_markup_and_script_keep_draft_flag_submit_and_audio_endpoints()
    {
        var root = TestPathHelper.FindRepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "wwwroot", "js", "placement", "take.js"));
        Assert.Contains("JSON.parse(dataIsland().textContent)", script);
        Assert.Contains("/placement/take/${config.placementId}/draft", script);
        Assert.Contains("/placement/take/${config.placementId}/answers/${config.questionId}", script);
        Assert.Contains("RequestVerificationToken: token", script);
        Assert.Contains("responseText: text", script);
        Assert.Contains("await saveDraftNow()", script);
        Assert.Contains("#is-auto-submit-input", script);
        Assert.Contains("examaudiotimeupdate", script);
        Assert.Contains("examaudioended", script);
        Assert.Contains("ee_placement_audio_", script);
        Assert.DoesNotContain("@take.", script);
        Assert.DoesNotContain("@q", script);
        Assert.Contains("/placement/take/${config.placementId}/fragment", script);
        Assert.Contains("history.pushState", script);
        // History must follow the question the server confirmed, never the one the client asked for.
        Assert.Contains("syncUrl(config.displayOrder", script);
        Assert.DoesNotContain("?q=${targetQuestion}", script);
        Assert.Contains("addEventListener(\"popstate\"", script);
        Assert.Contains("is-marked", script);
        var components = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "wwwroot", "css", "components.css"));
        Assert.Contains(".navigator__cell.is-marked", components);

        var navigator = File.ReadAllText(Path.Combine(root, "src", "WebMVC", "Views", "Placement", "_TakeNavigator.cshtml"));
        Assert.Contains("cell.IsLocked", navigator);
        Assert.Contains("aria-disabled=\"true\"", navigator);

        var mapper = File.ReadAllText(Path.Combine(root, "src", "WebAPI", "Endpoints", "PlacementEndpoints.DtoMapping.cs"));
        Assert.Contains("n.IsLocked", mapper);
        var sitting = File.ReadAllText(Path.Combine(root, "src", "WebAPI", "Endpoints", "PlacementEndpoints.Sitting.cs"));
        Assert.Equal(2, Regex.Matches(sitting, "AttemptEngineException.WrongPhase").Count);
    }

    private static AttemptQuestionDto Question() => new(
        10, 1, 3, 1, "L1", "Listening section", "listening", "Listening", ModalityDto.Listening,
        "Choose an answer.", null, null, null, null, null, null, null,
        [new(11, "A", "First"), new(12, "B", "Second")], null, false);

    private static PlacementTakeViewDto Take(
        AttemptQuestionDto? question = null,
        IReadOnlyList<NavigatorItemDto>? navigator = null,
        int totalQuestions = 3,
        int answeredCount = 1) => new(
            42, 100, 7, "Placement exam", "TOEIC", PlacementStatusDto.InProgress,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(20), 1200,
            totalQuestions, answeredCount, 0, question ?? Question(), navigator ?? [], false);
}
