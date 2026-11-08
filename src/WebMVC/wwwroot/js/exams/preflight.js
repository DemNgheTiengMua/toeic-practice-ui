(function() {
    "use strict";

    var config = JSON.parse(document.getElementById("exam-preflight-data").textContent);
    var audioUrls = config.audioUrls;
    var requiresMic = config.requiresMic;
    var isAlreadyVerified = config.isAlreadyVerified;

    var state = {
      audioBuffered: audioUrls.length === 0,
      micVerified: !requiresMic || isAlreadyVerified,
      bufferedCount: 0,
      totalAudio: audioUrls.length
    };

    var startBtn = document.getElementById("start-test-btn");
    var gateStatusBadge = document.getElementById("gate-status-badge");
    var gatePendingNotice = document.getElementById("gate-pending-notice");
    var gateReadyNotice = document.getElementById("gate-ready-notice");

    function checkStartReadiness() {
      if (state.audioBuffered && state.micVerified) {
        startBtn.disabled = false;
        if (gateStatusBadge) {
          gateStatusBadge.textContent = "VERIFIED & READY";
          gateStatusBadge.className = "badge badge--success font-sign";
        }
        if (gatePendingNotice) gatePendingNotice.style.display = "none";
        if (gateReadyNotice) gateReadyNotice.style.display = "block";
      } else {
        startBtn.disabled = true;
        if (gateStatusBadge) {
          gateStatusBadge.textContent = "CHECKS PENDING";
          gateStatusBadge.className = "badge badge--warning font-sign";
        }
        if (gatePendingNotice) gatePendingNotice.style.display = "block";
        if (gateReadyNotice) gateReadyNotice.style.display = "none";
      }
    }

    // --- Step 1: Audio Asset Pre-buffering ---
    var bufferBar = document.getElementById("buffer-progress-bar");
    var bufferPct = document.getElementById("buffer-percentage");
    var bufferDetails = document.getElementById("buffer-details");
    var bufferBadge = document.getElementById("audio-buffer-badge");
    var bufferErrorAlert = document.getElementById("buffer-error-alert");
    var bufferErrorMsg = document.getElementById("buffer-error-msg");
    var btnRetryBuffer = document.getElementById("btn-retry-buffer");

    function updateBufferUI(loaded, total, statusText, isError) {
      var pct = total === 0 ? 100 : Math.round((loaded / total) * 100);
      if (bufferBar) {
        bufferBar.style.width = pct + "%";
        bufferBar.setAttribute("aria-valuenow", String(pct));
        bufferBar.className = isError ? "progress__bar progress__bar--danger" : (pct === 100 ? "progress__bar progress__bar--success" : "progress__bar");
      }
      if (bufferPct) bufferPct.textContent = pct + "%";
      if (bufferDetails) {
        bufferDetails.textContent = total > 0 ? (loaded + " of " + total + " audio tracks pre-buffered") : "No audio tracks required";
      }
      if (bufferBadge) {
        bufferBadge.textContent = statusText;
        bufferBadge.className = "badge font-sign " + (isError ? "badge--danger" : (pct === 100 ? "badge--success" : "badge--warning"));
      }
    }

    function preloadViaAudio(url) {
      return new Promise(function(resolve, reject) {
        var audio = new Audio();
        audio.preload = "auto";
        audio.oncanplaythrough = function() { resolve(); };
        audio.onerror = function() { reject(new Error("Playback pre-buffer failed for " + url)); };
        audio.src = url;
        audio.load();
      });
    }

    async function bufferSingleAudio(url) {
      try {
        var resp = await fetch(url, { method: "GET", mode: "cors" });
        if (!resp.ok && resp.status !== 0) {
          await preloadViaAudio(url);
        } else {
          await resp.blob();
        }
      } catch (err) {
        await preloadViaAudio(url);
      }
    }

    async function runAudioBuffering() {
      if (bufferErrorAlert) bufferErrorAlert.hidden = true;
      if (state.totalAudio === 0) {
        state.audioBuffered = true;
        updateBufferUI(0, 0, "READY (NO AUDIO)", false);
        checkStartReadiness();
        return;
      }

      state.bufferedCount = 0;
      updateBufferUI(0, state.totalAudio, "BUFFERING (0/" + state.totalAudio + ")", false);

      for (var i = 0; i < audioUrls.length; i++) {
        var url = audioUrls[i];
        try {
          await bufferSingleAudio(url);
          state.bufferedCount++;
          var isLast = state.bufferedCount === state.totalAudio;
          updateBufferUI(
            state.bufferedCount,
            state.totalAudio,
            isLast ? "100% BUFFERED" : "BUFFERING (" + state.bufferedCount + "/" + state.totalAudio + ")",
            false
          );
        } catch (fetchErr) {
          console.error("Audio pre-buffering error for", url, fetchErr);
          state.audioBuffered = false;
          updateBufferUI(state.bufferedCount, state.totalAudio, "BUFFERING FAILED", true);
          if (bufferErrorAlert) {
            if (bufferErrorMsg) bufferErrorMsg.textContent = "Failed to load audio asset: " + url + ". Please check your connection and retry.";
            bufferErrorAlert.hidden = false;
          }
          checkStartReadiness();
          return;
        }
      }

      state.audioBuffered = true;
      updateBufferUI(state.totalAudio, state.totalAudio, "100% BUFFERED", false);
      checkStartReadiness();
    }

    if (btnRetryBuffer) {
      btnRetryBuffer.addEventListener("click", function() {
        runAudioBuffering();
      });
    }

    // --- Step 3: Interactive Microphone & Voice Sample Check ---
    var micCard = document.getElementById("mic-check-card");
    if (requiresMic && micCard) {
      var micSelect = document.getElementById("mic-select");
      var micMeterFill = document.getElementById("mic-meter-fill");
      var meterDbText = document.getElementById("meter-db-text");
      var micSignalLabel = document.getElementById("mic-signal-label");
      var micStatusBadge = document.getElementById("mic-status-badge");
      var btnStartSample = document.getElementById("btn-start-sample");
      var sampleCountdown = document.getElementById("sample-countdown");
      var sampleStatusText = document.getElementById("sample-status-text");
      var sampleReviewPanel = document.getElementById("sample-review-panel");
      var btnReplaySample = document.getElementById("btn-replay-sample");
      var btnMicYes = document.getElementById("btn-mic-confirm-yes");
      var btnMicNo = document.getElementById("btn-mic-confirm-no");
      var micConfirmedSuccess = document.getElementById("mic-confirmed-success");

      var micStream = null;
      var audioContext = null;
      var animFrameId = null;
      var recordedBlob = null;
      var recordedUrl = null;

      async function enumerateMics() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;
        try {
          var devices = await navigator.mediaDevices.enumerateDevices();
          var audioInputs = devices.filter(function(d) { return d.kind === "audioinput"; });
          if (micSelect && audioInputs.length > 0) {
            micSelect.innerHTML = "";
            audioInputs.forEach(function(d, index) {
              var opt = document.createElement("option");
              opt.value = d.deviceId;
              opt.textContent = d.label || ("Microphone " + (index + 1));
              micSelect.appendChild(opt);
            });
          }

          // #221 / D18: two different failures. The permission decides whether labels exist at all;
          // an granted permission with no input is a device to plug in. Saying "no devices found" for
          // both is the state this replaces.
          var permission = window.EeAudioGate ? await window.EeAudioGate.read() : null;
          var anyLabel = audioInputs.some(function(d) { return !!d.label; });
          var labelHint = document.querySelector('[data-audio-role="device-hint-labels"]');
          var connectHint = document.querySelector('[data-audio-role="device-hint-connect"]');
          if (labelHint) labelHint.hidden = !(!permission || permission.state !== "granted" || (audioInputs.length > 0 && !anyLabel));
          if (connectHint) connectHint.hidden = !(permission && permission.state === "granted" && audioInputs.length === 0);
          return permission;
        } catch (e) {
          console.warn("Could not enumerate audio inputs", e);
          return null;
        }
      }

      /**
       * #221 / D18: the permission states come from the browser, not from a failed getUserMedia.
       * This screen may prompt - it is the pre-sitting screen - and the shared panel renders
       * whichever of the three states the browser reports.
       */
      async function refreshMicPermission() {
        if (!window.EeAudioGate) return null;
        var panel = micCard.querySelector("[data-permission-gate]");
        var result = await window.EeAudioGate.read();
        if (panel) window.EeAudioGate.apply(panel, result.state, true);
        return result;
      }

      /**
       * #221: a silent microphone blocks by default, with a stated-cost override. The measurement is
       * the analyser's peak over three seconds - not the length of the recording, because a muted
       * microphone records three seconds of silence perfectly.
       */
      async function measureMicSignal(stream) {
        if (!window.EeAudio) return { outcome: "unavailable", peakDb: null };
        return window.EeAudio.measureSignal(micCard, stream, 3000);
      }

      function showSilentGate(measurement) {
        var gate = micCard.querySelector('[data-audio-role="silent-gate"]');
        if (!gate) return;
        var override = micCard.querySelector('[data-audio-role="silent-override"]');
        var blocked = measurement.outcome === "silent" && !(override && override.checked);
        gate.hidden = measurement.outcome !== "silent";
        micCard.setAttribute("data-audio-blocked", blocked ? "true" : "false");
      }

      function stopActiveStream() {
        if (animFrameId) cancelAnimationFrame(animFrameId);
        if (micStream) {
          micStream.getTracks().forEach(function(t) { t.stop(); });
          micStream = null;
        }
        if (audioContext && audioContext.state !== "closed") {
          audioContext.close();
          audioContext = null;
        }
        if (micMeterFill) micMeterFill.style.width = "0%";
        if (meterDbText) meterDbText.textContent = "-inf dBFS";
        if (micSignalLabel) {
          micSignalLabel.textContent = "NO SIGNAL";
          micSignalLabel.className = "badge badge--muted font-sign";
        }
      }

      async function startLiveMeter(stream) {
        audioContext = new (window.AudioContext || window.webkitAudioContext)();
        await audioContext.resume();
        var analyser = audioContext.createAnalyser();
        analyser.fftSize = 1024;
        var source = audioContext.createMediaStreamSource(stream);
        source.connect(analyser);

        var samples = new Float32Array(analyser.fftSize);

        function drawMeter() {
          analyser.getFloatTimeDomainData(samples);
          var sum = 0;
          for (var i = 0; i < samples.length; i++) {
            sum += samples[i] * samples[i];
          }
          var rms = Math.sqrt(sum / samples.length);
          var db = rms > 0 ? 20 * Math.log10(rms) : -Infinity;
          var pct = Math.max(0, Math.min(100, (db + 60) * 100 / 60));

          if (micMeterFill) micMeterFill.style.width = pct + "%";
          if (meterDbText) meterDbText.textContent = Number.isFinite(db) ? (Math.round(db) + " dBFS") : "-inf dBFS";

          if (db > -6) {
            if (micSignalLabel) {
              micSignalLabel.textContent = "TOO LOUD";
              micSignalLabel.className = "badge badge--danger font-sign";
            }
          } else if (db > -45) {
            if (micSignalLabel) {
              micSignalLabel.textContent = "VOICE DETECTED";
              micSignalLabel.className = "badge badge--success font-sign";
            }
          } else {
            if (micSignalLabel) {
              micSignalLabel.textContent = "NO SIGNAL";
              micSignalLabel.className = "badge badge--muted font-sign";
            }
          }

          animFrameId = requestAnimationFrame(drawMeter);
        }

        drawMeter();
      }

      async function runSampleRecording() {
        stopActiveStream();
        if (sampleReviewPanel) sampleReviewPanel.style.display = "none";
        if (micConfirmedSuccess) micConfirmedSuccess.style.display = "none";

        var deviceId = micSelect ? micSelect.value : "default";
        var constraints = deviceId && deviceId !== "default" ? { audio: { deviceId: { exact: deviceId } } } : { audio: true };

        try {
          // #221 / D18: this is the pre-sitting screen, so it MAY prompt - but through the gate, so
          // the three states are read from the browser rather than guessed from an error name.
          var opened = window.EeAudioGate ? await window.EeAudioGate.ask(constraints) : null;
          if (!opened || !opened.stream) {
            await refreshMicPermission();
            await enumerateMics();
            if (sampleStatusText) sampleStatusText.textContent = !opened
              ? "Microphone support is unavailable in this browser. Use a current Chrome, Edge, Firefox or Safari."
              : (opened.state === "denied"
                ? "The browser is blocking the microphone. Fix it with the control path above, then try again."
                : "The microphone was not granted. Enable it above, then try again.");
            if (btnStartSample) btnStartSample.disabled = false;
            return;
          }

          micStream = opened.stream;
          await refreshMicPermission();
          await enumerateMics();
          await startLiveMeter(micStream);

          // Measure signal before recording, so a muted microphone is reported as silence rather
          // than as a recording that played back fine (it did - it was silence).
          var measurement = await measureMicSignal(micStream);
          showSilentGate(measurement);

          var mediaRecorder = new MediaRecorder(micStream);
          var chunks = [];

          mediaRecorder.ondataavailable = function(e) {
            if (e.data && e.data.size > 0) chunks.push(e.data);
          };

          mediaRecorder.onstop = function() {
            var blob = new Blob(chunks, { type: mediaRecorder.mimeType || "audio/webm" });
            recordedBlob = blob;
            if (recordedUrl) URL.revokeObjectURL(recordedUrl);
            recordedUrl = URL.createObjectURL(blob);

            stopActiveStream();

            if (sampleCountdown) sampleCountdown.style.display = "none";
            if (btnStartSample) {
              btnStartSample.disabled = false;
              btnStartSample.textContent = "Record Again";
            }
            if (sampleStatusText) {
              sampleStatusText.textContent = measurement.outcome === "silent"
                ? "Recorded — but no signal was measured. A silent microphone blocks the sitting unless you accept the consequence below."
                : (measurement.outcome === "unavailable"
                  ? "Recorded. This browser cannot measure the level — that is a browser limit, not your microphone."
                  : "Recording finished. Playing back your sample...");
            }
            if (sampleReviewPanel) sampleReviewPanel.style.display = "block";

            // Automatically play back sample
            var playback = new Audio(recordedUrl);
            playback.onended = function() {
              if (sampleStatusText) sampleStatusText.textContent = "Playback complete. Please answer the clarity question below.";
            };
            playback.play().catch(function(e) {
              console.warn("Auto playback error", e);
              if (sampleStatusText) sampleStatusText.textContent = "Recorded. Click Replay Audio to listen.";
            });
          };

          mediaRecorder.start();
          if (btnStartSample) btnStartSample.disabled = true;
          if (sampleCountdown) {
            sampleCountdown.style.display = "inline";
            sampleCountdown.textContent = "3s";
          }
          if (sampleStatusText) sampleStatusText.textContent = "Recording sample... Speak now.";

          var remainingSecs = 3;
          var interval = setInterval(function() {
            remainingSecs--;
            if (sampleCountdown) sampleCountdown.textContent = remainingSecs + "s";
            if (remainingSecs <= 0) {
              clearInterval(interval);
              if (mediaRecorder.state !== "inactive") mediaRecorder.stop();
            }
          }, 1000);

        } catch (err) {
          console.error("Microphone check error", err);
          stopActiveStream();
          if (btnStartSample) btnStartSample.disabled = false;
          if (sampleStatusText) sampleStatusText.textContent = "Could not access microphone: " + (err.message || "Permission denied");
          if (micStatusBadge) {
            micStatusBadge.textContent = "MIC ACCESS BLOCKED";
            micStatusBadge.className = "badge badge--danger font-sign";
          }
        }
      }

      if (btnStartSample) {
        btnStartSample.addEventListener("click", function() {
          runSampleRecording();
        });
      }

      if (btnReplaySample) {
        btnReplaySample.addEventListener("click", function() {
          if (recordedUrl) {
            var audio = new Audio(recordedUrl);
            audio.play();
          }
        });
      }

      if (btnMicYes) {
        btnMicYes.addEventListener("click", function() {
          // #221 / D18: a silent microphone blocks by default. The override in the panel is the only
          // way past it, and it states the consequence before the box is ticked.
          var override = micCard.querySelector('[data-audio-role="silent-override"]');
          if (micCard.getAttribute("data-audio-blocked") === "true") {
            var gate = micCard.querySelector('[data-audio-role="silent-gate"]');
            if (gate) gate.hidden = false;
            if (sampleStatusText) {
              sampleStatusText.textContent = "No signal was measured. Read the consequence in the override panel and tick it to continue, or fix the microphone and record again.";
            }
            if (gate) gate.scrollIntoView({ block: "nearest" });
            return;
          }

          state.micVerified = true;
          if (micStatusBadge) {
            micStatusBadge.textContent = "MIC VERIFIED";
            micStatusBadge.className = "badge badge--success font-sign";
          }
          if (sampleReviewPanel) sampleReviewPanel.style.display = "none";
          if (micConfirmedSuccess) micConfirmedSuccess.style.display = "block";
          if (sampleStatusText) {
            sampleStatusText.textContent = override && override.checked
              ? "Confirmed with a silent microphone. The speaking skill may be reported as not measured."
              : "Microphone check confirmed clear.";
          }
          checkStartReadiness();
        });
      }

      // The stated-cost override releases the gate; unticking it closes the gate again.
      var silentOverride = micCard.querySelector('[data-audio-role="silent-override"]');
      if (silentOverride) {
        silentOverride.addEventListener("change", function() {
          micCard.setAttribute("data-audio-blocked", silentOverride.checked ? "false" : "true");
        });
      }

      // #221: the two permission controls in the shared panel. This screen may prompt, so both are
      // live here - unlike a sitting, where the partial renders neither.
      micCard.addEventListener("click", async function(event) {
        var button = event.target.closest("[data-audio-action]");
        if (!button || !micCard.contains(button)) return;
        if (button.dataset.audioAction === "request-permission" || button.dataset.audioAction === "retry-permission") {
          button.disabled = true;
          var result = button.dataset.audioAction === "request-permission"
            ? await window.EeAudioGate.ask(micSelect && micSelect.value !== "default" ? { audio: { deviceId: { exact: micSelect.value } } } : true)
            : await window.EeAudioGate.read();
          if (result.stream) result.stream.getTracks().forEach(function(t) { t.stop(); });
          await refreshMicPermission();
          await enumerateMics();
          button.disabled = false;
          if (result.state === "granted" && micSelect && micSelect.options.length <= 1) {
            if (sampleStatusText) sampleStatusText.textContent = "Microphone enabled. No device is listed yet — plug one in, then record a sample.";
          }
        }
      });

      // Initial microphone enumeration
      refreshMicPermission().then(enumerateMics);

      if (btnMicNo) {
        btnMicNo.addEventListener("click", function() {
          state.micVerified = false;
          if (micStatusBadge) {
            micStatusBadge.textContent = "CHECK REQUIRED";
            micStatusBadge.className = "badge badge--warning font-sign";
          }
          if (sampleReviewPanel) sampleReviewPanel.style.display = "none";
          if (micConfirmedSuccess) micConfirmedSuccess.style.display = "none";
          if (sampleStatusText) sampleStatusText.textContent = "Adjust your device or position, then click Record again.";
          checkStartReadiness();
        });
      }

      // Initial microphone enumeration
      enumerateMics();
    }

    // Start audio asset pre-buffering immediately on page load
    runAudioBuffering();
    checkStartReadiness();
  })();
