/**
 * Shared audio player for continuous exam playback and interactive study playback.
 */
(function () {
  "use strict";

  function formatTime(seconds) {
    if (isNaN(seconds) || !isFinite(seconds) || seconds < 0) return "00:00";
    var wholeSeconds = Math.floor(seconds);
    var minutes = Math.floor(wholeSeconds / 60);
    var remainder = wholeSeconds % 60;
    return (minutes < 10 ? "0" + minutes : minutes) + ":" +
      (remainder < 10 ? "0" + remainder : remainder);
  }

  function initPlayer(player) {
    if (player.dataset.initialized === "true") return;

    var audio = player.querySelector(".exam-audio__native");
    var playButton = player.querySelector(".exam-audio__btn-play");
    var buttonText = player.querySelector(".exam-audio__btn-text");
    var playIcon = player.querySelector(".exam-audio__icon-play");
    var pauseIcon = player.querySelector(".exam-audio__icon-pause");
    var badge = player.querySelector(".exam-audio__badge");
    var progress = player.querySelector(".exam-audio__progress");
    var progressBar = player.querySelector(".exam-audio__bar");
    var timer = player.querySelector(".exam-audio__timer");
    var status = player.querySelector(".exam-audio__status");
    var muteButton = player.querySelector(".exam-audio__btn-mute");
    var volumeIcon = player.querySelector(".exam-audio__icon-vol");
    var muteIcon = player.querySelector(".exam-audio__icon-mute");
    var volumeSlider = player.querySelector(".exam-audio__slider");
    var rewindButton = player.querySelector(".exam-audio__btn-rewind");
    var forwardButton = player.querySelector(".exam-audio__btn-forward");

    if (!audio || !playButton || !progress) return;
    player.dataset.initialized = "true";

    var mode = player.dataset.mode ||
      (player.dataset.allowReplay === "true" ? "study" : "exam");
    var isExam = mode === "exam";
    var startMs = parseInt(player.dataset.startMs, 10);
    var endMs = parseInt(player.dataset.endMs, 10);
    var hasSegment = Number.isInteger(startMs) && Number.isInteger(endMs) &&
      startMs >= 0 && endMs > startMs;
    var segmentStart = hasSegment ? startMs / 1000 : 0;
    var segmentEnd = hasSegment ? endMs / 1000 : null;
    var started = false;
    var completed = false;
    var internalSeek = false;
    var lastTimeSeen = segmentStart;
    var pointerIsDown = false;
    var scrubWasCompleted = false;

    function lowerBound() {
      return segmentStart;
    }

    function upperBound() {
      if (hasSegment) {
        return isFinite(audio.duration) ? Math.min(segmentEnd, audio.duration) : segmentEnd;
      }
      return isFinite(audio.duration) ? audio.duration : null;
    }

    function duration() {
      var end = upperBound();
      return end === null ? 0 : Math.max(0, end - lowerBound());
    }

    function clampTime(seconds) {
      var end = upperBound();
      var target = Math.max(lowerBound(), seconds);
      return end === null ? target : Math.min(end, target);
    }

    function updateTime() {
      var length = duration();
      var elapsed = Math.max(0, audio.currentTime - lowerBound());
      if (length > 0) elapsed = Math.min(length, elapsed);

      if (timer) timer.textContent = formatTime(elapsed) + " / " + formatTime(length);
      if (progressBar) {
        var percent = length > 0 ? Math.min(100, Math.max(0, elapsed / length * 100)) : 0;
        progressBar.style.width = percent + "%";
        progress.setAttribute("aria-valuenow", String(Math.round(percent)));
      }
    }

    function updateState() {
      var label;
      var badgeText;
      if (completed) {
        label = isExam ? "COMPLETED" : "REPLAY";
        badgeText = isExam ? "COMPLETED" : "FINISHED";
      } else if (isExam && started) {
        label = "PLAYING";
        badgeText = "PLAYING";
      } else if (!audio.paused && !audio.ended) {
        label = "PAUSE";
        badgeText = "PLAYING";
      } else {
        label = isExam ? "PLAY AUDIO" : "PLAY";
        badgeText = started ? "PAUSED" : "READY";
      }

      if (buttonText) buttonText.textContent = label;
      playButton.setAttribute("aria-label", isExam && started && !completed
        ? "Audio is playing"
        : (completed && isExam ? "Audio completed" : (audio.paused ? "Play audio" : "Pause audio")));
      playButton.disabled = isExam && (started || completed);
      playButton.classList.toggle("disabled", playButton.disabled);
      if (playIcon) playIcon.classList.toggle("hidden", !audio.paused && !completed);
      if (pauseIcon) pauseIcon.classList.toggle("hidden", audio.paused || completed || isExam);
      if (badge) {
        badge.textContent = badgeText;
        badge.className = "badge exam-audio__badge font-sign " +
          (completed ? "badge--muted" : (!audio.paused ? "badge--info" : (started ? "badge--warning" : "badge--info")));
      }
      if (status) {
        status.textContent = completed
          ? (isExam ? "Audio completed" : "Playback finished")
          : (!audio.paused ? "Audio playing" : (started ? "Audio paused" : "Audio ready"));
      }
    }

    function seekTo(seconds) {
      var target = clampTime(seconds);
      if (Math.abs(audio.currentTime - target) > 0.001) {
        internalSeek = true;
        audio.currentTime = target;
      }
      lastTimeSeen = target;
      completed = false;
      updateTime();
      updateState();
      return target;
    }

    function playStudyFromCurrentPosition() {
      if (audio.currentTime >= (upperBound() === null ? Infinity : upperBound())) {
        seekTo(lowerBound());
      }
      audio.play().catch(function (error) {
        console.warn("Audio playback prevented:", error);
      });
    }

    function finishPlayback() {
      if (completed) return;
      completed = true;
      if (hasSegment) audio.currentTime = segmentEnd;
      updateTime();
      updateState();
      if (!audio.paused) audio.pause();
    }

    function stopAtBoundary() {
      var end = upperBound();
      if (end !== null && audio.currentTime >= end) {
        finishPlayback();
        return true;
      }
      return false;
    }

    function watchBoundary() {
      if (audio.paused || completed || stopAtBoundary()) return;
      window.requestAnimationFrame(watchBoundary);
    }

    function seekFromPointer(event) {
      var bounds = progress.getBoundingClientRect();
      if (bounds.width <= 0) return;
      var fraction = Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width));
      seekTo(lowerBound() + fraction * duration());
    }

    function finishScrub(event) {
      if (!pointerIsDown) return;
      pointerIsDown = false;
      if (progress.hasPointerCapture(event.pointerId)) progress.releasePointerCapture(event.pointerId);
      seekFromPointer(event);
      if (scrubWasCompleted) playStudyFromCurrentPosition();
      scrubWasCompleted = false;
    }

    audio.addEventListener("loadedmetadata", function () {
      if (hasSegment && audio.currentTime < segmentStart) seekTo(segmentStart);
      updateTime();
      updateState();
    });

    audio.addEventListener("durationchange", updateTime);
    audio.addEventListener("timeupdate", function () {
      if (!isExam) {
        var clampedTime = clampTime(audio.currentTime);
        if (Math.abs(clampedTime - audio.currentTime) > 0.001) {
          seekTo(clampedTime);
        }
      }
      lastTimeSeen = audio.currentTime;
      if (!audio.paused && stopAtBoundary()) return;
      updateTime();
    });

    audio.addEventListener("seeking", function () {
      if (internalSeek) {
        internalSeek = false;
        return;
      }

      if (isExam) {
        if (Math.abs(audio.currentTime - lastTimeSeen) > 0.75) seekTo(lastTimeSeen);
        return;
      }

      var target = clampTime(audio.currentTime);
      if (Math.abs(target - audio.currentTime) > 0.001) seekTo(target);
    });

    audio.addEventListener("play", function () {
      document.querySelectorAll("audio").forEach(function (other) {
        if (other !== audio && !other.paused) other.pause();
      });

      if (completed && isExam) {
        audio.pause();
        return;
      }
      if (audio.currentTime < lowerBound() || (upperBound() !== null && audio.currentTime >= upperBound())) {
        seekTo(lowerBound());
      }
      if (!isExam) completed = false;
      started = true;
      updateState();
      window.requestAnimationFrame(watchBoundary);
    });

    audio.addEventListener("pause", function () {
      if (isExam && started && !completed && !audio.ended) {
        audio.play().catch(function (error) {
          console.warn("Continuous exam audio could not resume:", error);
        });
        return;
      }
      updateState();
    });

    audio.addEventListener("ended", finishPlayback);

    playButton.addEventListener("click", function () {
      if (isExam) {
        if (started || completed) return;
        if (audio.currentTime < lowerBound()) seekTo(lowerBound());
        audio.play().catch(function (error) {
          console.warn("Exam audio playback prevented:", error);
        });
        return;
      }

      if (completed) {
        seekTo(lowerBound());
        playStudyFromCurrentPosition();
      } else if (audio.paused) {
        playStudyFromCurrentPosition();
      } else {
        audio.pause();
      }
    });

    if (!isExam) {
      progress.addEventListener("pointerdown", function (event) {
        pointerIsDown = true;
        scrubWasCompleted = completed;
        progress.setPointerCapture(event.pointerId);
        seekFromPointer(event);
      });
      progress.addEventListener("pointermove", function (event) {
        if (!pointerIsDown) return;
        seekFromPointer(event);
      });
      progress.addEventListener("pointerup", finishScrub);
      progress.addEventListener("pointercancel", function () {
        pointerIsDown = false;
        scrubWasCompleted = false;
      });
      progress.addEventListener("keydown", function (event) {
        var delta = event.key === "ArrowLeft" ? -5 : (event.key === "ArrowRight" ? 5 : 0);
        if (event.key === "Home") delta = lowerBound() - audio.currentTime;
        if (event.key === "End") delta = (upperBound() || audio.currentTime) - audio.currentTime;
        if (delta === 0 && event.key !== "Home" && event.key !== "End") return;
        event.preventDefault();
        var wasCompleted = completed;
        seekTo(audio.currentTime + delta);
        if (wasCompleted) playStudyFromCurrentPosition();
      });
    }

    function skip(seconds) {
      var wasCompleted = completed;
      seekTo(audio.currentTime + seconds);
      if (wasCompleted) playStudyFromCurrentPosition();
    }

    if (rewindButton) rewindButton.addEventListener("click", function () { skip(-5); });
    if (forwardButton) forwardButton.addEventListener("click", function () { skip(5); });

    if (volumeSlider) {
      audio.volume = Number(volumeSlider.value) / 100;
      volumeSlider.addEventListener("input", function () {
        audio.volume = Number(volumeSlider.value) / 100;
        audio.muted = audio.volume === 0;
        if (volumeIcon) volumeIcon.classList.toggle("hidden", audio.muted);
        if (muteIcon) muteIcon.classList.toggle("hidden", !audio.muted);
        if (muteButton) muteButton.setAttribute("aria-label", audio.muted ? "Unmute audio" : "Mute audio");
      });
    }

    if (muteButton) {
      muteButton.addEventListener("click", function () {
        audio.muted = !audio.muted;
        if (volumeIcon) volumeIcon.classList.toggle("hidden", audio.muted);
        if (muteIcon) muteIcon.classList.toggle("hidden", !audio.muted);
        if (volumeSlider && !audio.muted && Number(volumeSlider.value) === 0) volumeSlider.value = "85";
        if (muteButton) muteButton.setAttribute("aria-label", audio.muted ? "Unmute audio" : "Mute audio");
      });
    }

    updateTime();
    updateState();
    if (autoPlay()) {
      audio.play().catch(function (error) {
        console.warn("Audio autoplay prevented:", error);
      });
    }

    function autoPlay() {
      return player.dataset.autoplay === "true";
    }
  }

  function initAll() {
    document.querySelectorAll("[data-exam-audio]").forEach(initPlayer);
  }

  window.createExamAudioHtml = function (options) {
    if (!options || (options.mode !== "exam" && options.mode !== "study")) {
      throw new Error("A valid exam audio mode is required.");
    }
    var template = document.getElementById("exam-audio-template");
    if (!template || !template.content || !template.content.firstElementChild) {
      throw new Error("The shared exam audio template is not available.");
    }

    var player = template.content.firstElementChild.cloneNode(true);
    var audio = player.querySelector(".exam-audio__native");
    var mode = options.mode || "exam";
    player.dataset.mode = mode;
    player.dataset.allowReplay = mode === "study" ? "true" : "false";
    player.dataset.autoplay = "false";
    if (options.playerId) player.id = options.playerId;
    if (options.startMs == null) player.removeAttribute("data-start-ms");
    else player.dataset.startMs = String(options.startMs);
    if (options.endMs == null) player.removeAttribute("data-end-ms");
    else player.dataset.endMs = String(options.endMs);
    if (audio) audio.src = options.audioPath || "";

    return player.outerHTML;
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initAll);
  } else {
    initAll();
  }

  window.initExamAudio = initAll;
})();
