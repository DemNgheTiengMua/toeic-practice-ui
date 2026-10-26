(function (sitting) {
  "use strict";

  // Keep initialization, event binding, and first render in the original shell order.
  sitting.initializeContext();
  sitting.initializeAudio();
  sitting.initializeSyncState();
  sitting.initializeSpeaking();
  sitting.initializeWriting();
  sitting.initializeAnnotations();
  sitting.bindAnnotationControls();
  sitting.bindDraftControls();
  sitting.bindFlagFilter();
  sitting.initializeSyncDebounce();
  sitting.bindPreviousNext();
  sitting.bindReviewControls();
  sitting.bindGroupControls();
  sitting.bindWritingToolbar();
  sitting.bindUnitControl();
  sitting.bindPalette();
  sitting.startTimer();
  sitting.bindHistoryGuards();
  sitting.bindNetwork();
  sitting.bindSubmissionForms();

  sitting.initAccessibility();
  sitting.initVolume();

  // Start initial queue flush if uncommitted items restored
  sitting.renderQuestion(sitting.currentDisplayOrder);
  if (sitting.pendingQueue.length > 0) {
    sitting.triggerFlush();
  }
  sitting.renderPhaseProgress(Date.now());
  sitting.initialQuestion = sitting.questionMap[sitting.currentDisplayOrder];
  if (sitting.initialQuestion && sitting.initialQuestion.questionType === 'free_response') {
    sitting.updateWritingCounters(sitting.initialQuestion);
  }
})(window.ExamSitting);
