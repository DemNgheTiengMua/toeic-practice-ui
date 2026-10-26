(function (sitting) {
  "use strict";
  sitting.countWords = function (text) {
    var trimmed = (text || '').trim();
    return trimmed ? trimmed.split(/\s+/).length : 0;
  };
  sitting.stripSuffixes = function (word) {
    if (!word) return '';
    if (word.endsWith('ations') && word.length > 7) return word.slice(0, -6);
    if (word.endsWith('ation') && word.length > 6) return word.slice(0, -5);
    if (word.endsWith('tion') && word.length > 6) return word.slice(0, -4);
    if (word.endsWith('ment') && word.length > 6) return word.slice(0, -4);
    if (word.endsWith('ing') && word.length > 5) return word.slice(0, -3);
    if (word.endsWith('ed') && word.length > 4) return word.slice(0, -2);
    if (word.endsWith('es') && word.length > 4) return word.slice(0, -2);
    if (word.endsWith('s') && word.length > 3) return word.slice(0, -1);
    if (word.endsWith('e') && word.length > 4) return word.slice(0, -1);
    return word;
  };
  sitting.containsKeywordStem = function (text, keyword) {
    if (!keyword) return true;
    var kw = keyword.trim().toLowerCase();
    var lower = (text || '').toLowerCase();
    if (lower.indexOf(kw) !== -1) return true;
    var stem = sitting.stripSuffixes(kw);
    if (stem.length >= 3 && lower.indexOf(stem) !== -1) return true;
    var words = lower.split(/[\s,.\!?;\:]+/).filter(Boolean);
    for (var i = 0; i < words.length; i++) {
      var w = words[i];
      if (w.indexOf(stem) !== -1 || sitting.stripSuffixes(w) === stem) return true;
    }
    return false;
  };
  sitting.parseKeywordList = function (keywordsStr) {
    if (!keywordsStr) return [];
    var trimmed = keywordsStr.trim();
    if (trimmed.startsWith('[') && trimmed.endsWith(']')) {
      try {
        var arr = JSON.parse(trimmed);
        if (Array.isArray(arr)) return arr.filter(Boolean);
      } catch (e) {}
    }
    return trimmed.split(/[,;]+/).map(function (k) { return k.trim(); }).filter(Boolean);
  };
  sitting.updateRequiredKeywordsMatch = function (q, text) {
    if (!sitting.requiredKeywordsList || !q || !q.requiredKeywords) return;
    var badges = sitting.requiredKeywordsList.querySelectorAll('.keyword-badge');
    badges.forEach(function (badge) {
      var kw = badge.getAttribute('data-keyword') || '';
      var matched = sitting.containsKeywordStem(text, kw);
      if (matched) {
        badge.className = 'keyword-badge badge--used';
        badge.setAttribute('title', 'Used');
      } else {
        badge.className = 'keyword-badge badge--pending';
        badge.setAttribute('title', 'Not used yet');
      }
    });
  };
  sitting.renderRequiredKeywords = function (q) {
    if (!sitting.requiredKeywordsArea || !sitting.requiredKeywordsList) return;
    var kws = sitting.parseKeywordList(q ? q.requiredKeywords : null);
    if (kws.length === 0) {
      sitting.requiredKeywordsArea.style.display = 'none';
      sitting.requiredKeywordsList.innerHTML = '';
      return;
    }
    sitting.requiredKeywordsArea.style.display = '';
    var html = '';
    kws.forEach(function (kw) {
      html += '<span class="keyword-badge badge--pending" data-keyword="' + sitting.escapeHtml(kw) + '">' + sitting.escapeHtml(kw) + '</span>';
    });
    sitting.requiredKeywordsList.innerHTML = html;
    var text = (sitting.writingEditor ? sitting.writingEditor.value : '') || '';
    sitting.updateRequiredKeywordsMatch(q, text);
  };

  sitting.pushUndoState = function () {
    if (!sitting.writingEditor || !sitting.isTrackingUndo) return;
    var currentText = sitting.writingEditor.value;
    if (sitting.undoStack.length === 0 || sitting.undoStack[sitting.undoStack.length - 1] !== currentText) {
      sitting.undoStack.push(currentText);
      if (sitting.undoStack.length > 50) sitting.undoStack.shift();
      sitting.redoStack = [];
    }
  };
  sitting.isCutPasteRestricted = function (q) {
    if (!q) return false;
    var cfg = sitting.parseToolsConfig(q.toolsConfigJson);
    return cfg.cutPaste === true || cfg.allowKeyboardCutPaste === false || cfg.showOnScreenCutPaste === true || cfg.onScreenCutPasteOnly === true;
  };
  sitting.updateWritingEditorTools = function (q) {
    if (!sitting.writingEditor) return;
    var cfg = sitting.parseToolsConfig(q ? q.toolsConfigJson : null);
    var disableSpellCheck = cfg.disableSpellCheck !== false && cfg.disableSpellcheck !== false;
    sitting.writingEditor.setAttribute('spellcheck', disableSpellCheck ? 'false' : 'true');
    sitting.writingEditor.setAttribute('autocorrect', disableSpellCheck ? 'off' : 'on');
    sitting.writingEditor.setAttribute('autocomplete', disableSpellCheck ? 'off' : 'on');
    sitting.writingEditor.setAttribute('autocapitalize', disableSpellCheck ? 'off' : 'on');

    var isRestricted = sitting.isCutPasteRestricted(q);
    if (sitting.writingOnscreenToolbar) {
      sitting.writingOnscreenToolbar.style.display = isRestricted ? 'flex' : 'none';
    }

    var showWordCount = cfg.showWordCount !== false && cfg.wordCount !== false;
    var writingCounts = document.getElementById('writing-counts');
    if (writingCounts) {
      writingCounts.style.display = showWordCount ? 'flex' : 'none';
    }
  };
  sitting.updateWritingCounters = function (question) {
    if (!sitting.writingEditor || !question) return;
    var responseText = sitting.writingEditor.value || '';
    var words = sitting.countWords(responseText);
    if (sitting.liveWordCount) sitting.liveWordCount.textContent = words;
    if (sitting.wordCountDisplay) sitting.wordCountDisplay.textContent = words;
    if (sitting.characterCountDisplay) sitting.characterCountDisplay.textContent = responseText.length;

    var minWords = Number(question.minWords) || 0;
    var maxWords = Number(question.maxWords) || 0;

    if (sitting.wordRangeDisplay) {
      var minimum = minWords ? minWords + ' minimum' : '';
      var maximum = maxWords ? maxWords + ' maximum' : '';
      sitting.wordRangeDisplay.textContent = minimum && maximum
        ? ' (' + minimum + ', ' + maximum + ')'
        : minimum || maximum ? ' (' + (minimum || maximum) + ')' : '';
    }

    if (sitting.minWordsBadge) {
      if (minWords > 0) {
        sitting.minWordsBadge.style.display = 'inline-flex';
        sitting.minWordsBadge.textContent = 'Min: ' + minWords + ' words';
        if (words < minWords) {
          sitting.minWordsBadge.style.background = '#fef2f2';
          sitting.minWordsBadge.style.color = '#b31217';
          sitting.minWordsBadge.style.borderColor = '#b31217';
          if (sitting.underLengthWarning) sitting.underLengthWarning.style.display = 'inline-flex';
        } else {
          sitting.minWordsBadge.style.background = '#f0fdf4';
          sitting.minWordsBadge.style.color = '#0a6b3c';
          sitting.minWordsBadge.style.borderColor = '#0a6b3c';
          if (sitting.underLengthWarning) sitting.underLengthWarning.style.display = 'none';
        }
      } else {
        sitting.minWordsBadge.style.display = 'none';
        if (sitting.underLengthWarning) sitting.underLengthWarning.style.display = 'none';
      }
    }

    sitting.updateRequiredKeywordsMatch(question, responseText);
  };
  sitting.markDraftDirty = function (questionId) {
    var draft = sitting.responseDrafts[questionId];
    if (!draft) return;
    draft.text = sitting.writingEditor.value;
    draft.dirty = true;
    sitting.pushUndoState();
    sitting.updateWritingCounters(sitting.questionMap[sitting.currentDisplayOrder]);
    sitting.updateProgressStats();
    sitting.saveStatus.textContent = 'Draft not saved';
    sitting.saveStatus.className = 'save-status save-status--saving';
    if (sitting.draftSaveTimeout) clearTimeout(sitting.draftSaveTimeout);
    sitting.draftSaveTimeout = setTimeout(function () {
      sitting.saveDraft(questionId);
    }, 500);
  };
  sitting.saveDraft = function (questionId) {
    var draft = sitting.responseDrafts[questionId];
    if (!draft || !draft.dirty) return Promise.resolve(true);
    if (sitting.draftSaveInFlight[questionId]) return sitting.draftSaveInFlight[questionId];

    var savedText = draft.text;
    sitting.saveStatus.textContent = 'Saving draft…';
    sitting.saveStatus.className = 'save-status save-status--saving';
    var request = fetch('/exams/take/' + sitting.attemptId + '/draft', {
      method: 'POST',
      keepalive: true,
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': sitting.csrfToken
      },
      body: JSON.stringify({
        questionId: questionId,
        responseText: savedText
      })
    })
    .then(function (response) {
      if (!response.ok) {
        return response.json().catch(function () { return {}; }).then(function (error) {
          throw new Error(error.message || error.detail || 'Draft save failed with status ' + response.status);
        });
      }
      return response.json();
    })
    .then(function (result) {
      draft.dirty = draft.text !== savedText;
      var savedAt = result.savedAt ? new Date(result.savedAt) : new Date();
      sitting.saveStatus.textContent = 'Draft saved at ' + savedAt.toLocaleTimeString([], { hour12: false });
      sitting.saveStatus.className = 'save-status save-status--saved';
      return true;
    })
    .catch(function (error) {
      sitting.saveStatus.textContent = 'Draft save failed';
      sitting.saveStatus.className = 'save-status save-status--error';
      console.error('Failed to save exam draft:', error);
      return false;
    })
    .then(function (saved) {
      delete sitting.draftSaveInFlight[questionId];
      if (draft.dirty && saved) {
        setTimeout(function () { sitting.saveDraft(questionId); }, 0);
      }
      return saved;
    });

    sitting.draftSaveInFlight[questionId] = request;
    return request;
  };
  sitting.prepareFinalDraftFields = function (form) {
    var container = form.querySelector('.exam-draft-submit-fields');
    if (!container) return;
    container.innerHTML = '';

    var draftIndex = 0;
    sitting.questions.forEach(function (question) {
      if (question.questionType !== 'free_response') return;
      var draft = sitting.responseDrafts[question.questionId];
      if (!draft) return;

      var questionIdInput = document.createElement('input');
      questionIdInput.type = 'hidden';
      questionIdInput.name = 'Drafts[' + draftIndex + '].QuestionId';
      questionIdInput.value = question.questionId;

      var responseTextInput = document.createElement('input');
      responseTextInput.type = 'hidden';
      responseTextInput.name = 'Drafts[' + draftIndex + '].ResponseText';
      responseTextInput.value = draft.text;

      container.appendChild(questionIdInput);
      container.appendChild(responseTextInput);
      draftIndex++;
    });
  };

  sitting.initializeWriting = function () {
    sitting.internalClipboard = '';
    sitting.undoStack = [];
    sitting.redoStack = [];
    sitting.isTrackingUndo = true;
  };

  sitting.bindDraftControls = function () {
    if (sitting.writingEditor) {
      sitting.writingEditor.addEventListener('input', function () {
        var questionId = Number(sitting.writingEditor.getAttribute('data-question-id'));
        sitting.markDraftDirty(questionId);
      });
      sitting.writingEditor.addEventListener('blur', function () {
        var questionId = Number(sitting.writingEditor.getAttribute('data-question-id'));
        if (sitting.draftSaveTimeout) clearTimeout(sitting.draftSaveTimeout);
        sitting.saveDraft(questionId);
      });
    }

    if (sitting.saveDraftButton) {
      sitting.saveDraftButton.addEventListener('click', function () {
        var questionId = Number(sitting.writingEditor.getAttribute('data-question-id'));
        if (sitting.draftSaveTimeout) clearTimeout(sitting.draftSaveTimeout);
        sitting.saveDraft(questionId);
      });
    }
  };

  sitting.bindWritingToolbar = function () {
    if (sitting.writingEditor) {
      sitting.writingEditor.addEventListener('keydown', function (e) {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (sitting.isCutPasteRestricted(currentQ)) {
          if ((e.ctrlKey || e.metaKey) && (e.key === 'c' || e.key === 'C' || e.key === 'x' || e.key === 'X' || e.key === 'v' || e.key === 'V')) {
            e.preventDefault();
            e.stopPropagation();
            return false;
          }
        }
      });

      sitting.writingEditor.addEventListener('cut', function (e) {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (sitting.isCutPasteRestricted(currentQ)) {
          e.preventDefault();
          e.stopPropagation();
          return false;
        }
      });

      sitting.writingEditor.addEventListener('copy', function (e) {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (sitting.isCutPasteRestricted(currentQ)) {
          e.preventDefault();
          e.stopPropagation();
          return false;
        }
      });

      sitting.writingEditor.addEventListener('paste', function (e) {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (sitting.isCutPasteRestricted(currentQ)) {
          e.preventDefault();
          e.stopPropagation();
          return false;
        }
      });
    }

    if (sitting.btnWritingCut) {
      sitting.btnWritingCut.addEventListener('click', function () {
        if (!sitting.writingEditor) return;
        var start = sitting.writingEditor.selectionStart;
        var end = sitting.writingEditor.selectionEnd;
        if (start === end) return;
        sitting.pushUndoState();
        sitting.internalClipboard = sitting.writingEditor.value.substring(start, end);
        sitting.writingEditor.value = sitting.writingEditor.value.substring(0, start) + sitting.writingEditor.value.substring(end);
        sitting.writingEditor.selectionStart = sitting.writingEditor.selectionEnd = start;
        sitting.markDraftDirty(sitting.writingEditor.getAttribute('data-question-id'));
        sitting.writingEditor.focus();
      });
    }

    if (sitting.btnWritingPaste) {
      sitting.btnWritingPaste.addEventListener('click', function () {
        if (!sitting.writingEditor || !sitting.internalClipboard) return;
        var start = sitting.writingEditor.selectionStart;
        var end = sitting.writingEditor.selectionEnd;
        sitting.pushUndoState();
        sitting.writingEditor.value = sitting.writingEditor.value.substring(0, start) + sitting.internalClipboard + sitting.writingEditor.value.substring(end);
        sitting.writingEditor.selectionStart = sitting.writingEditor.selectionEnd = start + sitting.internalClipboard.length;
        sitting.markDraftDirty(sitting.writingEditor.getAttribute('data-question-id'));
        sitting.writingEditor.focus();
      });
    }

    if (sitting.btnWritingUndo) {
      sitting.btnWritingUndo.addEventListener('click', function () {
        if (!sitting.writingEditor || sitting.undoStack.length === 0) return;
        sitting.isTrackingUndo = false;
        sitting.redoStack.push(sitting.writingEditor.value);
        var prev = sitting.undoStack.pop();
        sitting.writingEditor.value = prev;
        sitting.isTrackingUndo = true;
        sitting.markDraftDirty(sitting.writingEditor.getAttribute('data-question-id'));
        sitting.writingEditor.focus();
      });
    }

    if (sitting.btnWritingRedo) {
      sitting.btnWritingRedo.addEventListener('click', function () {
        if (!sitting.writingEditor || sitting.redoStack.length === 0) return;
        sitting.isTrackingUndo = false;
        sitting.undoStack.push(sitting.writingEditor.value);
        var next = sitting.redoStack.pop();
        sitting.writingEditor.value = next;
        sitting.isTrackingUndo = true;
        sitting.markDraftDirty(sitting.writingEditor.getAttribute('data-question-id'));
        sitting.writingEditor.focus();
      });
    }
  };
})(window.ExamSitting);
