(function (sitting) {
  "use strict";

  // 9. Modals Management
  sitting.openSubmitModal = function () {
    if (sitting.isSpeakingPreparing || sitting.isSpeakingRecording || sitting.isAudioUploading) {
      sitting.saveStatus.textContent = sitting.isAudioUploading
        ? 'Wait for the recording to finish uploading before submitting.'
        : (sitting.isSpeakingPreparing ? 'Wait for preparation to finish before submitting.' : 'Stop and save the recording before submitting.');
      sitting.saveStatus.className = 'save-status save-status--error';
      return;
    }
    sitting.updateProgressStats();
    var submitQueueMsg = document.getElementById('modal-sync-pending');
    var submitNowBtn = document.getElementById('btn-modal-submit-now');
    if (sitting.pendingQueue.length > 0 || sitting.notesDirty) {
      if (submitQueueMsg) submitQueueMsg.style.display = 'block';
      if (submitNowBtn) submitNowBtn.disabled = true;
    } else {
      if (submitQueueMsg) submitQueueMsg.style.display = 'none';
      if (submitNowBtn) submitNowBtn.disabled = false;
    }
    sitting.submitModal.style.display = 'flex';
  };

  sitting.bindHistoryGuards = function () {
    // 8. Exam Integrity Guards & Non-Happy Paths

    // History Trap: Intercept browser Back navigation and display Exit modal
    window.history.pushState({ page: 'exam-shell', q: sitting.currentDisplayOrder }, '', window.location.href);
    window.addEventListener('popstate', function () {
      window.history.pushState({ page: 'exam-shell', q: sitting.currentDisplayOrder }, '', window.location.href);
      sitting.exitModal.style.display = 'flex';
    });

    // Accidental tab close / refresh protection
    window.addEventListener('beforeunload', function (e) {
      if (!sitting.isSubmitting) {
        e.preventDefault();
        e.returnValue = 'Your exam attempt is currently in progress. Leaving may cause unsaved responses to be lost.';
        return e.returnValue;
      }
    });
  };

  sitting.bindSubmissionForms = function () {
    sitting.openSubmitBtns = document.querySelectorAll('.js-open-submit-modal');
    sitting.closeSubmitBtns = document.querySelectorAll('.js-close-submit-modal');
    sitting.openSubmitBtns.forEach(function (btn) {
      btn.addEventListener('click', sitting.openSubmitModal);
    });
    sitting.closeSubmitBtns.forEach(function (btn) {
      btn.addEventListener('click', function () {
        sitting.submitModal.style.display = 'none';
      });
    });

    sitting.openExitBtn = document.getElementById('btn-open-exit-modal');
    sitting.closeExitBtns = document.querySelectorAll('.js-close-exit-modal');
    if (sitting.openExitBtn) {
      sitting.openExitBtn.addEventListener('click', function () {
        sitting.exitModal.style.display = 'flex';
      });
    }
    sitting.closeExitBtns.forEach(function (btn) {
      btn.addEventListener('click', function () {
        sitting.exitModal.style.display = 'none';
      });
    });

    window.addEventListener('click', function (e) {
      if (e.target === sitting.submitModal) sitting.submitModal.style.display = 'none';
      if (e.target === sitting.exitModal) sitting.exitModal.style.display = 'none';
    });

    // Form submission cleans up storage buffer
    if (sitting.submitExamForm) {
      sitting.submitExamForm.addEventListener('submit', function (e) {
        if (sitting.isSubmitting) return;
        if (sitting.isSpeakingPreparing || sitting.isSpeakingRecording || sitting.isAudioUploading) {
          e.preventDefault();
          sitting.saveStatus.textContent = sitting.isAudioUploading
            ? 'Wait for the recording to finish uploading before submitting.'
            : (sitting.isSpeakingPreparing ? 'Wait for preparation to finish before submitting.' : 'Stop and save the recording before submitting.');
          sitting.saveStatus.className = 'save-status save-status--error';
          return;
        }
        e.preventDefault();
        sitting.prepareFinalDraftFields(sitting.submitExamForm);

        function submitWhenAnswersAreSynced() {
          if (sitting.pendingQueue.length === 0 && !sitting.notesDirty && !sitting.isFlushing) {
            if (window.stopExamAudio) window.stopExamAudio();
            sitting.isSubmitting = true;
            try {
              localStorage.removeItem(sitting.storageKey);
              localStorage.removeItem(sitting.audioStorageKey);
            } catch (err) {}
            sitting.submitExamForm.submit();
            return;
          }
          if (Date.now() >= sitting.expiresAt) return;
          if (Date.now() >= sitting.expiresAt) return;
          var submitQueueMsg = document.getElementById('modal-sync-pending');
          if (submitQueueMsg) submitQueueMsg.style.display = 'block';
          sitting.triggerFlush();
          setTimeout(submitWhenAnswersAreSynced, 100);
        }

        if (sitting.pendingQueue.length > 0 || sitting.notesDirty || sitting.isFlushing) {
          var submitNowBtn = document.getElementById('btn-modal-submit-now');
          if (submitNowBtn) submitNowBtn.disabled = true;
        }
        submitWhenAnswersAreSynced();
      });
    }

    if (sitting.autoSubmitForm) {
      sitting.autoSubmitForm.addEventListener('submit', function () {
        if (window.stopExamAudio) window.stopExamAudio();
        sitting.isSubmitting = true;
        try {
          localStorage.removeItem(sitting.storageKey);
          localStorage.removeItem(sitting.audioStorageKey);
        } catch (err) {}
      });
    }

    if (sitting.cancelExamForm) {
      sitting.cancelExamForm.addEventListener('submit', function () {
        if (window.stopExamAudio) window.stopExamAudio();
        sitting.isSubmitting = true;
        try {
          localStorage.removeItem(sitting.storageKey);
          localStorage.removeItem(sitting.audioStorageKey);
        } catch (err) {}
      });
    }
  };
})(window.ExamSitting);
