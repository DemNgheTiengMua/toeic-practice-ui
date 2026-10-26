(function (sitting) {
  "use strict";
  sitting.getQuestionAudioKey = function (item) {
    if (!item || !item.audioPath || !item.audioPath.trim()) return null;
    var key = item.audioPath.trim();
    if (item.audioStartMs != null || item.audioEndMs != null) {
      key += ':' + (item.audioStartMs || 0) + '-' + (item.audioEndMs || 0);
    }
    return key;
  };

  sitting.getAudioProgress = function (key) {
    if (!key || !sitting.playedAudios[key]) return { completed: false, currentTime: 0 };
    var entry = sitting.playedAudios[key];
    if (typeof entry === 'boolean') {
      return { completed: entry, currentTime: 0 };
    }
    return {
      completed: !!entry.completed,
      currentTime: Number(entry.currentTime) || 0
    };
  };
  sitting.recordAudioProgress = function (key, progress) {
    if (!key || !progress) return;
    var existing = sitting.getAudioProgress(key);
    var completed = !!(progress.completed || existing.completed);
    var currentTime = Math.max(existing.currentTime, Number(progress.currentTime) || 0);
    sitting.playedAudios[key] = {
      completed: completed,
      currentTime: currentTime
    };
    try {
      localStorage.setItem(sitting.audioStorageKey, JSON.stringify(sitting.playedAudios));
    } catch (e) {}
  };

  sitting.getAudioReviewSeconds = function (q) {
    if (!q) return 0;
    if (q.timerPolicyJson) {
      try {
        var p = JSON.parse(q.timerPolicyJson);
        if (p.timerMode === 'AUDIO_PLUS_CHECK' || p.timerMode === 'AudioDriven' || p.checkTimeSec > 0 || p.reviewSeconds > 0) {
          return p.reviewSeconds || p.checkTimeSec || 120;
        }
      } catch (e) {}
    }
    if (q.navigationMode === 'AUDIO_PLUS_CHECK') {
      return 120;
    }
    return 0;
  };
  sitting.checkAndStartAudioReview = function () {
    var curQ = sitting.questionMap[sitting.currentDisplayOrder];
    if (!curQ || sitting.hasStartedAudioReview) return;
    var reviewSec = sitting.getAudioReviewSeconds(curQ);
    if (reviewSec > 0) {
      sitting.hasStartedAudioReview = true;
      sitting.startAudioReviewWindow(reviewSec);
    }
  };
  sitting.startAudioReviewWindow = function (initialSeconds) {
    var timerAlert = document.getElementById('timer-alert-placard');
    var timerAlertMsg = document.getElementById('timer-alert-message');
    if (timerAlert && timerAlertMsg) {
      timerAlertMsg.textContent = 'POST-AUDIO REVIEW TIME';
      timerAlert.style.display = 'inline-flex';
    }

    var reviewRemaining = initialSeconds || 120;
    var csrfToken = document.querySelector('#anti-forgery-holder input[name="__RequestVerificationToken"]')?.value || '';

    fetch('/exams/take/' + sitting.attemptId + '/audio-review/start', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': csrfToken
      }
    }).then(function (res) {
      if (res.ok) return res.json();
    }).then(function (data) {
      if (data && typeof data.remainingSeconds === 'number' && data.remainingSeconds > 0) {
        reviewRemaining = data.remainingSeconds;
      }
    }).catch(function (err) {
      console.warn('Audio review start error:', err);
    });

    if (sitting.audioReviewTimer) clearInterval(sitting.audioReviewTimer);
    sitting.audioReviewTimer = setInterval(function () {
      reviewRemaining--;
      if (timerAlertMsg) {
        var m = Math.floor(Math.max(0, reviewRemaining) / 60);
        var s = Math.max(0, reviewRemaining) % 60;
        var timeStr = (m < 10 ? '0' + m : '' + m) + ':' + (s < 10 ? '0' + s : '' + s);
        timerAlertMsg.textContent = 'POST-AUDIO REVIEW TIME · ' + timeStr;
      }
      if (reviewRemaining <= 0) {
        clearInterval(sitting.audioReviewTimer);
        if (timerAlertMsg) {
          timerAlertMsg.textContent = 'POST-AUDIO REVIEW EXPIRED';
        }
      }
    }, 1000);
  };

  sitting.initializeAudio = function () {
    sitting.currentAudioKey = sitting.getQuestionAudioKey(sitting.questionMap[sitting.currentDisplayOrder]);
    sitting.audioStorageKey = 'ee_exam_audio_' + sitting.attemptId;
    sitting.playedAudios = {};
    try {
      sitting.rawStoredAudios = localStorage.getItem(sitting.audioStorageKey);
      if (sitting.rawStoredAudios) {
        sitting.playedAudios = JSON.parse(sitting.rawStoredAudios) || {};
      }
    } catch (e) {
      console.warn('Audio storage read error:', e);
    }
    // If initial question audio was already completed or listened in this attempt, re-render with saved progress
    sitting.initialProgress = sitting.getAudioProgress(sitting.currentAudioKey);
    if (sitting.currentAudioKey && (sitting.initialProgress.completed || sitting.initialProgress.currentTime > 0) && sitting.audioWrapper && window.createExamAudioHtml) {
      sitting.initialQ = sitting.questionMap[sitting.currentDisplayOrder] || sitting.sittingData.currentQuestion;
      sitting.audioWrapper.innerHTML = window.createExamAudioHtml({
        mode: 'exam',
        audioPath: sitting.initialQ.audioPath,
        startMs: sitting.initialQ.audioStartMs,
        endMs: sitting.initialQ.audioEndMs,
        playerId: 'main-exam-audio',
        completed: sitting.initialProgress.completed,
        currentTime: sitting.initialProgress.currentTime
      });
      if (window.initExamAudio) window.initExamAudio();
    }

    document.addEventListener('examaudiotimeupdate', function (e) {
      if (e.detail && typeof e.detail.currentTime === 'number') {
        sitting.currentAudioOffsetMs = Math.round(e.detail.currentTime * 1000);
      }
      if (sitting.currentAudioKey && e.detail) {
        sitting.recordAudioProgress(sitting.currentAudioKey, {
          currentTime: e.detail.currentTime,
          completed: e.detail.completed
        });
      }
    });

    sitting.hasStartedAudioReview = false;
    sitting.audioReviewTimer = null;
    document.addEventListener('examaudioended', function () {
      if (sitting.currentAudioKey) {
        sitting.recordAudioProgress(sitting.currentAudioKey, {
          completed: true
        });
      }
      sitting.checkAndStartAudioReview();
    });

    if (sitting.audioWrapper) {
      sitting.audioWrapper.addEventListener('ended', function () {
        sitting.checkAndStartAudioReview();
      }, true);
    }
  };
})(window.ExamSitting);
