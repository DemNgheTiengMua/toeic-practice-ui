(function (sitting) {
  "use strict";

  sitting.scheduleSync = function (immediate) {
    if (sitting.syncDebounceTimer) {
      clearTimeout(sitting.syncDebounceTimer);
      sitting.syncDebounceTimer = null;
    }
    if (immediate) {
      sitting.triggerSync();
    } else {
      sitting.syncDebounceTimer = setTimeout(sitting.triggerSync, 500);
    }
  };
  sitting.triggerFlush = function () {
    sitting.scheduleSync(true);
  };
  sitting.triggerSync = function () {
    if (sitting.isFlushing || (sitting.pendingQueue.length === 0 && !sitting.notesDirty && !sitting.positionDirty)) {
      if (sitting.pendingQueue.length === 0 && !sitting.notesDirty && !sitting.positionDirty) {
        sitting.saveStatus.textContent = 'Saved';
        sitting.saveStatus.className = 'save-status save-status--saved';
        var submitQueueMsg = document.getElementById('modal-sync-pending');
        var submitNowBtn = document.getElementById('btn-modal-submit-now');
        if (submitQueueMsg) submitQueueMsg.style.display = 'none';
        if (submitNowBtn) submitNowBtn.disabled = false;
      }
      return;
    }

    var wasNotesDirty = sitting.notesDirty;
    var wasPositionDirty = sitting.positionDirty;
    sitting.notesDirty = false;
    sitting.positionDirty = false;

    sitting.isFlushing = true;
    sitting.saveStatus.textContent = 'Saving…';
    sitting.saveStatus.className = 'save-status save-status--saving';

    var syncItems = sitting.pendingQueue.slice();
    var payload = {
      answers: syncItems.map(function (item) {
        return {
          questionId: item.questionId,
          selectedOptionId: (item.selectedOptionId !== null && item.selectedOptionId !== undefined) ? item.selectedOptionId : null,
          selectedOptionIdsJson: item.selectedOptionIdsJson || null,
          responseText: item.responseText || null,
          isFlagged: !!item.isFlagged
        };
      }),
      lastAudioOffsetMs: sitting.currentAudioOffsetMs,
      candidateNotesJson: sitting.candidateNotesJson || null,
      lastQuestionId: (sitting.questionMap[sitting.currentDisplayOrder] ? sitting.questionMap[sitting.currentDisplayOrder].questionId : null)
    };

    fetch('/exams/attempts/' + sitting.attemptId + '/sync', {
      method: 'POST',
      keepalive: true,
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': sitting.csrfToken,
        'X-Requested-With': 'XMLHttpRequest',
        'Accept': 'application/json'
      },
      body: JSON.stringify(payload)
    })
    .then(function (res) {
      if (!res.ok) {
        return res.json().then(function (err) { throw err; });
      }
      return res.json();
    })
    .then(function (data) {
      sitting.isFlushing = false;
      if (data.isAutoSubmitted && data.redirectUrl) {
        sitting.isSubmitting = true;
        sitting.timesUpModal.style.display = 'flex';
        window.location.href = data.redirectUrl;
        return;
      }

      if (data.phaseDeadlineAt !== undefined) {
        sitting.phaseDeadlineAt = sitting.parseDeadlineToEpochMs(data.phaseDeadlineAt);
      }

      var syncedQIds = syncItems.map(function (x) { return x.questionId; });
      sitting.pendingQueue = sitting.pendingQueue.filter(function (x) { return syncedQIds.indexOf(x.questionId) === -1; });
      try {
        localStorage.setItem(sitting.storageKey, JSON.stringify(sitting.pendingQueue));
      } catch (e) {}

      var networkWarning = document.getElementById('network-warning');
      if (networkWarning) networkWarning.style.display = 'none';

      if (sitting.pendingQueue.length > 0 || sitting.notesDirty || sitting.positionDirty) {
        sitting.scheduleSync(false);
      } else {
        sitting.saveStatus.textContent = 'Saved';
        sitting.saveStatus.className = 'save-status save-status--saved';
        var submitQueueMsg = document.getElementById('modal-sync-pending');
        var submitNowBtn = document.getElementById('btn-modal-submit-now');
        if (submitQueueMsg) submitQueueMsg.style.display = 'none';
        if (submitNowBtn) submitNowBtn.disabled = false;
      }
    })
    .catch(function (err) {
      sitting.isFlushing = false;
      if (wasNotesDirty) {
        sitting.notesDirty = true;
      }
      if (wasPositionDirty) {
        sitting.positionDirty = true;
      }
      if (err && err.code === 'attempt_expired') {
        sitting.isSubmitting = true;
        sitting.timesUpModal.style.display = 'flex';
        window.location.href = '/exams/result/' + sitting.attemptId;
        return;
      }

      var networkWarning = document.getElementById('network-warning');
      if (networkWarning) networkWarning.style.display = 'block';

      sitting.saveStatus.textContent = 'Offline · Saved locally';
      sitting.saveStatus.className = 'save-status save-status--error';

      setTimeout(function () {
        sitting.scheduleSync(true);
      }, 4000);
    });
  };
  sitting.saveAnswer = function (questionId, selectedOptionId, flagged, responseText, selectedOptionIdsJson) {
    // Immediate in-memory registration
    var previousAnswer = sitting.localAnswers[questionId] || {};
    sitting.localAnswers[questionId] = {
      selectedOptionId: selectedOptionId,
      selectedOptionIdsJson: typeof selectedOptionIdsJson === 'string' ? selectedOptionIdsJson : (previousAnswer.selectedOptionIdsJson || null),
      isFlagged: flagged,
      responseText: typeof responseText === 'string' ? responseText : (previousAnswer.responseText || ''),
      responseAudioPath: previousAnswer.responseAudioPath || null,
      responseDurationMs: previousAnswer.responseDurationMs || null
    };

    // Immediate Navigator cell UI reflection
    var cell = document.getElementById('nav-cell-' + questionId);
    if (cell) {
      var isAnswered = (selectedOptionId !== null && selectedOptionId !== undefined)
        || (sitting.localAnswers[questionId].responseText.trim().length > 0)
        || (sitting.localAnswers[questionId].selectedOptionIdsJson != null && sitting.localAnswers[questionId].selectedOptionIdsJson.length > 2)
        || !!sitting.localAnswers[questionId].responseAudioPath;
      if (isAnswered) {
        cell.classList.add('is-answered');
        cell.classList.remove('is-unanswered');
      } else {
        cell.classList.remove('is-answered');
        cell.classList.add('is-unanswered');
      }
      if (flagged) {
        cell.classList.add('is-marked');
      } else {
        cell.classList.remove('is-marked');
      }
    }

    sitting.updateProgressStats();

    // Enqueue save job (debouncing redundant requests for same question)
    sitting.pendingQueue = sitting.pendingQueue.filter(function (job) { return job.questionId !== questionId; });
    sitting.pendingQueue.push({
      questionId: questionId,
      selectedOptionId: selectedOptionId,
      selectedOptionIdsJson: sitting.localAnswers[questionId].selectedOptionIdsJson,
      responseText: sitting.localAnswers[questionId].responseText,
      isFlagged: flagged,
      retryCount: 0
    });

    try {
      localStorage.setItem(sitting.storageKey, JSON.stringify(sitting.pendingQueue));
    } catch (e) {}

    sitting.scheduleSync(false); // 500ms debounce
  };

  sitting.initializeSyncState = function () {
    sitting.isFlushing = false;
    sitting.isSubmitting = false;
  };

  sitting.initializeSyncDebounce = function () {
    // 5. Debounced Auto-Sync & LocalStorage Recovery Seam
    sitting.syncDebounceTimer = null;
  };

  sitting.bindNetwork = function () {
    // Online / Offline Detection
    sitting.networkWarning = document.getElementById('network-warning');
    window.addEventListener('online', function () {
      if (sitting.networkWarning) sitting.networkWarning.style.display = 'none';
      sitting.scheduleSync(true);
    });

    window.addEventListener('offline', function () {
      if (sitting.networkWarning) sitting.networkWarning.style.display = 'block';
      sitting.saveStatus.textContent = 'Offline · Saved locally';
      sitting.saveStatus.className = 'save-status save-status--error';
    });
  };
})(window.ExamSitting);
