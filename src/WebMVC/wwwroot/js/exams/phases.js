(function (sitting) {
  "use strict";

  sitting.getPhaseIndexAt = function (now) {
    var elapsedSeconds = Math.max(0, Math.floor((now - sitting.attemptStartedAt) / 1000));
    for (var i = 0; i < sitting.phases.length; i++) {
      if (elapsedSeconds < sitting.phases[i].startOffsetSeconds + sitting.phases[i].durationSeconds) return i;
    }
    return sitting.phases.length;
  };
  sitting.getQuestionPhaseIndex = function (question) {
    return sitting.phases.findIndex(function (phase) { return phase.skillCode === question.skillCode; });
  };
  sitting.formatTimer = function (seconds) {
    var minutes = Math.floor(seconds / 60);
    var remainder = seconds % 60;
    return (minutes < 10 ? '0' : '') + minutes + ':' + (remainder < 10 ? '0' : '') + remainder;
  };
  sitting.renderPhaseProgress = function (now) {
    var elapsedSeconds = Math.max(0, Math.floor((now - sitting.attemptStartedAt) / 1000));
    var currentIndex = sitting.getPhaseIndexAt(now);
    document.querySelectorAll('[data-phase-skill]').forEach(function (item, index) {
      var phase = sitting.phases[index];
      if (!phase) return;
      var timer = item.querySelector('[data-phase-timer]');
      var remaining = index < currentIndex
        ? 0
        : index === currentIndex
          ? Math.max(0, phase.startOffsetSeconds + phase.durationSeconds - elapsedSeconds)
          : phase.durationSeconds;
      item.setAttribute('aria-current', index === currentIndex ? 'step' : 'false');
      if (timer) timer.textContent = sitting.formatTimer(remaining);
    });

    if (currentIndex < sitting.phases.length && currentIndex !== sitting.activePhaseIndex) {
      sitting.activePhaseIndex = currentIndex;
      var phase = sitting.phases[currentIndex];
      var firstQuestion = phase.questions[0];
      var currentQuestion = sitting.questionMap[sitting.currentDisplayOrder];
      if (firstQuestion && (!currentQuestion || currentQuestion.skillCode !== phase.skillCode)) {
        sitting.renderQuestion(firstQuestion.displayOrder);
      }
      sitting.updateNavigationForPhase();
    }
  };

  // 7. Exam Timer Loop
  sitting.updateTimer = function () {
    var now = Date.now();
    sitting.renderPhaseProgress(now);
    var diff = Math.max(0, Math.floor((sitting.expiresAt - now) / 1000));

    var hours = Math.floor(diff / 3600);
    var minutes = Math.floor((diff % 3600) / 60);
    var seconds = diff % 60;

    var hStr = hours < 10 ? '0' + hours : '' + hours;
    var mStr = minutes < 10 ? '0' + minutes : '' + minutes;
    var sStr = seconds < 10 ? '0' + seconds : '' + seconds;

    sitting.timerDisplay.textContent = hStr + ':' + mStr + ':' + sStr;

    if (diff <= 300 && diff > 60) {
      sitting.timerBox.className = 'timer timer--warning';
    } else if (diff <= 60) {
      sitting.timerBox.className = 'timer timer--critical';
    }

    if (diff <= 0) {
      sitting.timerDisplay.textContent = '00:00:00';
      sitting.timerBox.className = 'timer timer--critical';
      if (!sitting.isSubmitting) {
        sitting.isSubmitting = true;
        sitting.timesUpModal.style.display = 'flex';
        setTimeout(function () {
          if (window.stopExamAudio) window.stopExamAudio();
          sitting.prepareFinalDraftFields(sitting.autoSubmitForm);
          try {
            localStorage.removeItem(sitting.storageKey);
            localStorage.removeItem(sitting.audioStorageKey);
          } catch (err) {}
          sitting.autoSubmitForm.submit();
        }, 1000);
      }
    }
  };

  sitting.startTimer = function () {
    sitting.updateTimer();
    setInterval(sitting.updateTimer, 1000);
  };
})(window.ExamSitting);
