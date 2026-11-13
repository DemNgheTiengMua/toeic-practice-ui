using System.Text.RegularExpressions;
using Contracts;
using WebMVC.Models;

namespace WebMVC.Tests;

/// <summary>
/// #221 / D18: the audio equipment gate.
/// <para>
/// Every test here names the input that would pass a weaker version while being wrong. The three
/// that matter most:
/// </para>
/// <list type="number">
/// <item>
/// A gate that always renders the microphone panel passes "the gate exists" and asks a reading-only
/// paper for a permission it will never use. <see cref="A_reading_only_placement_offers_no_microphone_and_no_audio_control"/>
/// pins the null.
/// </item>
/// <item>
/// A take page that calls getUserMedia passes every copy assertion and still puts a permission dialog
/// over a running clock. <see cref="No_take_page_can_raise_a_permission_prompt"/> asserts the call site
/// is absent, not merely that the copy is right.
/// </item>
/// <item>
/// Three states rendered by swapping a class on one block pass "different class per state" and read
/// identically. <see cref="The_three_permission_states_have_different_copy_and_different_remedies"/>
/// asserts the actual strings.
/// </item>
/// </list>
public sealed class AudioEquipmentGateTests
{
    private static string RepoRoot => TestPathHelper.FindRepoRoot();

