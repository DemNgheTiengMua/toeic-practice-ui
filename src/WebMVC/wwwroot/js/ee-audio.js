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
    if (!devicesApi || typeof devicesApi.enumerateDevices !== "function") {
      if (fallback) {
        fallback.textContent = "Device selection is unavailable in this browser. Audio uses the system defaults.";
        fallback.hidden = false;
      }
      return;
    }

    try {
      var devices = await devicesApi.enumerateDevices();
      var preferences = readPreferences();
      fillSelect(component.querySelector('[data-audio-role="input-device"]'), devices, "audioinput", preferences.inputDeviceId);
      fillSelect(component.querySelector('[data-audio-role="output-device"]'), devices, "audiooutput", preferences.outputDeviceId);
      if (error) error.hidden = true;
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
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      throw new Error("Microphone access is unavailable. Use a supported browser on a secure connection.");
    }
    var constraints = inputConstraints();
    try {
      return await navigator.mediaDevices.getUserMedia({ audio: constraints });
    } catch (error) {
      if (constraints !== true && (error.name === "NotFoundError" || error.name === "OverconstrainedError")) {
        savePreferences({ inputDeviceId: "default" });
        await enumerate(component);
        return navigator.mediaDevices.getUserMedia({ audio: true });
      }
      throw error;
    }
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
        if (button) {
          button.textContent = "Stop microphone check";
          button.disabled = false;
          button.dataset.running = "true";
        }
        keepMicrophoneOpen = true;
        if (help) help.textContent = "Speak normally and watch the level. Select Stop microphone check to release microphone access.";
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
      }
    });
    component.addEventListener("click", function (event) {
      var button = event.target.closest("[data-audio-action]");
      if (!button || !component.contains(button)) return;
      if (button.dataset.audioAction === "toggle-mute") {
        var current = readPreferences();
        savePreferences({ muted: !current.muted });
        applyOutputToAll();
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
    refreshDevices: function () {
      return Promise.all(Array.from(document.querySelectorAll("[data-audio-equipment]"), enumerate));
    }
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
