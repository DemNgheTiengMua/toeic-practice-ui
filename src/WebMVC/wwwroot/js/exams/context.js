(function (window) {
  "use strict";

  // Shared attempt state stays live across the feature modules; initialize only after all modules load.
  var sitting = window.ExamSitting = {};

  sitting.escapeHtml = function (str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  };

  // Accepts epoch milliseconds, a numeric string, or an ISO timestamp and returns epoch ms.
  // The sitting payload and the transition endpoint use different shapes for the same instant.
  function toEpochMs(value) {
    if (value === null || value === undefined || value === '') return null;
    if (typeof value === 'number') return isFinite(value) ? value : null;
    var numeric = Number(value);
    if (!isNaN(numeric) && /^\d+$/.test(String(value).trim())) return numeric;
    var parsed = Date.parse(value);
    return isNaN(parsed) ? null : parsed;
  }

  sitting.initializeContext = function () {
    // 1. Data Model & Initialization
    sitting.examDataEl = document.getElementById('exam-data');
    sitting.questions = [];
    sitting.phaseData = [];
    sitting.sittingData = {};
    try {
      sitting.questions = JSON.parse(sitting.examDataEl ? sitting.examDataEl.textContent : '[]');
      sitting.phaseData = JSON.parse(document.getElementById('phase-data')?.textContent || '[]');
      sitting.sittingData = JSON.parse(document.getElementById('sitting-data')?.textContent || '{}');
    } catch (e) {
      console.error('Failed to parse exam payload:', e);
    }

    sitting.attemptId = sitting.sittingData.attemptId;
    sitting.totalQuestions = sitting.questions.length || sitting.sittingData.totalQuestions;
    sitting.currentDisplayOrder = sitting.sittingData.displayOrder;
    sitting.activeUnitNumber = sitting.sittingData.activeUnitNumber;
    sitting.activeGroupId = sitting.sittingData.activeGroupId;
    sitting.activeSkillId = sitting.sittingData.activeSkillId;
    // Normalise to epoch ms: sitting-data ships milliseconds, the transition endpoint ships an
    // ISO string, and the phase countdown must compare one shape.
    sitting.phaseDeadlineAt = toEpochMs(sitting.sittingData.phaseDeadlineAt);
    sitting.expiresAt = sitting.sittingData.expiresAt;
    sitting.attemptStartedAt = sitting.sittingData.attemptStartedAt;
    sitting.csrfToken = document.querySelector('#anti-forgery-holder input[name="__RequestVerificationToken"]').value;
    sitting.currentAudioOffsetMs = sitting.sittingData.lastAudioOffsetMs;
    sitting.candidateNotesJson = sitting.sittingData.candidateNotesJson;
    sitting.notesDirty = false;

    sitting.questionMap = {};
    sitting.questions.forEach(function (q) {
      sitting.questionMap[q.displayOrder] = q;
    });

    // Local Answer & State Registry (optimistic in-memory cache)
    sitting.localAnswers = {};
    sitting.questions.forEach(function (q) {
      sitting.localAnswers[q.questionId] = {
        selectedOptionId: q.selectedOptionId,
        selectedOptionIdsJson: q.selectedOptionIds != null ? JSON.stringify(q.selectedOptionIds) : null,
        isFlagged: q.isFlagged,
        responseText: q.responseText || '',
        responseAudioPath: q.responseAudioPath || null,
        responseDurationMs: q.responseDurationMs || null
      };
    });

    sitting.responseDrafts = {};
    sitting.questions.forEach(function (q) {
      if (q.questionType === 'free_response') {
        sitting.responseDrafts[q.questionId] = {
          text: q.responseText || '',
          dirty: false
        };
      }
    });

    // Restore uncommitted answers from localStorage backup if present
    sitting.storageKey = 'ee_exam_pending_' + sitting.attemptId;
    sitting.pendingQueue = [];
    try {
      sitting.rawStored = localStorage.getItem(sitting.storageKey);
      if (sitting.rawStored) {
        sitting.parsed = JSON.parse(sitting.rawStored);
        if (Array.isArray(sitting.parsed) && sitting.parsed.length > 0) {
          sitting.pendingQueue = sitting.parsed;
          sitting.pendingQueue.forEach(function (item) {
            if (sitting.localAnswers[item.questionId]) {
              sitting.localAnswers[item.questionId].selectedOptionId = item.selectedOptionId;
              sitting.localAnswers[item.questionId].isFlagged = item.isFlagged;
              if (typeof item.responseText === 'string') {
                sitting.localAnswers[item.questionId].responseText = item.responseText;
              }
            }
          });
        }
      }
    } catch (e) {
      console.warn('LocalStorage restore error:', e);
    }

    // UI Elements
    sitting.saveStatus = document.getElementById('save-status');
    sitting.timerDisplay = document.getElementById('timer-display');
    sitting.timerBox = document.getElementById('timer-box');
    sitting.topbarBadge = document.getElementById('topbar-section-badge');
    sitting.flagBtn = document.getElementById('flag-btn');
    sitting.btnPrev = document.getElementById('btn-prev');
    sitting.btnNext = document.getElementById('btn-next');
    sitting.optionList = document.getElementById('option-list');
    sitting.freeResponseContainer = document.getElementById('free-response-container');
    sitting.passageCard = document.getElementById('passage-card');
    sitting.passageText = document.getElementById('passage-text');
    sitting.imageContainer = document.getElementById('image-container');
    sitting.questionImage = document.getElementById('question-image');
    sitting.questionNumberDisplay = document.getElementById('question-number-display');
    sitting.questionSectionDisplay = document.getElementById('question-section-display');
    sitting.questionStemDisplay = document.getElementById('question-stem-display');
    sitting.audioWrapper = document.getElementById('exam-audio-wrapper');
    sitting.writingPane = document.getElementById('writing-response-pane');
    sitting.writingEditor = document.getElementById('writing-editor');
    sitting.wordCountDisplay = document.getElementById('word-count');
    sitting.characterCountDisplay = document.getElementById('character-count');
    sitting.wordRangeDisplay = document.getElementById('word-range');
    sitting.saveDraftButton = document.getElementById('btn-save-writing-draft');
    sitting.flaggedFilterButton = document.getElementById('flagged-filter-btn');

    sitting.clusterContainer = document.getElementById('cluster-card-container');
    sitting.questionMetaBar = document.getElementById('question-meta-bar');
    sitting.reviewScreenModal = document.getElementById('review-screen-modal');
    sitting.unitLockModal = document.getElementById('unit-lock-modal');
    sitting.groupLockModal = document.getElementById('group-lock-modal');
    sitting.btnConfirmGroupLock = document.getElementById('btn-confirm-group-lock');
    sitting.writingOnscreenToolbar = document.getElementById('writing-onscreen-toolbar');
    sitting.btnWritingCut = document.getElementById('btn-writing-cut');
    sitting.btnWritingPaste = document.getElementById('btn-writing-paste');
    sitting.btnWritingUndo = document.getElementById('btn-writing-undo');
    sitting.btnWritingRedo = document.getElementById('btn-writing-redo');
    sitting.liveWordCount = document.getElementById('live-word-count');
    sitting.minWordsBadge = document.getElementById('min-words-badge');
    sitting.underLengthWarning = document.getElementById('under-length-warning');
    sitting.requiredKeywordsArea = document.getElementById('required-keywords-area');
    sitting.requiredKeywordsList = document.getElementById('required-keywords-list');
    sitting.btnOpenReviewScreen = document.getElementById('btn-open-review-screen');
    sitting.btnReviewFirstUnanswered = document.getElementById('btn-review-first-unanswered');
    sitting.btnReviewOnlyUnanswered = document.getElementById('btn-review-only-unanswered');
    sitting.btnReviewOnlyFlagged = document.getElementById('btn-review-only-flagged');
    sitting.btnReviewShowAll = document.getElementById('btn-review-show-all');
    sitting.reviewQuestionsList = document.getElementById('review-questions-list');
    sitting.reviewStatAnswered = document.getElementById('review-stat-answered');
    sitting.reviewStatFlagged = document.getElementById('review-stat-flagged');
    sitting.reviewStatUnanswered = document.getElementById('review-stat-unanswered');
    sitting.reviewUnitTransitionContainer = document.getElementById('review-unit-transition-container');
    sitting.btnReviewFinishUnit = document.getElementById('btn-review-finish-unit');
    sitting.btnConfirmUnitLock = document.getElementById('btn-confirm-unit-lock');

    sitting.submitModal = document.getElementById('submit-modal');
    sitting.exitModal = document.getElementById('exit-modal');
    sitting.timesUpModal = document.getElementById('times-up-modal');
    sitting.autoSubmitForm = document.getElementById('auto-submit-form');
    sitting.submitExamForm = document.getElementById('submit-exam-form');
    sitting.cancelExamForm = document.getElementById('cancel-exam-form');

    sitting.phases = sitting.phaseData.map(function (phase) {
      return {
        skillId: phase.skillId,
        skillCode: phase.skillCode,
        skillName: phase.skillName,
        skillDisplayOrder: phase.skillDisplayOrder,
        startOffsetSeconds: phase.startOffsetSeconds,
        durationSeconds: phase.durationSeconds,
        questions: sitting.questions.filter(function (question) { return question.skillCode === phase.skillCode; })
      };
    });
    // The server's ActiveSkillId is the only phase authority; the wall clock never picks the phase.
    sitting.activePhaseIndex = sitting.activeSkillId
      ? sitting.phases.findIndex(function (phase) { return phase.skillId === sitting.activeSkillId; })
      : 0;
    if (sitting.activePhaseIndex < 0) sitting.activePhaseIndex = 0;
    sitting.flaggedOnly = false;
    sitting.draftSaveTimeout = null;
    sitting.draftSaveInFlight = {};
  };
})(window);
