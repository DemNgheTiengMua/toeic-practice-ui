(function (sitting) {
  "use strict";

  sitting.parseToolsConfig = function (jsonStr) {
    if (!jsonStr) return {};
    try {
      return JSON.parse(jsonStr);
    } catch (e) {
      return {};
    }
  };
  sitting.renderTokenizedText = function (container, rawText, sectionId, questionId, targetType) {
    if (!container) return;
    if (!rawText) {
      container.innerHTML = '';
      return;
    }
    var words = rawText.split(/\s+/).filter(Boolean);
    var html = '';
    words.forEach(function (word, idx) {
      html += '<span class="ee-token" data-token-idx="' + idx + '" data-section-id="' + sectionId + '" data-question-id="' + questionId + '" data-target="' + targetType + '">' + sitting.escapeHtml(word) + '</span> ';
    });
    container.innerHTML = html;
    sitting.applyHighlightsForTarget(container, sectionId, targetType);
  };
  sitting.applyHighlightsForTarget = function (container, sectionId, targetType) {
    if (!container) return;
    var tokens = container.querySelectorAll('.ee-token');
    tokens.forEach(function (tok) {
      tok.classList.remove('ee-token--highlighted');
      var existingPin = tok.querySelector('.ee-note-pin');
      if (existingPin) existingPin.remove();
    });

    sitting.candidateNotes.forEach(function (note) {
      if (note.target === targetType && (String(note.sectionId) === String(sectionId) || !note.sectionId)) {
        for (var i = note.startToken; i <= note.endToken; i++) {
          var tokenEl = container.querySelector('.ee-token[data-token-idx="' + i + '"]');
          if (tokenEl) {
            tokenEl.classList.add('ee-token--highlighted');
            if (i === note.endToken && note.note && note.note.trim()) {
              var pin = document.createElement('span');
              pin.className = 'ee-note-pin';
              pin.textContent = '[NOTE]';
              pin.title = note.note;
              (function (savedNote) {
                pin.addEventListener('click', function (e) {
                  e.stopPropagation();
                  alert('Candidate Note: ' + savedNote);
                });
              })(note.note);
              tokenEl.appendChild(pin);
            }
          }
        }
      }
    });
  };
  sitting.applyAllHighlights = function () {
    var curQ = sitting.questionMap[sitting.currentDisplayOrder];
    if (!curQ) return;
    var splitPassage = document.getElementById('split-passage-text');
    var standardPassage = document.getElementById('passage-text');
    if (splitPassage && splitPassage.offsetParent !== null) {
      sitting.applyHighlightsForTarget(splitPassage, curQ.sectionId, 'passage');
    } else if (standardPassage) {
      sitting.applyHighlightsForTarget(standardPassage, curQ.sectionId, 'passage');
    }
    if (sitting.questionStemDisplay) {
      sitting.applyHighlightsForTarget(sitting.questionStemDisplay, curQ.sectionId, 'stem');
    }
  };
  sitting.addHighlight = function (meta, noteText) {
    var hl = {
      id: 'hl_' + Date.now() + '_' + Math.random().toString(36).substr(2, 4),
      sectionId: meta.sectionId,
      questionId: meta.questionId,
      target: meta.target,
      startToken: meta.startToken,
      endToken: meta.endToken,
      text: meta.text,
      note: noteText || ''
    };
    sitting.candidateNotes.push(hl);
    sitting.candidateNotesJson = JSON.stringify(sitting.candidateNotes);
    sitting.notesDirty = true;
    sitting.applyAllHighlights();
    sitting.scheduleSync(false);
  };
  sitting.removeHighlight = function (meta) {
    sitting.candidateNotes = sitting.candidateNotes.filter(function (h) {
      var sameTarget = h.target === meta.target && String(h.sectionId) === String(meta.sectionId);
      var overlaps = !(h.endToken < meta.startToken || h.startToken > meta.endToken);
      return !(sameTarget && overlaps);
    });
    sitting.candidateNotesJson = JSON.stringify(sitting.candidateNotes);
    sitting.notesDirty = true;
    sitting.applyAllHighlights();
    sitting.scheduleSync(false);
  };
  sitting.handleHighlighterSelection = function (e) {
    var sel = window.getSelection();
    if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
      if (sitting.highlighterPopover && !sitting.highlighterPopover.contains(e.target)) {
        sitting.highlighterPopover.style.display = 'none';
      }
      return;
    }

    var curQ = sitting.questionMap[sitting.currentDisplayOrder];
    var cfg = sitting.parseToolsConfig(curQ ? curQ.toolsConfigJson : null);
    var allowHl = cfg.allowHighlight === true || cfg.highlighter === true;
    var allowNotes = cfg.allowNotes === true || cfg.notes === true;
    if (!allowHl && !allowNotes) {
      if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
      return;
    }

    var range = sel.getRangeAt(0);
    var container = range.commonAncestorContainer;
    if (container.nodeType === 3) container = container.parentElement;
    var tokenizedContainer = container.closest('.ee-tokenized-text');
    if (!tokenizedContainer) {
      if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
      return;
    }

    var btnHl = sitting.highlighterPopover ? sitting.highlighterPopover.querySelector('[data-action="highlight"]') : null;
    var btnNote = sitting.highlighterPopover ? sitting.highlighterPopover.querySelector('[data-action="note"]') : null;
    if (btnHl) btnHl.style.display = allowHl ? '' : 'none';
    if (btnNote) btnNote.style.display = allowNotes ? '' : 'none';

    var tokensInContainer = Array.from(tokenizedContainer.querySelectorAll('.ee-token'));
    var selectedTokens = tokensInContainer.filter(function (tok) {
      return sel.containsNode(tok, true);
    });

    if (selectedTokens.length === 0) {
      if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
      return;
    }

    var startIdx = parseInt(selectedTokens[0].getAttribute('data-token-idx'), 10);
    var endIdx = parseInt(selectedTokens[selectedTokens.length - 1].getAttribute('data-token-idx'), 10);
    var secId = selectedTokens[0].getAttribute('data-section-id');
    var qId = selectedTokens[0].getAttribute('data-question-id');
    var target = selectedTokens[0].getAttribute('data-target');

    sitting.currentSelectionMeta = {
      sectionId: secId,
      questionId: qId,
      target: target,
      startToken: Math.min(startIdx, endIdx),
      endToken: Math.max(startIdx, endIdx),
      text: sel.toString().trim()
    };

    var rect = range.getBoundingClientRect();
    if (sitting.highlighterPopover) {
      sitting.highlighterPopover.style.left = (rect.left + window.scrollX) + 'px';
      sitting.highlighterPopover.style.top = (rect.top + window.scrollY - 40) + 'px';
      sitting.highlighterPopover.style.display = 'flex';
    }
  };

  sitting.initializeAnnotations = function () {
    // Tokenized Text Highlighter & Notes Engine
    sitting.candidateNotes = [];
    try {
      sitting.candidateNotes = JSON.parse(sitting.candidateNotesJson || '[]');
      if (!Array.isArray(sitting.candidateNotes)) sitting.candidateNotes = [];
    } catch (e) {
      sitting.candidateNotes = [];
    }

    sitting.currentSelectionMeta = null;
    sitting.highlighterPopover = document.getElementById('highlighter-popover');
    sitting.stickyNoteModal = document.getElementById('sticky-note-modal');
    sitting.stickyNoteInput = document.getElementById('sticky-note-input');
    sitting.stickyNoteQuote = document.getElementById('sticky-note-quote');
    sitting.btnSaveNote = document.getElementById('btn-save-note');
    sitting.btnCancelNote = document.getElementById('btn-cancel-note');
    sitting.componentContainer = document.getElementById('component-widget-container');
  };

  sitting.bindAnnotationControls = function () {
    document.addEventListener('mouseup', sitting.handleHighlighterSelection);
    document.addEventListener('contextmenu', function (e) {
      var sel = window.getSelection();
      if (sel && !sel.isCollapsed && sel.rangeCount > 0) {
        var range = sel.getRangeAt(0);
        var container = range.commonAncestorContainer;
        if (container.nodeType === 3) container = container.parentElement;
        if (container && container.closest('.ee-tokenized-text')) {
          var curQ = sitting.questionMap[sitting.currentDisplayOrder];
          var cfg = sitting.parseToolsConfig(curQ ? curQ.toolsConfigJson : null);
          if (cfg.allowHighlight === true || cfg.highlighter === true || cfg.allowNotes === true || cfg.notes === true) {
            e.preventDefault();
            sitting.handleHighlighterSelection(e);
          }
        }
      }
    });

    document.querySelectorAll('.js-hl-action').forEach(function (btn) {
      btn.addEventListener('click', function () {
        if (!sitting.currentSelectionMeta) return;
        var action = btn.getAttribute('data-action');
        if (action === 'highlight') {
          sitting.addHighlight(sitting.currentSelectionMeta, '');
          if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
          window.getSelection()?.removeAllRanges();
        } else if (action === 'note') {
          if (sitting.stickyNoteModal && sitting.stickyNoteQuote && sitting.stickyNoteInput) {
            sitting.stickyNoteQuote.textContent = '"' + sitting.currentSelectionMeta.text + '"';
            sitting.stickyNoteInput.value = '';
            sitting.stickyNoteModal.style.display = 'flex';
            sitting.stickyNoteInput.focus();
          }
          if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
        } else if (action === 'clear') {
          sitting.removeHighlight(sitting.currentSelectionMeta);
          if (sitting.highlighterPopover) sitting.highlighterPopover.style.display = 'none';
          window.getSelection()?.removeAllRanges();
        }
      });
    });

    if (sitting.btnSaveNote) {
      sitting.btnSaveNote.addEventListener('click', function () {
        if (sitting.currentSelectionMeta && sitting.stickyNoteInput) {
          var noteText = sitting.stickyNoteInput.value.trim();
          sitting.addHighlight(sitting.currentSelectionMeta, noteText);
          sitting.stickyNoteModal.style.display = 'none';
          window.getSelection()?.removeAllRanges();
        }
      });
    }
    if (sitting.btnCancelNote) {
      sitting.btnCancelNote.addEventListener('click', function () {
        sitting.stickyNoteModal.style.display = 'none';
      });
    }
  };
})(window.ExamSitting);
