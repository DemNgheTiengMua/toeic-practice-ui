/**
 * Exam Audio Player ("Bảng hiệu phòng thi" Design Language)
 *
 * Invariants:
 * 1. Anti-seeking: Scrubbing, rewinding, or fast-forwarding is strictly prevented.
 * 2. Playback limit: In formal exams and placement tests (data-allow-replay="false"),
 *    the audio can be played strictly ONCE. When finished, playback is permanently locked.
 * 3. In practice / review mode (data-allow-replay="true"), replay is allowed from start,
 *    but seeking during playback remains disabled.
 * 4. Segment: data-start-ms / data-end-ms limit playback to one slice of a longer track (a
 *    group's slice of its section's master audio). The slice behaves as the whole recording:
 *    it starts at data-start-ms, ends at data-end-ms, and timers and progress count within it.
 */
(function () {
  "use strict";

  function formatTime(seconds) {
    if (isNaN(seconds) || !isFinite(seconds) || seconds < 0) return "--:--";
    var s = Math.floor(seconds);
    var mins = Math.floor(s / 60);
    var secs = s % 60;
    return (mins < 10 ? "0" + mins : mins) + ":" + (secs < 10 ? "0" + secs : secs);
  }

  function initPlayer(playerEl) {
    if (playerEl.dataset.initialized) return;
    playerEl.dataset.initialized = "true";

    var allowReplay = playerEl.getAttribute("data-allow-replay") === "true";
    var autoPlay = playerEl.getAttribute("data-autoplay") === "true";
    var audio = playerEl.querySelector(".exam-audio__native");
    var btnPlay = playerEl.querySelector(".exam-audio__btn-play");
    var btnText = playerEl.querySelector(".exam-audio__btn-text");
    var iconPlay = playerEl.querySelector(".exam-audio__icon-play");
    var iconPause = playerEl.querySelector(".exam-audio__icon-pause");
    var badge = playerEl.querySelector(".exam-audio__badge");
    var progressBar = playerEl.querySelector(".exam-audio__bar");
    var statusText = playerEl.querySelector(".exam-audio__status");
    var timerText = playerEl.querySelector(".exam-audio__timer");
    var btnMute = playerEl.querySelector(".exam-audio__btn-mute");
    var iconVol = playerEl.querySelector(".exam-audio__icon-vol");
    var iconMute = playerEl.querySelector(".exam-audio__icon-mute");
    var volumeSlider = playerEl.querySelector(".exam-audio__slider");

    if (!audio || !btnPlay) return;

    var startMs = parseInt(playerEl.getAttribute("data-start-ms"), 10);
    var endMs = parseInt(playerEl.getAttribute("data-end-ms"), 10);
    var hasSegment = !isNaN(startMs) && !isNaN(endMs) && startMs >= 0 && endMs > startMs;
    var segStart = hasSegment ? startMs / 1000 : 0;
    var segEnd = hasSegment ? endMs / 1000 : null;

    var maxTimeSeen = segStart;
    var hasEnded = false;

    function segmentLength() {
      return hasSegment ? segEnd - segStart : audio.duration;
    }

    // Moves the playhead to the start of the slice without tripping the anti-seeking guard.
    function rewindToSegmentStart() {
      maxTimeSeen = segStart;
      audio.currentTime = segStart;
    }

    function updateTimeDisplay() {
      var length = segmentLength();
      var elapsed = Math.max(0, audio.currentTime - segStart);
      if (hasSegment) elapsed = Math.min(elapsed, length);
      if (timerText) timerText.textContent = formatTime(elapsed) + " / " + formatTime(length);

      if (progressBar && length > 0) {
        var pct = (elapsed / length) * 100;
        progressBar.style.width = Math.min(100, Math.max(0, pct)) + "%";
      }
    }

    // Update duration once metadata is loaded; a slice starts playback at its own offset.
    audio.addEventListener("loadedmetadata", function () {
      if (hasSegment && audio.currentTime < segStart) rewindToSegmentStart();
      updateTimeDisplay();
    });

    function stopAtSegmentEnd() {
      if (hasSegment && !hasEnded && audio.currentTime >= segEnd) {
        finishPlayback();
        audio.pause();
        return true;
      }
      return false;
    }

    // timeupdate fires only ~4 times a second, which would let a slice run on into the next
    // group's audio; while playing, check every frame as well.
    function watchSegmentEnd() {
      if (audio.paused || hasEnded || stopAtSegmentEnd()) return;
      window.requestAnimationFrame(watchSegmentEnd);
    }

    // Enforce anti-seeking invariant on timeupdate & seeking events
    audio.addEventListener("timeupdate", function () {
      if (audio.currentTime > maxTimeSeen) {
        maxTimeSeen = audio.currentTime;
      }
      if (stopAtSegmentEnd()) return;
      updateTimeDisplay();
    });

    audio.addEventListener("seeking", function () {
      // If user/browser attempts to jump forward or backward past continuous playback
      if (Math.abs(audio.currentTime - maxTimeSeen) > 0.75) {
        audio.currentTime = maxTimeSeen;
      }
    });

    // Playback state changes
    audio.addEventListener("play", function () {
      // Play pressed before metadata arrived: the slice still starts at its own offset.
      if (hasSegment && audio.currentTime < segStart - 0.25) rewindToSegmentStart();
      if (hasSegment) window.requestAnimationFrame(watchSegmentEnd);

      // Pause all other audio players on page
      document.querySelectorAll(".exam-audio__native").forEach(function (other) {
        if (other !== audio && !other.paused) {
          other.pause();
        }
      });

      if (btnText) btnText.textContent = "PAUSE";
      if (iconPlay) iconPlay.classList.add("hidden");
      if (iconPause) iconPause.classList.remove("hidden");
      if (badge) {
        badge.className = "badge badge--info exam-audio__badge font-sign";
        badge.textContent = "PLAYING";
      }
      if (statusText && !hasEnded) {
        statusText.textContent = allowReplay
          ? "Playing audio · Seeking disabled"
          : "Playing audio · Plays once only · No rewinding";
      }
    });

    audio.addEventListener("pause", function () {
      if (hasEnded) return;
      if (btnText) btnText.textContent = "RESUME";
      if (iconPlay) iconPlay.classList.remove("hidden");
      if (iconPause) iconPause.classList.add("hidden");
      if (badge) {
        badge.className = "badge badge--warning exam-audio__badge font-sign";
        badge.textContent = "PAUSED";
      }
    });

    // When audio completes: the file ends, or playback reaches the end of the slice
    audio.addEventListener("ended", function () {
      if (!hasEnded) finishPlayback();
    });

    function finishPlayback() {
      hasEnded = true;
      if (progressBar) progressBar.style.width = "100%";
      updateTimeDisplay();

      if (!allowReplay) {
        // Formal exam mode: permanently lock playback
        btnPlay.disabled = true;
        btnPlay.classList.add("disabled");
        if (btnText) btnText.textContent = "ENDED";
        if (iconPlay) iconPlay.classList.remove("hidden");
        if (iconPause) iconPause.classList.add("hidden");
        if (badge) {
          badge.className = "badge badge--muted exam-audio__badge font-sign";
          badge.textContent = "LOCKED";
        }
        if (statusText) {
          statusText.innerHTML = "<strong>Audio completed</strong> · Replay not permitted";
        }
      } else {
        // Review or practice mode: allow replay from start
        if (btnText) btnText.textContent = "REPLAY";
        if (iconPlay) iconPlay.classList.remove("hidden");
        if (iconPause) iconPause.classList.add("hidden");
        if (badge) {
          badge.className = "badge badge--muted exam-audio__badge font-sign";
          badge.textContent = "FINISHED";
        }
        if (statusText) {
          statusText.textContent = "Playback finished · Click Replay to listen again";
        }
        maxTimeSeen = segStart;
      }
    }

    // Play/Pause button click
    btnPlay.addEventListener("click", function () {
      if (!allowReplay && hasEnded) return;

      if (hasEnded && allowReplay) {
        // Replay from the beginning of the recording (or of the slice)
        rewindToSegmentStart();
        hasEnded = false;
        audio.play().catch(function (err) {
          console.warn("Audio replay prevented:", err);
        });
        return;
      }

      if (audio.paused) {
        audio.play().catch(function (err) {
          console.warn("Audio playback prevented:", err);
        });
      } else {
        audio.pause();
      }
    });

    // Volume slider
    if (volumeSlider) {
      volumeSlider.addEventListener("input", function () {
        var val = parseFloat(volumeSlider.value) / 100;
        audio.volume = val;
        if (val === 0) {
          audio.muted = true;
          if (iconVol) iconVol.classList.add("hidden");
          if (iconMute) iconMute.classList.remove("hidden");
        } else {
          audio.muted = false;
          if (iconVol) iconVol.classList.remove("hidden");
          if (iconMute) iconMute.classList.add("hidden");
        }
      });
    }

    // Mute toggle
    if (btnMute) {
      btnMute.addEventListener("click", function () {
        audio.muted = !audio.muted;
        if (audio.muted) {
          if (iconVol) iconVol.classList.add("hidden");
          if (iconMute) iconMute.classList.remove("hidden");
          if (volumeSlider) volumeSlider.value = 0;
        } else {
          if (iconVol) iconVol.classList.remove("hidden");
          if (iconMute) iconMute.classList.add("hidden");
          if (volumeSlider) volumeSlider.value = (audio.volume || 0.85) * 100;
        }
      });
    }

    // Auto-play if requested and supported
    if (autoPlay) {
      audio.play().catch(function () {
        // Browser autoplay restrictions handled gracefully: user clicks Play
      });
    }
  }

  function initAll() {
    document.querySelectorAll("[data-exam-audio]").forEach(initPlayer);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initAll);
  } else {
    initAll();
  }

  // Export helper for dynamically injected audio elements
  window.initExamAudio = initAll;
})();
