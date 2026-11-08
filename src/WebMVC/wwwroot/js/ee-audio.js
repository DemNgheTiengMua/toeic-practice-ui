(function () {
  "use strict";

  var STORAGE_KEY = "ee_audio_device_preferences";
  var defaults = {
    inputDeviceId: "default",
    outputDeviceId: "default",
    volume: 0.85,
    muted: false,
    lastTestedAt: null
  };
  var activeChecks = new WeakMap();

  function readPreferences() {
    try {
      var stored = JSON.parse(window.localStorage.getItem(STORAGE_KEY) || "{}");
      return {
        inputDeviceId: typeof stored.inputDeviceId === "string" ? stored.inputDeviceId : defaults.inputDeviceId,
        outputDeviceId: typeof stored.outputDeviceId === "string" ? stored.outputDeviceId : defaults.outputDeviceId,
        volume: Number.isFinite(stored.volume) ? Math.max(0, Math.min(1, stored.volume)) : defaults.volume,
        muted: typeof stored.muted === "boolean" ? stored.muted : defaults.muted,
        lastTestedAt: typeof stored.lastTestedAt === "string" ? stored.lastTestedAt : null
      };
    } catch (error) {
      console.warn("Audio preferences could not be read:", error);
      return Object.assign({}, defaults);
    }
  }

  function savePreferences(patch) {
    var preferences = Object.assign(readPreferences(), patch);
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
      syncControls();
      return preferences;
    } catch (error) {
      console.error("Audio preferences could not be saved:", error);
      showGlobalError("Audio preferences could not be saved in this browser.");
      return preferences;
    }
  }

  function showGlobalError(message) {
    document.querySelectorAll("[data-audio-equipment]").forEach(function (component) {
      var error = component.querySelector('[data-audio-role="error"]');
      if (error) {
        error.textContent = message;
        error.hidden = false;
      }
    });
  }

  /* ---- #221 / D18: the microphone permission gate ------------------------- */

  function gate() {
    return window.EeAudioGate || null;
  }

  /** True when this surface may raise a browser prompt. False inside a timed or one-way sitting. */
  function canPrompt(component) {
    return component.getAttribute("data-permission-promptable") !== "false";
  }

  /**
   * Switches the three permission blocks. The blocks are markup, not strings built here, because
   * the copy per state is the criterion: a state whose only difference is a class name would pass
   * a "state renders" assertion and tell the learner nothing.
   */
  function renderPermission(component, state) {
    var api = gate();
    if (!api) return;
    api.apply(component, state, canPrompt(component));
    var panel = component.querySelector("[data-permission-gate]");
    if (panel) {
      panel.setAttribute("data-permission-disabled", state === "granted" ? "false" : "true");
    }
  }

  async function refreshPermission(component) {
    var api = gate();
    if (!api) return null;
    var result = await api.read();
    renderPermission(component, result.state);
    return result;
  }

  /**
   * The device-list split D18 calls "naming and availability are different failures": granted with
   * an empty list means "plug one in"; labels withheld means "enable the microphone". Collapsing
   * them into "no devices found" is the state this replaces.
   */
  function renderDeviceHint(component, devices, permissionState) {
    var labels = component.querySelector('[data-audio-role="device-hint-labels"]');
    var connect = component.querySelector('[data-audio-role="device-hint-connect"]');
    if (!labels || !connect) return;
    var inputs = (devices || []).filter(function (device) { return device.kind === "audioinput"; });
    var anyLabel = inputs.some(function (device) { return !!device.label; });
    var showLabelHint = permissionState !== "granted" || (inputs.length > 0 && !anyLabel);
    labels.hidden = !showLabelHint;
    connect.hidden = !(permissionState === "granted" && inputs.length === 0);
  }

  function supportSetSinkId() {
    return typeof HTMLMediaElement !== "undefined" &&
      typeof HTMLMediaElement.prototype.setSinkId === "function";
  }

  function deviceLabel(device, type, index) {
    if (device.label) return device.label;
    var name = type === "audioinput" ? "Microphone" : "Speaker";
    return name + " " + (index + 1);
  }

  function fillSelect(select, devices, type, selectedId) {
    if (!select) return;
    var current = select.value;
    select.replaceChildren(new Option("System default", "default"));
    devices.filter(function (device) { return device.kind === type; })
      .forEach(function (device, index) {
        select.add(new Option(deviceLabel(device, type, index), device.deviceId));
      });
    var desired = selectedId || current || "default";
    select.value = Array.from(select.options).some(function (option) { return option.value === desired; })
      ? desired
      : "default";
  }

  async function enumerate(component) {
    var devicesApi = navigator.mediaDevices;
    var fallback = component.querySelector('[data-audio-role="fallback"]');
    var error = component.querySelector('[data-audio-role="error"]');
    var panel = component.querySelector("[data-permission-gate]");
    if (!devicesApi || typeof devicesApi.enumerateDevices !== "function") {
      if (fallback) {
        fallback.textContent = "Device selection is unavailable in this browser. Audio uses the system defaults.";
        fallback.hidden = false;
      }
      return;
    }

    var permission = panel ? await refreshPermission(component) : null;

    try {
      var devices = await devicesApi.enumerateDevices();
      var preferences = readPreferences();
      fillSelect(component.querySelector('[data-audio-role="input-device"]'), devices, "audioinput", preferences.inputDeviceId);
      fillSelect(component.querySelector('[data-audio-role="output-device"]'), devices, "audiooutput", preferences.outputDeviceId);
      if (error) error.hidden = true;
      if (panel) renderDeviceHint(component, devices, permission ? permission.state : "never-asked");
    } catch (enumerationError) {
      console.error("Audio devices could not be enumerated:", enumerationError);
      if (fallback) {
        fallback.textContent = "The browser could not list audio devices. You can still use the system defaults.";
        fallback.hidden = false;
      }
    }

    var outputSelect = component.querySelector('[data-audio-role="output-device"]');
    var support = component.querySelector('[data-audio-role="output-support"]');
    if (outputSelect && !supportSetSinkId()) {
      outputSelect.disabled = true;
      if (support) support.textContent = "This browser routes audio through the system default; device selection is not supported.";
    } else if (support) {
      support.textContent = "Output routing is supported by this browser.";
    }
  }

  function syncControls() {
    var preferences = readPreferences();
    document.querySelectorAll("[data-audio-equipment]").forEach(function (component) {
      var input = component.querySelector('[data-audio-role="input-device"]');
      var output = component.querySelector('[data-audio-role="output-device"]');
      var volume = component.querySelector('[data-audio-role="volume"]');
      var volumeValue = component.querySelector('[data-audio-role="volume-value"]');
      var mute = component.querySelector('[data-audio-action="toggle-mute"]');
      if (input && Array.from(input.options).some(function (option) { return option.value === preferences.inputDeviceId; })) {
        input.value = preferences.inputDeviceId;
      }
      if (output && Array.from(output.options).some(function (option) { return option.value === preferences.outputDeviceId; })) {
        output.value = preferences.outputDeviceId;
      }
      if (volume) volume.value = String(Math.round(preferences.volume * 100));
      if (volumeValue) volumeValue.textContent = Math.round(preferences.volume * 100) + "%";
      if (mute) {
        mute.textContent = preferences.muted ? "Unmute" : "Mute";
        mute.setAttribute("aria-label", preferences.muted ? "Unmute audio" : "Mute audio");
        mute.setAttribute("aria-pressed", String(preferences.muted));
      }
    });
  }

  async function applyOutput(audio) {
    var preferences = readPreferences();
    audio.volume = preferences.volume;
    audio.muted = preferences.muted;
    if (preferences.outputDeviceId !== "default" && typeof audio.setSinkId === "function") {
      try {
        await audio.setSinkId(preferences.outputDeviceId);
      } catch (error) {
        console.warn("Preferred audio output could not be applied:", error);
        showGlobalError("The selected speaker is unavailable. Audio will use the system default.");
        savePreferences({ outputDeviceId: "default" });
        try {
          await audio.setSinkId("default");
        } catch (fallbackError) {
          console.warn("System default audio routing could not be explicitly selected:", fallbackError);
        }
      }
    }
  }

  function applyOutputToAll() {
    document.querySelectorAll("audio").forEach(function (audio) {
      applyOutput(audio);
    });
  }

  function inputConstraints() {
    var inputDeviceId = readPreferences().inputDeviceId;
    return inputDeviceId && inputDeviceId !== "default"
      ? { deviceId: { exact: inputDeviceId } }
      : true;
  }

  function createAudioRecorder(stream) {
    if (typeof MediaRecorder.isTypeSupported !== "function") {
      return new MediaRecorder(stream);
    }

    var audio = document.createElement("audio");
    var mimeTypes = [
      "audio/webm;codecs=opus",
      "audio/webm",
      "audio/mp4",
      "audio/ogg;codecs=opus"
    ];
    var mimeType = mimeTypes.find(function (candidate) {
      return MediaRecorder.isTypeSupported(candidate) && audio.canPlayType(candidate) !== "";
    });
    return mimeType
      ? new MediaRecorder(stream, { mimeType: mimeType })
      : new MediaRecorder(stream);
  }

  function setStatus(component, role, text, style) {
    var element = component.querySelector('[data-audio-role="' + role + '"]');
    if (!element) return;
    element.textContent = text;
    if (role === "mic-status" || role === "speaker-status") {
      element.className = "badge font-sign " + (style || "badge--muted");
    }
  }

  function cleanupCheck(component) {
    var state = activeChecks.get(component);
    if (!state) return;
    state.cancelled = true;
    if (state.cancelCapture) state.cancelCapture();
    if (state.meterState && state.meterState.frame) cancelAnimationFrame(state.meterState.frame);
    if (state.recorder && state.recorder.state !== "inactive") state.recorder.stop();
    if (state.stream) state.stream.getTracks().forEach(function (track) { track.stop(); });
    if (state.context && state.context.state !== "closed") state.context.close();
    if (state.audio) {
      state.audio.pause();
      state.audio.removeAttribute("src");
    }
    if (state.objectUrl) URL.revokeObjectURL(state.objectUrl);
    activeChecks.delete(component);
  }

  function stopMicrophoneCheck(component) {
    cleanupCheck(component);
    var level = component.querySelector('[data-audio-role="meter-level"]');
    var meter = component.querySelector(".audio-equipment__meter");
    if (level) level.style.width = "0";
    if (meter) {
      meter.setAttribute("aria-valuenow", "0");
      meter.classList.remove("audio-equipment__meter--voice", "audio-equipment__meter--loud");
    }
    setStatus(component, "mic-status", "NO SIGNAL", "badge--muted");
  }

  async function startMeter(component, stream) {
    var context = new AudioContext();
    await context.resume();
    var analyser = context.createAnalyser();
    analyser.fftSize = 1024;
    context.createMediaStreamSource(stream).connect(analyser);
    var samples = new Float32Array(analyser.fftSize);
    var level = component.querySelector('[data-audio-role="meter-level"]');
    var meter = component.querySelector(".audio-equipment__meter");
    var dbReadout = component.querySelector('[data-audio-role="meter-db"]');
    var meterState = { frame: 0 };

    function draw() {
      analyser.getFloatTimeDomainData(samples);
      var sum = samples.reduce(function (total, sample) { return total + sample * sample; }, 0);
      var rms = Math.sqrt(sum / samples.length);
      var db = rms > 0 ? 20 * Math.log10(rms) : -Infinity;
      var percent = Math.max(0, Math.min(100, (db + 60) * 100 / 60));
      if (level) level.style.width = percent + "%";
      if (meter) meter.setAttribute("aria-valuenow", String(Math.round(percent)));
      if (dbReadout) dbReadout.textContent = Number.isFinite(db) ? Math.round(db) + " dBFS" : "-inf dBFS";
      if (db > -6) {
        if (meter) meter.classList.add("audio-equipment__meter--loud");
        if (meter) meter.classList.remove("audio-equipment__meter--voice");
        setStatus(component, "mic-status", "TOO LOUD", "badge--danger");
      } else if (db > -45) {
        if (meter) meter.classList.add("audio-equipment__meter--voice");
        if (meter) meter.classList.remove("audio-equipment__meter--loud");
        setStatus(component, "mic-status", "VOICE DETECTED", "badge--success");
      } else {
        if (meter) meter.classList.remove("audio-equipment__meter--voice", "audio-equipment__meter--loud");
        setStatus(component, "mic-status", "NO SIGNAL", "badge--muted");
      }
      meterState.frame = requestAnimationFrame(draw);
    }
    draw();
    return { context: context, state: meterState };
  }

  async function openMicrophone(component) {
    var api = gate();
    var constraints = inputConstraints();

    // On a sitting surface no control may prompt. `acquire` never calls getUserMedia unless the
    // permission is already granted, so a refusal here is reported rather than raised as a dialog
    // over a running clock.
    if (api && !canPrompt(component)) {
      var blocked = await api.acquire(constraints);
      if (!blocked.ok) {
        var refusal = new Error(
          blocked.reason === "permission"
            ? "The microphone is not allowed in this browser. Set it up on the instructions page before you start a sitting."
            : "Microphone access is unavailable.");
        refusal.name = blocked.reason === "permission" ? "NotAllowedError" : "NotSupportedError";
        throw refusal;
      }
      return blocked.stream;
    }

    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      throw new Error("Microphone access is unavailable. Use a supported browser on a secure connection.");
    }
    try {
      var stream = await navigator.mediaDevices.getUserMedia({ audio: constraints });
      if (api) renderPermission(component, api.GRANTED);
      return stream;
    } catch (error) {
      if (api) refreshPermission(component);
      if (constraints !== true && (error.name === "NotFoundError" || error.name === "OverconstrainedError")) {
        savePreferences({ inputDeviceId: "default" });
        await enumerate(component);
        return navigator.mediaDevices.getUserMedia({ audio: true });
      }
      throw error;
    }
  }

  /**
   * #221 / D18: "the microphone test measures signal, not permission", and reports one of three
   * outcomes - signal detected, no signal, or measurement unavailable. The third is evidence about
   * the browser, not about the learner's hardware, so it never blocks: an analyser that cannot be
   * built is not a broken headset.
   *
   * The peak is measured over real analyser samples, not over a duration. A muted microphone
   * records a file of the right length containing silence, so "did we capture 3 seconds" answers a
   * different question and would pass a muted headset.
   */
  function measureSignal(component, stream, milliseconds) {
    var AudioCtx = window.AudioContext || window.webkitAudioContext;
    if (!AudioCtx) return Promise.resolve({ outcome: "unavailable", peakDb: null });

    return new Promise(function (resolve) {
      var context;
      try {
        context = new AudioCtx();
        var analyser = context.createAnalyser();
        analyser.fftSize = 1024;
        context.createMediaStreamSource(stream).connect(analyser);
        var samples = new Float32Array(analyser.fftSize);
        var peak = -Infinity;
        var deadline = Date.now() + (milliseconds || 3000);
        var finished = false;

        function finish(outcome) {
          if (finished) return;
          finished = true;
          if (context.state !== "closed") context.close();
          resolve({
            outcome: outcome,
            peakDb: Number.isFinite(peak) ? Math.round(peak) : null
          });
        }

        function sample() {
          analyser.getFloatTimeDomainData(samples);
          for (var i = 0; i < samples.length; i++) {
            var level = Math.abs(samples[i]);
            if (level > 0) {
              var db = 20 * Math.log10(level);
              if (db > peak) peak = db;
            }
          }
          // -45 dBFS is the same voice threshold the live meter prints, so the two agree.
          if (peak > -45) return finish("signal");
          if (Date.now() >= deadline) return finish("silent");
          window.requestAnimationFrame(sample);
        }

        context.resume().then(sample, function () { finish("unavailable"); });
      } catch (error) {
        if (context && context.state !== "closed") context.close();
        resolve({ outcome: "unavailable", peakDb: null });
      }
    });
  }

  /** The stated-cost override: a silent microphone blocks until the learner accepts the sentence. */
  function showSilentGate(component, measurement) {
    var card = component.querySelector('[data-audio-role="silent-gate"]');
    if (!card) return;
    var accepted = component.querySelector('[data-audio-role="silent-override"]');
    if (measurement.outcome === "signal") {
      card.hidden = true;
      component.setAttribute("data-audio-blocked", "false");
      return;
    }
    if (measurement.outcome === "unavailable") {
      // The browser could not measure. Report it and do not block - that is a browser fact.
      card.hidden = true;
      component.setAttribute("data-audio-blocked", "false");
      return;
    }
    card.hidden = false;
    var blocked = !(accepted && accepted.checked);
    component.setAttribute("data-audio-blocked", blocked ? "true" : "false");
  }

  async function runMicCheck(component, echoTest) {
    cleanupCheck(component);
    var button = component.querySelector('[data-audio-action="' + (echoTest ? "echo-test" : "mic-check") + '"]');
    if (button) button.disabled = true;
    setStatus(component, "mic-status", "TESTING", "badge--warning");
    var help = component.querySelector('[data-audio-role="mic-help"]');
    var keepMicrophoneOpen = false;
    var state = { cancelled: false };
    activeChecks.set(component, state);
    try {
      var stream = await openMicrophone(component);
      if (state.cancelled) {
        stream.getTracks().forEach(function (track) { track.stop(); });
        return;
      }
      state.stream = stream;
      var meter = await startMeter(component, stream);
      state.context = meter.context;
      state.meterState = meter.state;
      activeChecks.set(component, state);
      await enumerate(component);
      if (state.cancelled) return;
      savePreferences({ lastTestedAt: new Date().toISOString() });
      if (!echoTest) {
        // #221: a silent microphone blocks by default, with a stated-cost override. Measuring the
        // analyser for three seconds is what distinguishes silence from hardware that works, and
        // "we recorded three seconds" cannot make that distinction.
        if (help) help.textContent = "Listening for three seconds — speak normally.";
        var measurement = await measureSignal(component, stream, 3000);
        if (state.cancelled) return;
        showSilentGate(component, measurement);
        if (measurement.outcome === "unavailable") {
          setStatus(component, "mic-status", "MEASUREMENT UNAVAILABLE", "badge--warning");
          if (help) help.textContent = "This browser cannot measure the input level. That is a browser limit, not a problem with your microphone — the sitting is not blocked by it.";
        } else if (measurement.outcome === "silent") {
          setStatus(component, "mic-status", "NO SIGNAL DETECTED", "badge--danger");
          if (help) help.textContent = "No signal was measured. Check that the microphone is not muted, is the selected device, and is plugged in.";
        } else {
          setStatus(component, "mic-status", "SIGNAL DETECTED", "badge--success");
          if (help) help.textContent = "Signal detected on this microphone. Select Stop microphone check to release it.";
        }
        if (button) {
          button.textContent = "Stop microphone check";
          button.disabled = false;
          button.dataset.running = "true";
        }
        keepMicrophoneOpen = true;
        return;
      }
      if (typeof MediaRecorder === "undefined") throw new Error("Audio recording is not supported in this browser.");
      var recorder = createAudioRecorder(stream);
      var chunks = [];
      state.recorder = recorder;
      recorder.addEventListener("dataavailable", function (event) {
        if (event.data && event.data.size > 0) chunks.push(event.data);
      });
      var stopped = new Promise(function (resolve) {
        recorder.addEventListener("stop", resolve, { once: true });
      });
      recorder.start();
      if (button) button.disabled = false;
      if (help) help.textContent = "Recording a 3-second sample on this device...";
      var captureTimeout;
      await new Promise(function (resolve) {
        captureTimeout = window.setTimeout(resolve, 3000);
        state.cancelCapture = function () {
          window.clearTimeout(captureTimeout);
          resolve();
        };
      });
      state.cancelCapture = null;
      if (recorder.state !== "inactive") recorder.stop();
      await stopped;
      if (state.cancelled) return;
      var blobType = recorder.mimeType || (chunks.find(function (chunk) { return chunk.type; }) || {}).type || "";
      var blob = new Blob(chunks, { type: blobType });
      cleanupCheck(component);
      if (!blob.size) throw new Error("The microphone test did not capture audio. Check the selected microphone and try again.");
      var objectUrl = URL.createObjectURL(blob);
      var player = new Audio(objectUrl);
      var echoState = { objectUrl: objectUrl, audio: player, cancelled: false };
      activeChecks.set(component, echoState);
      function finishEcho(status, style, message) {
        if (activeChecks.get(component) !== echoState) return;
        cleanupCheck(component);
        setStatus(component, "mic-status", status, style);
        if (help) help.textContent = message;
      }
      player.addEventListener("ended", function () {
        finishEcho("READY", "badge--muted", "Microphone access is off. Run another check whenever you need it.");
      }, { once: true });
      player.addEventListener("error", function () {
        finishEcho("PLAYBACK FAILED", "badge--danger", "The recorded audio could not be played. Check the output device and run the echo test again.");
      }, { once: true });
      await applyOutput(player);
      if (echoState.cancelled) return;
      var preferences = readPreferences();
      player.muted = false;
      player.volume = preferences.volume || 1;
      setStatus(component, "mic-status", "PLAYING ECHO", "badge--info");
      if (help) help.textContent = "Playing your recording. Microphone access has been stopped.";
      try {
        await player.play();
      } catch (error) {
        console.error("Echo playback blocked or failed:", error);
        finishEcho("PLAYBACK FAILED", "badge--danger", "Echo playback could not start. Check browser audio permissions and the selected output device.");
      }
    } catch (error) {
      cleanupCheck(component);
      console.error("Microphone check failed:", error);
      setStatus(component, "mic-status", "NO SIGNAL", "badge--danger");
      if (help) {
        help.textContent = error.name === "NotAllowedError" || error.name === "SecurityError"
          ? "Microphone access is blocked. Allow microphone access in your browser's site settings, then try again."
          : (error.message || "Microphone check failed. Check the selected device and try again.");
      }
    } finally {
      if (button && !keepMicrophoneOpen) {
        button.disabled = false;
        button.removeAttribute("data-running");
        button.textContent = echoTest ? "3-second echo test" : "Start microphone check";
      }
    }
  }

  async function playChime(volume, outputDeviceId) {
    var context = new AudioContext();
    var destination = context.createMediaStreamDestination();
    var audio = new Audio();
    var oscillator = context.createOscillator();
    var gain = context.createGain();
    audio.srcObject = destination.stream;
    audio.volume = 1;

    try {
      if (outputDeviceId !== "default") {
        if (typeof audio.setSinkId !== "function") {
          throw new Error("This browser cannot route the speaker check to the selected output device.");
        }
        await audio.setSinkId(outputDeviceId);
      }

      oscillator.type = "sine";
      oscillator.frequency.setValueAtTime(660, context.currentTime);
      oscillator.frequency.setValueAtTime(880, context.currentTime + 0.18);
      gain.gain.setValueAtTime(Math.max(0.001, volume * 0.2), context.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, context.currentTime + 0.55);
      oscillator.connect(gain).connect(destination);
      await context.resume();
      oscillator.start();
      oscillator.stop(context.currentTime + 0.55);
      await audio.play();
      await new Promise(function (resolve) {
        oscillator.addEventListener("ended", resolve, { once: true });
      });
    } finally {
      audio.pause();
      audio.srcObject = null;
      destination.stream.getTracks().forEach(function (track) { track.stop(); });
      await context.close();
    }
  }

  async function runSpeakerTest(component) {
    var preferences = readPreferences();
    var button = component.querySelector('[data-audio-action="speaker-test"]');
    setStatus(component, "speaker-status", "TESTING", "badge--warning");
    if (button) button.disabled = true;
    try {
      if (preferences.muted) {
        setStatus(component, "speaker-status", "MUTED", "badge--warning");
      } else {
        setStatus(component, "speaker-status", "CHIME PLAYING", "badge--info");
        await playChime(preferences.volume, preferences.outputDeviceId);
        setStatus(component, "speaker-status", "CHECK COMPLETE", "badge--success");
      }
      savePreferences({ lastTestedAt: new Date().toISOString() });
    } catch (error) {
      console.error("Speaker check failed:", error);
      setStatus(component, "speaker-status", "TEST FAILED", "badge--danger");
      showGlobalError("The speaker check could not play audio through the selected output device.");
    } finally {
      window.setTimeout(function () {
        if (button) button.disabled = false;
        if (component.isConnected) setStatus(component, "speaker-status", "READY", "badge--muted");
      }, 1500);
    }
  }

  /** The explicit control D18 requires: it says what it does ("see device names"), and nothing else does. */
  async function requestPermission(component, button) {
    var api = gate();
    if (!api) return;
    if (button) button.disabled = true;
    var result = await api.ask(inputConstraints());
    if (result.stream) result.stream.getTracks().forEach(function (track) { track.stop(); });
    renderPermission(component, result.state);
    await enumerate(component);
    if (button) button.disabled = false;
    if (result.state === "granted") {
      var help = component.querySelector('[data-audio-role="mic-help"]');
      if (help) help.textContent = "Microphone enabled. The device list is now readable; run a check to measure it.";
    }
  }

  /**
   * Re-reads the permission after the learner says they changed it. Browsers usually need a reload
   * before they re-read a permission they were told to forget; when the state is unchanged the
   * panel must not claim success, so the copy goes back to the denied block rather than promising
   * the change took.
   */
  async function retryPermission(component, button) {
    var api = gate();
    if (!api) return;
    if (button) button.disabled = true;
    var result = await api.read();
    renderPermission(component, result.state);
    await enumerate(component);
    if (button) button.disabled = false;
    if (result.state !== "granted") {
      var help = component.querySelector('[data-audio-role="mic-help"]');
      if (help) help.textContent = "Still blocked. The browser only re-reads the permission on a reload — change it, then reload this page.";
    }
  }

  function initComponent(component) {
    if (component.dataset.audioInitialized === "true") return;
    component.dataset.audioInitialized = "true";
    var input = component.querySelector('[data-audio-role="input-device"]');
    var output = component.querySelector('[data-audio-role="output-device"]');
    var volume = component.querySelector('[data-audio-role="volume"]');
    var preferences = readPreferences();
    if (input) fillSelect(input, [], "audioinput", preferences.inputDeviceId);
    if (output) fillSelect(output, [], "audiooutput", preferences.outputDeviceId);
    enumerate(component);
    component.addEventListener("change", function (event) {
      if (event.target.matches('[data-audio-role="input-device"]')) {
        savePreferences({ inputDeviceId: event.target.value });
      } else if (event.target.matches('[data-audio-role="output-device"]')) {
        savePreferences({ outputDeviceId: event.target.value });
        applyOutputToAll();
      } else if (event.target.matches('[data-audio-role="volume"]')) {
        savePreferences({ volume: Number(event.target.value) / 100, muted: Number(event.target.value) === 0 });
        applyOutputToAll();
      } else if (event.target.matches('[data-audio-role="silent-override"]')) {
        // The override states its cost in the markup above the box; ticking it is the acceptance.
        component.setAttribute("data-audio-blocked", event.target.checked ? "false" : "true");
      }
    });
    component.addEventListener("click", function (event) {
      var button = event.target.closest("[data-audio-action]");
      if (!button || !component.contains(button)) return;
      if (button.dataset.audioAction === "toggle-mute") {
        var current = readPreferences();
        savePreferences({ muted: !current.muted });
        applyOutputToAll();
      } else if (button.dataset.audioAction === "request-permission") {
        requestPermission(component, button);
      } else if (button.dataset.audioAction === "retry-permission") {
        retryPermission(component, button);
      } else if (button.dataset.audioAction === "mic-check") {
        if (button.dataset.running === "true") {
          stopMicrophoneCheck(component);
          button.textContent = "Start microphone check";
          button.removeAttribute("data-running");
        } else {
          runMicCheck(component, false);
        }
      } else if (button.dataset.audioAction === "echo-test") {
        runMicCheck(component, true);
      } else if (button.dataset.audioAction === "speaker-test") {
        runSpeakerTest(component);
      }
    });
  }

  async function init() {
    document.querySelectorAll("[data-audio-equipment]").forEach(initComponent);
    document.querySelectorAll("[data-audio-emergency-open]").forEach(function (openButton) {
      var dialog = document.getElementById(openButton.getAttribute("aria-controls"));
      if (!dialog || openButton.dataset.audioEmergencyInitialized === "true") return;
      openButton.dataset.audioEmergencyInitialized = "true";
      var panel = dialog.querySelector('[role="dialog"]');
      var closeButtons = dialog.querySelectorAll("[data-audio-emergency-close]");
      var lastFocus = null;
      function close() {
        dialog.querySelectorAll("[data-audio-equipment]").forEach(function (component) {
          stopMicrophoneCheck(component);
          var micButton = component.querySelector('[data-audio-action="mic-check"]');
          if (micButton) {
            micButton.disabled = false;
            micButton.removeAttribute("data-running");
            micButton.textContent = "Start microphone check";
          }
        });
        dialog.classList.remove("modal-overlay--open");
        dialog.setAttribute("aria-hidden", "true");
        if (lastFocus) lastFocus.focus();
      }
      openButton.addEventListener("click", function () {
        lastFocus = document.activeElement;
        dialog.classList.add("modal-overlay--open");
        dialog.setAttribute("aria-hidden", "false");
        var focusable = dialog.querySelector("button:not(:disabled), select:not(:disabled), input:not(:disabled)");
        if (focusable) focusable.focus();
      });
      closeButtons.forEach(function (button) { button.addEventListener("click", close); });
      dialog.addEventListener("click", function (event) {
        if (event.target === dialog) close();
      });
      dialog.addEventListener("keydown", function (event) {
        if (event.key === "Escape") close();
        if (event.key === "Tab" && panel) {
          var focusable = Array.from(panel.querySelectorAll("button:not(:disabled), select:not(:disabled), input:not(:disabled)"));
          if (focusable.length && event.shiftKey && document.activeElement === focusable[0]) {
            event.preventDefault();
            focusable[focusable.length - 1].focus();
          } else if (focusable.length && !event.shiftKey && document.activeElement === focusable[focusable.length - 1]) {
            event.preventDefault();
            focusable[0].focus();
          }
        }
      });
    });
    syncControls();
    applyOutputToAll();
  }

  if (navigator.mediaDevices && navigator.mediaDevices.addEventListener) {
    navigator.mediaDevices.addEventListener("devicechange", function () {
      document.querySelectorAll("[data-audio-equipment]").forEach(enumerate);
    });
  }

  window.EeAudio = {
    init: init,
    readPreferences: readPreferences,
    savePreferences: savePreferences,
    inputConstraints: inputConstraints,
    applyOutput: applyOutput,
    measureSignal: measureSignal,
    refreshDevices: function () {
      return Promise.all(Array.from(document.querySelectorAll("[data-audio-equipment]"), enumerate));
    }
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