    private static string View(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "WebMVC", "Views", relative));

    /// <summary>
    /// The microphone panel and the three-state block it includes. The state copy lives in one partial
    /// so a settings screen and the pre-sitting screen cannot drift, so a test that read only the host
    /// would be asserting an empty shell.
    /// </summary>
    private static string MicrophoneView() =>
        View("Shared/_AudioEquipmentCheck.cshtml") + "\n" + View("Shared/_AudioPermissionStates.cshtml");

    /// <summary>
    /// Reads a browser script with its line endings normalised to LF.
    /// <para>
    /// <b>Why normalise.</b> A checkout on Windows writes CRLF while the file in the repo is LF, so a
    /// regex written against <c>\n</c> matches on one machine and not the other. That is not a code
    /// defect and it must not read as one: this exact regex passed on the #221 branch and failed the
    /// moment the branch was merged on a Windows checkout. Normalising here means every assertion in
    /// this file depends on the script's content, not on the platform that cloned it.
    /// </para>
    /// </summary>
    private static string Script(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "WebMVC", "wwwroot", "js", relative))
            .Replace("\r\n", "\n", StringComparison.Ordinal);


    // ---------------------------------------------------------------- scoping

    /// <summary>
    /// The criterion most likely to be faked: a gate that always asks passes every other one. A
    /// placement whose offers carry neither a speaking nor a listening skill must render no
    /// equipment panel at all - not an empty one, and not one with the microphone hidden by CSS.
    /// </summary>
    [Theory]
    [InlineData("reading", false, false)]
    [InlineData("writing", false, false)]
    [InlineData("listening", false, true)]
    [InlineData("speaking", true, false)]
    public void Placement_preflight_scopes_equipment_to_the_skills_it_will_measure(
        string modality, bool expectMic, bool expectAudio)
    {
        var gate = Gate(modality, requiresMic: expectMic, requiresAudio: expectAudio);
        var model = new PlacementPreflightPageViewModel(gate);

        if (!expectMic && !expectAudio)
        {
            Assert.Null(model.AudioEquipment);
            return;
        }

        Assert.NotNull(model.AudioEquipment);
        Assert.Equal(expectMic, model.AudioEquipment!.ShowInput);
        Assert.Equal(expectAudio, model.AudioEquipment.ShowOutput);
        // The pre-sitting surface is where a permission may legitimately be requested.
        Assert.False(model.AudioEquipment.Sitting);
    }

    /// <summary>
    /// The wrong-but-passing input: a gate derived from the certificate code rather than from the
    /// paper. A certificate that examines speaking but places reading-only must still show nothing.
    /// </summary>
    [Fact]
    public void A_reading_placement_on_a_certificate_that_also_examines_speaking_still_asks_nothing()
    {
        // RequiresMic/RequiresAudio are computed from the offers that must be MEASURED; a reusable
        // speaking skill contributes nothing, so both are false here even though the certificate has
        // a speaking row.
        var gate = Gate("reading", requiresMic: false, requiresAudio: false,
            offers:
            [
                Offer("READ", "reading", mic: false, audio: false),
                Offer("SPEA", "speaking", mic: true, audio: false, canReuse: true),
            ]);

        var model = new PlacementPreflightPageViewModel(gate);

        Assert.Null(model.AudioEquipment);
    }

    [Fact]
    public void The_preflight_view_renders_the_panel_only_when_the_model_has_one()
    {
        var view = View("Placement/Preflight.cshtml");

        Assert.Matches(
            @"@if \(Model\.AudioEquipment is not null\)\s*\{\s*<partial name=""_AudioEquipmentCheck"" model=""Model\.AudioEquipment"" />",
            view);
    }

    // --------------------------------------------------- no prompt inside a sitting

    /// <summary>
    /// Reading the copy is not enough: a take page that calls getUserMedia passes every string
    /// assertion and still breaks the learner's attempt with a system dialog over the clock.
    /// <para>
    /// This asserts the call site is unreachable from the take-page scripts. `acquire` is the
    /// non-prompting path (it returns "blocked" without calling getUserMedia unless the permission is
    /// already granted); `ask` is the prompting path and may only appear in a settings or pre-sitting
    /// script. `exams/preflight.js` is listed because it is the same shell a candidate reaches from a
    /// sitting; it is a pre-sitting screen and prompts through the gate, never directly.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("exams/speaking.js")]
    public void No_take_page_script_calls_the_prompting_path(string script)
    {
        var source = Script(script);

        Assert.DoesNotContain("EeAudioGate.ask(", source);
        Assert.DoesNotContain(".ask(", source);
        // The one prompting call is inside the gate module, which a take page cannot reach directly.
        Assert.DoesNotContain("mediaDevices.getUserMedia(", source);
    }

    /// <summary>
    /// `exams/preflight.js` is a pre-sitting screen, so it MAY prompt - but only through the gate, so
    /// the state it renders is the browser's answer rather than an error name.
    /// </summary>
    [Fact]
    public void The_preflight_screen_prompts_only_through_the_gate()
    {
        var source = Script("exams/preflight.js");

        Assert.DoesNotContain("mediaDevices.getUserMedia(", source);
        Assert.Contains("EeAudioGate", source);
    }

    [Fact]
    public void The_gate_module_is_the_only_place_that_can_prompt()
    {
        var gate = Script("audio-gate.js");
        Assert.Contains("async function ask(", gate);
        Assert.Contains("async function acquire(", gate);
        Assert.Contains("mediaDevices.getUserMedia", gate);

        // `acquire` must refuse before it calls getUserMedia; the guard is what makes it non-prompting.
        var acquire = Regex.Match(gate, @"async function acquire\((?<body>[\s\S]*?)\n  \}\n");
        Assert.True(acquire.Success, "acquire() must exist");
        Assert.Contains("current.state !== GRANTED", acquire.Groups["body"].Value);
    }

    /// <summary>
    /// The engine's per-item speaking capture is the other take-page prompt site. It used to call
    /// getUserMedia directly; it now goes through the gate.
    /// </summary>
    [Fact]
    public void The_exam_shell_speaking_capture_goes_through_the_gate()
    {
        var source = Script("exams/speaking.js");
        Assert.DoesNotContain("await navigator.mediaDevices.getUserMedia", source);
        Assert.Contains("window.EeAudioGate.acquire(", source);
    }

    [Fact]
    public void The_sitting_surfaces_mark_their_equipment_panel_as_a_sitting()
    {
        var exam = new ExamTakeViewModel(TakeFixture(modality: ModalityDto.Speaking)).Display;
        Assert.True(exam.AudioEquipment?.Sitting);

        var practice = new PracticeTakePresentation(
            PracticeSession(ModalityDto.Listening),
            PracticeQuestion(audioPath: "/audio/x.mp3"),
            null, null);
        Assert.True(practice.AudioCheck.Sitting);
    }

    /// <summary>
    /// A sitting surface reports what the browser decided but never offers a control that asks. The
    /// host decides via `promptable`, and the three-state partial renders the two controls only when
    /// it is given `true`.
    /// </summary>
    [Fact]
    public void A_sitting_panel_renders_no_control_that_could_ask()
    {
        var host = View("Shared/_AudioEquipmentCheck.cshtml");
        var states = View("Shared/_AudioPermissionStates.cshtml");

        Assert.Contains("var promptable = !Model.Sitting;", host);
        Assert.Contains("data-permission-promptable=\"@(promptable ? \"true\" : \"false\")\"", host);
        Assert.Contains("<partial name=\"_AudioPermissionStates\" model=\"promptable\" />", host);

        // Both prompting controls are gated on the model in the partial, so a sitting (false) renders
        // neither. A control left outside an `@if (Model)` would reach a sitting.
        Assert.Matches(
            @"@if \(Model\)\s*\{\s*<button[^>]*data-audio-action=""request-permission""", states);
        Assert.Matches(
            @"@if \(Model\)\s*\{\s*<button[^>]*data-audio-action=""retry-permission""", states);
    }

    /// <summary>
    /// The pre-sitting screen is one of the two surfaces allowed to prompt, so it passes `true`; a
    /// screen that passed `false` would show the states with no way to fix any of them.
    /// </summary>
    [Fact]
    public void The_pre_sitting_screens_pass_true_to_the_state_panel()
    {
        var preflight = View("Exams/_PreflightMicrophone.cshtml");
        Assert.Contains("<partial name=\"_AudioPermissionStates\" model=\"true\" />", preflight);
        Assert.Contains("data-permission-promptable=\"true\"", preflight);

        // Placement preflight renders the equipment component, which is promptable off a sitting.
        var placement = View("Placement/Preflight.cshtml");
        Assert.Contains("<partial name=\"_AudioEquipmentCheck\" model=\"Model.AudioEquipment\" />", placement);
    }

    // -------------------------------------------------------- three distinct states

    /// <summary>
    /// The trap: browsers report "denied" for "never asked" in some paths, so a state derived from
    /// an error name, from `Notification.permission`, or from an absent `navigator.mediaDevices` lies.
    /// The state is read from `navigator.permissions.query({name:"microphone"})`, and when that API is
    /// unavailable the answer is "never asked" - not "denied".
    /// </summary>
    [Fact]
    public void The_gate_reads_permission_state_and_never_infers_it_from_an_error()
    {
        var gate = Script("audio-gate.js");

        Assert.Contains("permissions.query({ name: \"microphone\" })", gate);
        // No inference from an error name anywhere.
        Assert.DoesNotContain("name === \"NotAllowedError\"", gate);
        Assert.DoesNotContain("Notification.permission", gate);
        // An unsupported permissions API falls back to the last recorded answer, then "never asked".
        Assert.Contains("return { state: readCached() || NEVER_ASKED, measurable: false };", gate);
    }

    /// <summary>
    /// The durable record. localStorage survives a reload; a cookie would be sent to the server (and
    /// D18 forbids a server-side permission record it cannot observe). Asserted by key, because a
    /// state that is only in memory disappears on the reload between the pre-sitting screen and the
    /// sitting itself.
    /// </summary>
    [Fact]
    public void Permission_state_is_recorded_in_local_storage_not_in_a_cookie()
    {
        var gate = Script("audio-gate.js");

        Assert.Contains("var STORAGE_KEY = \"ee_microphone_preference_permission\";", gate);
        Assert.Contains("window.localStorage.setItem(STORAGE_KEY", gate);
        Assert.DoesNotContain("document.cookie", gate);
        // Nothing here may put a permission on the wire: device choice stays in the browser.
        Assert.DoesNotContain("fetch(", gate);
    }

    /// <summary>
    /// The criterion is that the copy differs, not that a class differs. Each state's own sentence is
    /// asserted, and the denied state must name the browser and the exact control path.
    /// </summary>
    [Fact]
    public void The_three_permission_states_have_different_copy_and_different_remedies()
    {
        var view = MicrophoneView();

        var neverAsked = Block(view, "never-asked");
        var denied = Block(view, "denied");
        var granted = Block(view, "granted");

        // 1. Never asked: the absence of an answer, and a control that says what it does.
        Assert.Contains("Microphone permission: not asked yet.", neverAsked);
        Assert.Contains("Nothing has been refused", neverAsked);
        Assert.Contains("Enable the microphone to see device names", neverAsked);

        // 2. Denied: names the browser and the exact control path, plus a retry.
        Assert.Contains("Microphone permission: blocked.", denied);
        Assert.Contains("I changed it — check again", denied);
        Assert.Contains("data-denied-browser=\"chrome\"", denied);
        Assert.Contains("Site settings", denied);
        Assert.Contains("data-denied-browser=\"firefox\"", denied);
        Assert.Contains("Privacy &amp; Security → Permissions → Microphone", denied);
        Assert.Contains("data-denied-browser=\"safari\"", denied);
        Assert.Contains("Settings for This Website → Microphone", denied);
        Assert.Contains("data-denied-browser=\"edge\"", denied);
        Assert.Contains("Permissions for this site", denied);

        // 3. Granted: a positive statement, not the absence of a warning.
        Assert.Contains("Microphone permission: granted.", granted);

        // No two states reuse the same headline: a shared headline would let a class-only change pass.
        var headlines = new[] { neverAsked, denied, granted }
            .Select(block => Regex.Match(block, @"<strong>(?<h>[^<]+)</strong>").Groups["h"].Value)
            .ToList();
        Assert.Equal(3, headlines.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The wrong-but-passing input: a denied state that says "allow the microphone" without naming
    /// the browser or the control path. That sentence is true, unhelpful, and passes every assertion
    /// about "a remedy exists".
    /// <para>
    /// So each named browser block is checked on its own for a concrete control path, not just for
    /// being present: one good block plus four placeholder blocks would otherwise pass.
    /// </para>
    /// </summary>
    [Fact]
    public void The_denied_state_never_falls_back_to_a_generic_allow_the_microphone_sentence()
    {
        var denied = Block(MicrophoneView(), "denied");

        // Each named browser carries a real control path of its own.
        var paths = new Dictionary<string, string>
        {
            ["chrome"] = "Site settings",
            ["edge"] = "Permissions for this site",
            ["firefox"] = "Privacy &amp; Security → Permissions → Microphone",
            ["safari"] = "Settings for This Website → Microphone",
            ["opera"] = "Site settings",
        };

        foreach (var (browser, path) in paths)
        {
            var block = BrowserBlock(denied, browser);
            Assert.Contains(path, block);
            Assert.Contains("reload this page", block);
            // A block that only says "allow the microphone" names no control, so it fails here.
            Assert.DoesNotContain(">allow the microphone", block);
        }

        // The fallback for an unrecognised browser still names where to look, rather than a bare
        // instruction that would send the learner hunting.
        var unknown = BrowserBlock(denied, "unknown");
        Assert.Contains("address bar or settings", unknown);
        Assert.Contains("reload this page", unknown);
    }

    /// <summary>One `data-denied-browser="…"` block, so a good block cannot cover for an empty one.</summary>
    private static string BrowserBlock(string denied, string browser)
    {
        var start = denied.IndexOf($"data-denied-browser=\"{browser}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The denied state must carry a data-denied-browser=\"{browser}\" block.");
        var next = denied.IndexOf("data-denied-browser=\"", start + 1, StringComparison.Ordinal);
        var end = next >= 0 ? next : denied.Length;
        return denied[start..end];
    }

    [Fact]
    public void The_denied_remedy_names_a_browser_that_the_gate_can_detect()
    {
        var view = MicrophoneView();
        var gate = Script("audio-gate.js");

        foreach (var browser in new[] { "chrome", "edge", "firefox", "safari", "opera", "unknown" })
        {
            Assert.Contains($"data-denied-browser=\"{browser}\"", view);
            if (browser != "unknown")
            {
                Assert.Contains($"return \"{browser}\";", gate);
            }
        }
    }

    // ------------------------------------------------------ silent microphone blocks

    /// <summary>
    /// A muted microphone records the right duration of silence, so "did we capture three seconds"
    /// answers a different question. The gate measures the analyser's peak; this asserts the measure
    /// exists and that duration is not what decides.
    /// </summary>
    [Fact]
    public void Silence_is_decided_by_measured_level_not_by_capture_duration()
    {
        var audio = Script("ee-audio.js");

        Assert.Contains("function measureSignal(", audio);
        Assert.Contains("analyser.getFloatTimeDomainData(samples)", audio);
        // -45 dBFS is the same voice threshold the live meter prints.
        Assert.Contains("if (peak > -45) return finish(\"signal\");", audio);
        Assert.Contains("if (Date.now() >= deadline) return finish(\"silent\");", audio);
        // The browser cannot measure is a third outcome, and it does not block.
        Assert.Contains("finish(\"unavailable\")", audio);
        Assert.Contains("if (measurement.outcome === \"unavailable\")", audio);
    }

    /// <summary>
    /// A silent microphone blocks by default. The override is not a bare checkbox: it states the
    /// consequence before the choice, so a learner cannot tick it without having been told.
    /// </summary>
    [Fact]
    public void The_silent_override_states_the_consequence_before_the_checkbox()
    {
        var view = MicrophoneView();
        var silent = SilentGate(view);

        Assert.Contains("No signal was detected from this microphone.", silent);
        Assert.Contains("the speaking skill will very", silent);
        Assert.Contains("likely be reported as <strong>not measured</strong>", silent);
        Assert.Contains("computed", silent);
        Assert.Contains("from the remaining skills", silent);
        Assert.Contains("This cannot be undone after you submit.", silent);

        // The stated cost precedes the control, which is what "in advance" means.
        var costAt = silent.IndexOf("the speaking skill will very", StringComparison.Ordinal);
        var boxAt = silent.IndexOf("data-audio-role=\"silent-override\"", StringComparison.Ordinal);
        Assert.True(costAt >= 0 && boxAt > costAt,
            "The consequence must be stated before the override control, not after it.");
    }

    [Fact]
    public void A_silent_microphone_blocks_the_sitting_by_default()
    {
        var audio = Script("ee-audio.js");

        // Default is blocked; only a ticked override releases it.
        Assert.Contains("var blocked = !(accepted && accepted.checked);", audio);
        Assert.Contains("component.setAttribute(\"data-audio-blocked\", blocked ? \"true\" : \"false\");", audio);
    }

    // ------------------------------------------------- the device-list split

    /// <summary>
    /// D18's "naming and availability are different failures": granted with an empty list is "plug one
    /// in"; labels withheld is "enable the microphone". Collapsing both into "no devices found" is the
    /// defect this replaces, and it is invisible to a test that only counts rendered alerts.
    /// </summary>
    [Fact]
    public void Granted_with_no_device_and_labels_withheld_are_two_different_sentences()
    {
        var view = MicrophoneView();

        Assert.Contains("data-audio-role=\"device-hint-labels\"", view);
        Assert.Contains("Device names are withheld until the microphone is enabled.", view);
        Assert.Contains("data-audio-role=\"device-hint-connect\"", view);
        Assert.Contains("No microphone is connected.", view);

        var audio = Script("ee-audio.js");
        Assert.Contains("var showLabelHint = permissionState !== \"granted\" || (inputs.length > 0 && !anyLabel);", audio);
        Assert.Contains("connect.hidden = !(permissionState === \"granted\" && inputs.length === 0);", audio);
    }

    // ------------------------------------------------- device identifiers stay local

    /// <summary>
    /// Device choice persists in the browser only. A device id in a request would break routing on
    /// another machine and leak a per-origin identifier; a test asserts none is sent.
    /// </summary>
    [Fact]
    public void No_device_identifier_reaches_the_network()
    {
        foreach (var script in new[] { "ee-audio.js", "audio-gate.js" })
        {
            var source = Script(script);
            // No request carries a device id, and no id is smuggled into a URL.
            Assert.DoesNotContain("deviceId=" + "\"", source);
            Assert.DoesNotContain("deviceId=" + "\\\"", source);
            Assert.DoesNotContain("&deviceId", source);
            Assert.DoesNotContain("?deviceId", source);
        }

        // The audio module may read and write localStorage, but must not fetch.
        Assert.DoesNotContain("fetch(", Script("ee-audio.js"));
    }

    [Fact]
    public void Device_preferences_are_stored_under_a_browser_only_key()
    {
        var audio = Script("ee-audio.js");
        Assert.Contains("var STORAGE_KEY = \"ee_audio_device_preferences\";", audio);
        Assert.Contains("window.localStorage.setItem(STORAGE_KEY", audio);
        Assert.DoesNotContain("document.cookie", audio);
    }

    // -------------------------------------------------------- the output test

    /// <summary>
    /// The output test plays a real, audible sample at the learner's chosen volume. A "test passed"
    /// badge proves nothing about a muted device, so the chime must be generated and played, not
    /// asserted.
    /// </summary>
    [Fact]
    public void The_output_test_plays_an_audible_sample_at_the_chosen_volume()
    {
        var audio = Script("ee-audio.js");
        var chime = Regex.Match(audio, @"async function playChime\([\s\S]*?\n  \}\r?\n\r?\n  async function runSpeakerTest");
        Assert.True(chime.Success, "playChime must exist");
        var body = chime.Value;

        Assert.Contains("createOscillator()", body);
        Assert.Contains("gain.gain.setValueAtTime(Math.max(0.001, volume * 0.2)", body);
        Assert.Contains("await audio.play();", body);
        // The chosen device is honoured, and a browser that cannot route says so instead of pretending.
        Assert.Contains("setSinkId(outputDeviceId)", body);
        Assert.Contains("cannot route", body);
    }

    [Fact]
    public void The_speaker_check_confirms_rather_than_claiming_a_pass()
    {
        var view = MicrophoneView();
        Assert.Contains("data-audio-action=\"speaker-test\"", view);
        // The control carries a word, not only an icon: a labelled control is the criterion.
        Assert.Contains(">Play sound<", view);
    }

    // ------------------------------------------------------- the settings control

    /// <summary>
    /// One labelled settings control carries type size and contrast alongside audio on every sitting
    /// surface, and the audio portion is suppressed on a section that uses none.
    /// </summary>
    /// <summary>
    /// One labelled settings control carries type size and contrast alongside audio on every sitting
    /// surface. The control's own button carries the word "Audio" - an icon alone would fail the
    /// criterion - and the audio portion is suppressed by the model on a paper that uses none.
    /// </summary>
    [Fact]
    public void Every_sitting_topbar_carries_one_labelled_settings_control()
    {
        foreach (var topbar in new[]
        {
            "Exams/_TakeTopbar.cshtml",
            "Practice/_TakeTopbar.cshtml",
            "Placement/_TakeTopbar.cshtml",
        })
        {
            var view = View(topbar);
            Assert.Contains("_AudioEmergencySwitcher", view);
        }

        var switcher = View("Shared/_AudioEmergencySwitcher.cshtml");
        Assert.Matches(@">\s*Audio\s*<", switcher);
        Assert.Contains("aria-haspopup=\"dialog\"", switcher);
    }

    [Fact]
    public void The_audio_portion_is_suppressed_on_a_section_that_uses_no_audio()
    {
        // Placement: no audio and no microphone in the paper => the model is null => no switcher.
        var model = new PlacementTakeViewModel(PlacementTake(ModalityDto.Reading));
        Assert.Null(model.AudioEquipment);

        // Exam: the audio panel is only built when a question is listening or speaking.
        var reading = new ExamTakeViewModel(TakeFixture(modality: ModalityDto.Reading)).Display;
        Assert.Null(reading.AudioEquipment);

        var listening = new ExamTakeViewModel(TakeFixture(modality: ModalityDto.Listening)).Display;
        Assert.NotNull(listening.AudioEquipment);
        Assert.True(listening.AudioEquipment!.ShowOutput);
        Assert.False(listening.AudioEquipment.ShowInput);
    }

    /// <summary>The topbar renders the settings control only when the model carries one.</summary>
    [Fact]
    public void The_placement_topbar_renders_the_settings_control_only_when_the_paper_needs_audio()
    {
        var view = View("Placement/_TakeTopbar.cshtml");
        Assert.Matches(
            @"@if \(Model\.AudioEquipment is not null\)\s*\{\s*@await Html\.PartialAsync\(""_AudioEmergencySwitcher"", Model\.AudioEquipment\)",
            view);
    }

    // ------------------------------------------------------------- accessibility

    /// <summary>
    /// The state must be announced, not only coloured. Each state block is a live region, and the
    /// state is also written to a data attribute so the current state is machine-readable.
    /// </summary>
    [Fact]
    public void The_permission_state_is_announced_and_not_only_coloured()
    {
        var view = MicrophoneView();

        Assert.Contains("role=\"alert\"", Block(view, "denied"));
        Assert.Contains("role=\"status\"", Block(view, "never-asked"));
        Assert.Contains("role=\"status\"", Block(view, "granted"));

        var gate = Script("audio-gate.js");
        Assert.Contains("component.setAttribute(\"data-permission-current\", visibleState);", gate);
    }

    /// <summary>
    /// Every helper a script needs must still be defined in it. `node --check` proves syntax and says
    /// nothing about this: a merge once dropped `fillSelect` and the file still parsed, while every
    /// audio check on the page threw `ReferenceError` at load. A real browser found it (the Playwright
    /// journey); this guard finds it without a browser, in the suite that runs every time.
    /// <para>
    /// A definition is either `function name(` or an assignment to a receiver (`sitting.name =
    /// function (`), which is how the exam shell extends its own namespace. Only definitions are
    /// asserted, because a scan for "calls with no definition" cannot tell a callback parameter from a
    /// helper and would false-positive on prose.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("ee-audio.js", "fillSelect", "deviceLabel", "supportSetSinkId", "syncControls", "measureSignal")]
    [InlineData("audio-gate.js", "readCached", "writeCached", "setBlock", "setBrowser", "acquire")]
    [InlineData("exams/preflight.js", "enumerateMics", "runSampleRecording", "showSilentGate", "refreshMicPermission")]
    [InlineData("exams/speaking.js", "playBeepCue", "cleanupSpeakingPacer", "startMockSpeakingCapture", "startSpeakingPacerRecording")]
    public void Every_function_a_script_calls_is_defined_in_it(string script, params string[] mustExist)
    {
        var source = Script(script);

        foreach (var helper in mustExist)
        {
            var pattern = $@"(?:function\s+{Regex.Escape(helper)}\s*\(|\.{Regex.Escape(helper)}\s*=\s*(?:async\s*)?function)";
            Assert.True(Regex.IsMatch(source, pattern),
                $"{script} no longer defines `{helper}`. Every call to it is a ReferenceError at load "
                + "in the browser, and `node --check` cannot see it.");
        }
    }

    // ------------------------------------------------------------------- helpers

    /// <summary>Extracts one `data-permission-state="…"` block, so a state cannot borrow another's copy.</summary>
    private static string Block(string view, string state)
    {
        var start = view.IndexOf($"data-permission-state=\"{state}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The view must render a data-permission-state=\"{state}\" block.");
        // The blocks are siblings; the next state marker or the end of the gate panel closes this one.
        var next = view.IndexOf("data-permission-state=\"", start + 1, StringComparison.Ordinal);
        var end = next >= 0 ? next : view.IndexOf("data-audio-role=\"device-hint-labels\"", start, StringComparison.Ordinal);
        if (end < 0) end = view.Length;
        return view[start..end];
    }

    private static string SilentGate(string view)
    {
        var start = view.IndexOf("data-audio-role=\"silent-gate\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "The view must render the silent-microphone gate.");
        return view[start..];
    }

    private static PlacementPreflightViewDto Gate(
        string modality, bool requiresMic, bool requiresAudio, List<PlacementSkillOfferDto>? offers = null) =>
        new(
            CertId: 1,
            CertCode: "TST",
            ExamId: 10,
            Offers: offers ?? [Offer(modality.ToUpperInvariant()[..4], modality, requiresMic, requiresAudio)],
            CooldownUntil: null,
            CooldownWaived: false,
            HasReusableSkills: false,
            IsStartable: true,
            RequiresMicrophone: requiresMic,
            RequiresAudio: requiresAudio);

    private static PlacementSkillOfferDto Offer(
        string code, string modality, bool mic, bool audio, bool canReuse = false) =>
        new(1, code, modality, modality, mic, audio, null, null, null, null, canReuse, false);

    private static PlacementTakeViewDto PlacementTake(ModalityDto modality) =>
        new(
            PlacementId: 1,
            AttemptId: 2,
            ExamId: 3,
            ExamName: "Placement",
            CertCode: "TST",
            Status: PlacementStatusDto.InProgress,
            StartedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(45),
            RemainingSeconds: 2700,
            TotalQuestions: 1,
            AnsweredCount: 0,
            FlaggedCount: 0,
            CurrentQuestion: Question(modality),
            Navigator: [new NavigatorItemDto(10, 1, 1, "P1", modality, false, false, true)],
            IsAutoSubmitted: false,
            Questions: [Question(modality)]);

    private static AttemptQuestionDto Question(ModalityDto modality) =>
        new(
            QuestionId: 10,
            DisplayOrder: 1,
            TotalQuestions: 1,
            SectionId: 1,
            SectionCode: "P1",
            SectionName: "Part 1",
            SkillCode: modality.ToString(),
            SkillName: modality.ToString(),
            Modality: modality,
            Stem: "Stem",
            ImagePath: null,
            ImageAlt: null,
            AudioPath: modality == ModalityDto.Listening ? "/audio/x.mp3" : null,
            AudioStartMs: null,
            AudioEndMs: null,
            Transcript: null,
            Passage: null,
            Options: [],
            SelectedOptionId: null,
            IsFlagged: false);

    private static AttemptTakeDto TakeFixture(ModalityDto modality) =>
        new(
            AttemptId: 1,
            ExamId: 2,
            ExamName: "Exam",
            CertCode: "TST",
            Status: AttemptStatusDto.InProgress,
            StartedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(60),
            RemainingSeconds: 3600,
            TotalQuestions: 1,
            AnsweredCount: 0,
            FlaggedCount: 0,
            CurrentQuestion: Question(modality),
            Navigator: [new NavigatorItemDto(10, 1, 1, "P1", modality, false, false, true)],
            IsAutoSubmitted: false,
            Questions: [Question(modality)]);

    private static PracticeSessionDto PracticeSession(ModalityDto modality) =>
        new(
            SessionId: 1,
            SectionId: 1,
            SectionCode: "P1",
            SectionName: "Part 1",
            Modality: modality,
            QuestionCount: 1,
            Status: PracticeStatusDto.InProgress,
            CorrectCount: 0,
            QuestionSource: QuestionSourceDto.All,
            StartedAt: DateTimeOffset.UtcNow,
            FinishedAt: null);

    private static PracticeQuestionDto PracticeQuestion(string? audioPath) =>
        new(
            SessionId: 1,
            SectionName: "Part 1",
            QuestionId: 10,
            DisplayOrder: 1,
            TotalQuestions: 1,
            Stem: "Stem",
            CategoryName: null,
            Hint: null,
            HintUsed: false,
            Options: [],
            SelectedOptionId: null,
            IsCorrect: null,
            CorrectOptionId: null,
            Explanation: null,
            Transcript: null,
            Passage: null,
            AudioPath: audioPath,
            ImagePath: null,
            ImageAlt: null);
}
