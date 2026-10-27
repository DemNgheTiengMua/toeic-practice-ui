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
    // Display-only phase index: derived from the clock so the progress strip keeps ticking,
    // but it never mutates sitting.activePhaseIndex. The server's ActiveSkillId owns the phase
    // (and the transition endpoints are the only way to change it) — ticket d01.
    var displayIndex = sitting.getPhaseIndexAt(now);
    document.querySelectorAll('[data-phase-skill]').forEach(function (item, index) {
      var phase = sitting.phases[index];
      if (!phase) return;
      var timer = item.querySelector('[data-phase-timer]');
      var remaining;
      if (index < sitting.activePhaseIndex) {
        remaining = 0;
      } else if (index === sitting.activePhaseIndex) {
        // The active phase counts down from the server's deadline. After a transition the clock
        // no longer lines up with the original phase offsets, so prefer the deadline and fall back
        // to the start-offset maths only when the server did not supply one.
        remaining = sitting.phaseDeadlineAt
          ? Math.max(0, Math.ceil((sitting.phaseDeadlineAt - now) / 1000))
          : Math.max(0, phase.startOffsetSeconds + phase.durationSeconds - Math.max(0, Math.floor((now - sitting.attemptStartedAt) / 1000)));
      } else if (index < displayIndex) {
        remaining = 0;
      } else if (index === displayIndex) {
        remaining = Math.max(0, phase.startOffsetSeconds + phase.durationSeconds - Math.max(0, Math.floor((now - sitting.attemptStartedAt) / 1000)));
      } else {
        remaining = phase.durationSeconds;
      }
      item.setAttribute('aria-current', index === sitting.activePhaseIndex ? 'step' : 'false');
      if (timer) timer.textContent = sitting.formatTimer(remaining);
    });
  };

  // Adopts a transition-endpoint response as the new authoritative phase state (tickets d02, d03).
  sitting.applySkillTransition = function (result) {
    if (!result) return false;
    if (typeof result.activeSkillId === 'number') {
      sitting.activeSkillId = result.activeSkillId;
      var idx = sitting.phases.findIndex(function (phase) { return phase.skillId === result.activeSkillId; });
      if (idx >= 0) sitting.activePhaseIndex = idx;
    }
    if (typeof result.activeUnitNumber === 'number') sitting.activeUnitNumber = result.activeUnitNumber;
    if (result.activeGroupId !== undefined) sitting.activeGroupId = result.activeGroupId;
    // `remainingSeconds` is min(phase deadline, attempt expiry), so it must NOT overwrite
    // `expiresAt` — doing that would shrink the attempt's total remaining time to the length of
    // the new phase. Only the phase deadline moves here; expiresAt keeps the attempt's own clock.
    // A null deadline means the engine fell back to the attempt's own expiry, and keeping the old
    // skill's deadline would count the new skill down to a moment that has already passed.
    sitting.phaseDeadlineAt = sitting.parseDeadlineToEpochMs(result.phaseDeadlineAt);
    sitting.phaseExpiredHandled = false;
    return true;
  };

  // Moves the candidate to the first question of the next skill via the transition endpoint.
  // On failure the candidate stays put and is told why (never a silent dead button).
  sitting.advanceToNextSkill = function (onSuccess) {
    if (sitting.isAdvancingSkill) return;
    sitting.isAdvancingSkill = true;
    if (sitting.btnNext) sitting.btnNext.disabled = true;
    sitting.saveStatus.textContent = 'Saving…';
    sitting.saveStatus.className = 'save-status save-status--saving';

    // Answers are queued behind a 500ms debounce, so answering the last question of a skill and
    // then clicking advance is a race the transition would win — the pending answer would then be
    // rejected as WrongPhase and retry forever. Flush first, and only advance once it settles.
    return Promise.resolve(sitting.flushThenTransition(onSuccess));
  };

  // Resolves once the pending answer queue has drained (or immediately when empty). Never rejects:
  // a sync failure must not silently swallow the transition attempt, but it must not fake success
  // either — the caller re-checks the queue and reports it.
  sitting.flushPendingAnswers = function () {
    if (!sitting.pendingQueue || sitting.pendingQueue.length === 0) return Promise.resolve();
    return new Promise(function (resolve) {
      var deadline = Date.now() + 5000;
      (function waitForDrain() {
        if (!sitting.pendingQueue || sitting.pendingQueue.length === 0 || Date.now() > deadline) {
          resolve();
          return;
        }
        sitting.triggerSync();
        setTimeout(waitForDrain, 150);
      })();
    });
  };

  sitting.flushThenTransition = function (onSuccess) {
    return sitting.flushPendingAnswers().then(function () {
      if (sitting.pendingQueue && sitting.pendingQueue.length > 0) {
        // Still queued: advancing now would strand these answers in the old phase.
        sitting.isAdvancingSkill = false;
        if (sitting.btnNext) sitting.btnNext.disabled = false;
        sitting.saveStatus.textContent = 'Answer still saving · retry';
        sitting.saveStatus.className = 'save-status save-status--error';
        sitting.showPhaseAdvanceError('Your last answer has not been saved yet. Wait a moment and try again.');
        return;
      }
      return sitting.performSkillTransition(onSuccess);
    });
  };

  sitting.performSkillTransition = function (onSuccess) {
    if (sitting.btnNext) sitting.btnNext.disabled = true;
    sitting.saveStatus.textContent = 'Saving…';
    sitting.saveStatus.className = 'save-status save-status--saving';

    fetch('/exams/take/' + sitting.attemptId + '/transition/skill', {
      method: 'POST',
      headers: {
        'RequestVerificationToken': sitting.csrfToken,
        'Content-Type': 'application/json',
        'Accept': 'application/json'
      }
    }).then(function (res) {
      return res.text().then(function (body) {
        var payload = null;
        try { payload = body ? JSON.parse(body) : null; } catch (e) { payload = null; }
        if (!res.ok) {
          var err = new Error((payload && (payload.error || payload.title)) || ('Phase advance failed (' + res.status + ')'));
          err.code = payload && payload.code;
          err.status = res.status;
          throw err;
        }
        return payload;
      });
    }).then(function (result) {
      // The server already moved, so nothing downstream may be reported as a transition failure:
      // clear the advance latch and the error banner before rendering, and guard the render.
      sitting.isAdvancingSkill = false;
      sitting.clearPhaseAdvanceError();
      sitting.applySkillTransition(result);
      if (result && result.isSubmitted) {
        window.location.href = '/exams/result/' + sitting.attemptId;
        return;
      }
      sitting.saveStatus.textContent = 'Saved';
      sitting.saveStatus.className = 'save-status save-status--saved';
      try {
        if (typeof onSuccess === 'function') onSuccess(result);
        var phase = sitting.phases[sitting.activePhaseIndex];
        var first = phase && phase.questions && phase.questions[0];
        if (first) {
          sitting.renderQuestion(first.displayOrder);
          // Keep the server payload in step with the adopted phase so a later reload or resume
          // reports the same question the candidate is looking at (ticket d01).
          sitting.sittingData.displayOrder = first.displayOrder;
        } else {
          sitting.updateNavigationForPhase();
        }
      } catch (renderError) {
        // The phase change itself succeeded; only the local render failed. Reload so the shell
        // re-derives everything from the server instead of retrying the transition.
        window.location.reload();
      }
    }).catch(function (err) {
      sitting.isAdvancingSkill = false;
      if (sitting.btnNext) sitting.btnNext.disabled = false;
      sitting.saveStatus.textContent = 'Not advanced · ' + (err.message || 'retry');
      sitting.saveStatus.className = 'save-status save-status--error';
      if (err.status === 400 && err.code === 'ATTEMPT_NOT_ACTIVE') {
        sitting.phaseAdvanceError = 'This attempt is no longer in progress and cannot be resumed. Open your exam history to see the result.';
      } else {
        sitting.phaseAdvanceError = err.message || 'Could not advance to the next skill. Please retry.';
      }
      sitting.showPhaseAdvanceError(sitting.phaseAdvanceError);
    });
  };

  sitting.clearPhaseAdvanceError = function () {
    var host = document.getElementById('phase-advance-error');
    if (host) {
      host.textContent = '';
      host.style.display = 'none';
    }
  };

  // Called once when the active skill's deadline passes without the candidate pressing advance.
  // Advancing beats expiring: the server refuses writes once the phase deadline has passed, so
  // the only way to keep the attempt usable is to hand the candidate to the next skill.
  sitting.handlePhaseExpiry = function () {
    if (sitting.phaseExpiredHandled) return;
    sitting.phaseExpiredHandled = true;
    var nextPhase = sitting.phases[sitting.activePhaseIndex + 1];
    if (!nextPhase) {
      // Last skill: there is nothing to advance to, so submit rather than strand the candidate.
      if (sitting.autoSubmitForm) {
        sitting.isSubmitting = true;
        sitting.prepareFinalDraftFields(sitting.autoSubmitForm);
        sitting.autoSubmitForm.submit();
      }
      return;
    }
    sitting.saveStatus.textContent = 'Time up for this skill · advancing…';
    sitting.saveStatus.className = 'save-status save-status--saving';
    sitting.advanceToNextSkill(function () {
      sitting.phaseExpiredHandled = false;
    }).then(function () {
      // A failed advance leaves phaseExpiredHandled set only until the next retry window, so a
      // transient network error does not permanently disable the fallback.
      if (!sitting.isAdvancingSkill) sitting.phaseExpiredHandled = false;
    }, function () {
      sitting.phaseExpiredHandled = false;
    });
  };

  sitting.showPhaseAdvanceError = function (message) {
    var host = document.getElementById('phase-advance-error');
    if (!host) {
      host = document.createElement('div');
      host.id = 'phase-advance-error';
      host.className = 'alert alert--danger';
      host.setAttribute('role', 'alert');
      host.style.margin = '0.75rem 0';
      if (sitting.btnNext && sitting.btnNext.parentNode) {
        sitting.btnNext.parentNode.insertBefore(host, sitting.btnNext);
      } else {
        document.body.appendChild(host);
      }
    }
    host.textContent = message;
    host.style.display = '';
  };

  // 7. Exam Timer Loop
  sitting.updateTimer = function () {
    var now = Date.now();
    sitting.renderPhaseProgress(now);

    // Phase-expiry fallback (ticket d02): if the candidate never clicks the advance control, the
    // skill must still change hands at its deadline. Without this, every save after the deadline
    // throws AttemptExpired while the attempt stays open, ejecting the candidate to /exams.
    if (sitting.phaseDeadlineAt && now >= sitting.phaseDeadlineAt && sitting.attemptExpiring !== true) {
      if (!sitting.isAdvancingSkill && !sitting.isSubmitting) {
        sitting.handlePhaseExpiry();
      }
    }

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
