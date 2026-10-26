(function (sitting) {
  "use strict";

  // 11 Interactive Question Component Widgets
  sitting.renderComponentWidget = function (q) {
    if (!sitting.componentContainer) return;
    var isPacedAuto = sitting.isSystemPacedAuto(q);
    var pacerContainer = document.getElementById('speaking-pacer-container');
    var verificationScreen = document.getElementById('speaking-verification-screen');
    if (verificationScreen) verificationScreen.style.display = 'none';

    if (isPacedAuto) {
      if (sitting.optionList) sitting.optionList.style.display = 'none';
      if (sitting.writingPane) sitting.writingPane.style.display = 'none';
      if (sitting.freeResponseContainer) sitting.freeResponseContainer.style.display = 'none';
      if (sitting.btnPrev) sitting.btnPrev.style.display = 'none';
      if (sitting.btnNext) sitting.btnNext.style.display = 'none';
      if (pacerContainer) pacerContainer.style.display = 'block';
      if (q.responseAudioPath || (sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].responseAudioPath)) {
        sitting.transitionAfterSpeakingItem(q);
        return;
      }
      sitting.startSpeakingPacerStateMachine(q);
    } else {
      if (pacerContainer) pacerContainer.style.display = 'none';
      sitting.cleanupSpeakingPacer();
    }

            var isFreeResponse = (q.questionType || '').toLowerCase() === 'free_response';
    var isMcq = !q.questionType || q.questionType === 'mcq' || q.questionType === 'mcq_3';

    if (isFreeResponse) {
      sitting.optionList.style.display = 'none';
      sitting.componentContainer.style.display = 'none';
      sitting.componentContainer.innerHTML = '';
      return;
    }

    if (isMcq) {
      sitting.componentContainer.style.display = 'none';
      sitting.componentContainer.innerHTML = '';
      if (q.clusterSize === 3) {
        sitting.optionList.style.display = 'none';
        sitting.optionList.innerHTML = '';
      } else {
        sitting.optionList.style.display = '';
      }
      return;
    }

    sitting.optionList.style.display = 'none';
    sitting.componentContainer.style.display = 'block';

    var answer = sitting.localAnswers[q.questionId] || {};
    var type = (q.questionType || '').toLowerCase();

    if (type === 'multi_select') {
      var maxSel = q.maxSelections || 2;
      var selectedIds = [];
      try {
        if (answer.selectedOptionIdsJson) {
          selectedIds = JSON.parse(answer.selectedOptionIdsJson);
        } else if (q.selectedOptionIds) {
          selectedIds = q.selectedOptionIds.slice();
        }
      } catch (e) {
        selectedIds = [];
      }

      var html = '<div class="multi-select-widget stack" style="gap: var(--space-2);">';
      html += '<div class="alert alert--info py-1 px-2" style="font-size: 0.85rem; font-weight: 600; border-radius: 0; box-shadow: none;">';
      html += 'SELECT UP TO ' + maxSel + ' ANSWERS: Selected <span id="ms-count-' + q.questionId + '">' + selectedIds.length + '</span>/' + maxSel;
      html += '</div>';
      html += '<div class="option-list" role="group" aria-label="Multiple choice options for question ' + q.displayOrder + '">';

      (q.options || []).forEach(function (opt) {
        var isChecked = selectedIds.indexOf(opt.optionId) !== -1;
        var isDisabled = !isChecked && selectedIds.length >= maxSel;
        html += '<label class="option option--checkbox ' + (isChecked ? 'is-selected' : '') + '" style="' + (isDisabled ? 'opacity: 0.6; cursor: not-allowed;' : '') + '">';
        html += '<input type="checkbox" class="option__input js-option-checkbox" value="' + opt.optionId + '" ' + (isChecked ? 'checked' : '') + ' ' + (isDisabled ? 'disabled' : '') + '>';
        html += '<span class="option__marker">' + sitting.escapeHtml(opt.label) + '</span>';
        html += '<span class="option__text">' + sitting.escapeHtml(opt.text) + '</span>';
        html += '</label>';
      });

      html += '</div></div>';
      sitting.componentContainer.innerHTML = html;

      var checkboxes = sitting.componentContainer.querySelectorAll('.js-option-checkbox');
      checkboxes.forEach(function (cb) {
        cb.addEventListener('change', function () {
          var optId = parseInt(cb.value, 10);
          var idx = selectedIds.indexOf(optId);
          if (cb.checked) {
            if (idx === -1 && selectedIds.length < maxSel) {
              selectedIds.push(optId);
            } else if (idx === -1 && selectedIds.length >= maxSel) {
              cb.checked = false;
              return;
            }
          } else {
            if (idx !== -1) {
              selectedIds.splice(idx, 1);
            }
          }

          var countEl = document.getElementById('ms-count-' + q.questionId);
          if (countEl) countEl.textContent = selectedIds.length;

          checkboxes.forEach(function (otherCb) {
            var otherId = parseInt(otherCb.value, 10);
            var otherChecked = selectedIds.indexOf(otherId) !== -1;
            var parentLabel = otherCb.closest('.option');
            if (parentLabel) {
              parentLabel.classList.toggle('is-selected', otherChecked);
            }
            if (!otherChecked) {
              otherCb.disabled = selectedIds.length >= maxSel;
              if (parentLabel) parentLabel.style.opacity = selectedIds.length >= maxSel ? '0.6' : '';
              if (parentLabel) parentLabel.style.cursor = selectedIds.length >= maxSel ? 'not-allowed' : '';
            }
          });

          var json = JSON.stringify(selectedIds);
          sitting.saveAnswer(q.questionId, selectedIds[0] || null, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), null, json);
        });
      });
    } else if (type === 'inline_completion') {
      var maxWords = q.maxWords;
      var allowNumber = q.allowNumber !== false;
      var curText = answer.responseText || '';

      var html = '<div class="inline-completion-widget stack" style="gap: var(--space-2);">';
      html += '<div style="display: flex; gap: 0.5rem; flex-wrap: wrap;">';
      html += '<span class="badge badge--neutral" style="font-family: \'IBM Plex Mono\', monospace; border-radius: 0; box-shadow: none;">' + (maxWords ? 'MAX ' + maxWords + ' WORDS' : 'TEXT COMPLETION') + '</span>';
      html += '<span class="badge badge--neutral" style="font-family: \'IBM Plex Mono\', monospace; border-radius: 0; box-shadow: none;">' + (allowNumber ? 'LETTERS & NUMBERS' : 'LETTERS ONLY (NO NUMBERS)') + '</span>';
      html += '</div>';
      html += '<div class="mt-2">';
      html += '<input type="text" class="completion-input js-completion-input" value="' + sitting.escapeHtml(curText) + '" placeholder="Type your answer here..." autocomplete="off" spellcheck="false">';
      html += '<div class="text-xs text-muted mt-1">';
      html += 'Word count: <strong class="js-comp-word-count">' + (curText.trim() ? curText.trim().split(/\s+/).length : 0) + '</strong>' + (maxWords ? ' / ' + maxWords + ' max' : '') + '<span class="js-comp-warning ml-2" style="color: #b31217; display: none;"></span>';
      html += '</div></div></div>';
      sitting.componentContainer.innerHTML = html;

      var inputEl = sitting.componentContainer.querySelector('.js-completion-input');
      var wordCountEl = sitting.componentContainer.querySelector('.js-comp-word-count');
      var warningEl = sitting.componentContainer.querySelector('.js-comp-warning');

      inputEl.addEventListener('input', function () {
        var val = inputEl.value;
        if (!allowNumber && /\d/.test(val)) {
          val = val.replace(/\d/g, '');
          inputEl.value = val;
        }
        var words = val.trim() ? val.trim().split(/\s+/).length : 0;
        if (wordCountEl) wordCountEl.textContent = words;
        if (maxWords && words > maxWords) {
          if (warningEl) {
            warningEl.textContent = ' (Exceeds maximum word limit)';
            warningEl.style.display = 'inline';
          }
        } else {
          if (warningEl) warningEl.style.display = 'none';
        }
        sitting.saveAnswer(q.questionId, null, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), val, null);
      });
    } else if (type === 'dropdown') {
      var selectedOptId = answer.selectedOptionId;
      var html = '<div class="dropdown-widget stack" style="gap: var(--space-2);">';
      html += '<label class="text-sm text-bold">Select the correct option from the list below:</label>'
      html += '<select class="completion-input js-dropdown-select" style="max-width: 480px; background: #fff; cursor: pointer;">';
      html += '<option value="">-- Select Option --</option>';
      (q.options || []).forEach(function (opt) {
        var isSel = opt.optionId === selectedOptId;
        html += '<option value="' + opt.optionId + '" ' + (isSel ? 'selected' : '') + '>' + sitting.escapeHtml(opt.label) + '. ' + sitting.escapeHtml(opt.text) + '</option>';
      });
      html += '</select></div>';
      sitting.componentContainer.innerHTML = html;

      var selectEl = sitting.componentContainer.querySelector('.js-dropdown-select');
      selectEl.addEventListener('change', function () {
        var val = parseInt(selectEl.value, 10);
        var chosenId = !isNaN(val) ? val : null;
        sitting.saveAnswer(q.questionId, chosenId, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), null, null);
      });
    } else if (type === 'drag_drop') {
      var selectedOptId = answer.selectedOptionId;
      var placedOption = (q.options || []).find(function (o) { return o.OptionId === selectedOptId || o.optionId === selectedOptId; });

      var html = '<div class="drag-drop-widget stack" style="gap: var(--space-3);">';
      html += '<div>';
      html += '<span class="text-xs uppercase text-muted" style="font-family: \'IBM Plex Sans Condensed\', sans-serif; font-weight: 700; letter-spacing: 0.05em; display: block; margin-bottom: 0.5rem;">WORD BANK (Click or drag to place):</span>';
      html += '<div class="word-bank-chips" style="display: flex; flex-wrap: wrap; gap: 0.5rem; border: 2px dashed #14181d; padding: 0.75rem; background: #fbfcfd;">';
      (q.options || []).forEach(function (opt) {
        var isPlaced = placedOption && (placedOption.optionId === opt.optionId || placedOption.OptionId === opt.optionId);
        html += '<button type="button" class="chip-item js-drag-chip ' + (isPlaced ? 'is-placed' : '') + '" draggable="true" data-option-id="' + opt.optionId + '" data-label="' + sitting.escapeHtml(opt.label) + '" data-text="' + sitting.escapeHtml(opt.text) + '">';
        html += '<strong style="font-family: \'IBM Plex Mono\', monospace; margin-right: 0.35rem;">' + sitting.escapeHtml(opt.label) + '</strong>' + sitting.escapeHtml(opt.text);
        html += '</button>';
      });
      html += '</div></div>';

      html += '<div>';
      html += '<span class="text-xs uppercase text-muted" style="font-family: \'IBM Plex Sans Condensed\', sans-serif; font-weight: 700; letter-spacing: 0.05em; display: block; margin-bottom: 0.5rem;">DROP ZONE:</span>';
      html += '<div class="drop-zone js-drop-zone">';
      if (placedOption) {
        html += '<div class="placed-chip-content" style="display: flex; align-items: center; gap: 0.5rem; font-weight: 700;">';
        html += '<span class="badge badge--info" style="border-radius: 0; box-shadow: none;">' + sitting.escapeHtml(placedOption.label) + '</span>';
        html += '<span>' + sitting.escapeHtml(placedOption.text) + '</span>';
        html += '</div>';
        html += '<button type="button" class="btn btn--ghost btn--sm js-remove-placed" style="color: #b31217; font-weight: 700;">[X] Remove</button>';
      } else {
        html += '<span class="text-muted text-sm js-drop-placeholder">Drag word here or click word above</span>';
      }
      html += '</div></div></div>';
      sitting.componentContainer.innerHTML = html;

      var chips = sitting.componentContainer.querySelectorAll('.js-drag-chip');
      var dropZone = sitting.componentContainer.querySelector('.js-drop-zone');
      var removeBtn = sitting.componentContainer.querySelector('.js-remove-placed');

      function placeChip(optId) {
        sitting.saveAnswer(q.questionId, optId, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), null, null);
        sitting.renderComponentWidget(q);
      }

      function clearChip() {
        sitting.saveAnswer(q.questionId, null, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), null, null);
        sitting.renderComponentWidget(q);
      }

      chips.forEach(function (chip) {
        chip.addEventListener('click', function () {
          if (chip.classList.contains('is-placed')) return;
          var optId = parseInt(chip.getAttribute('data-option-id'), 10);
          placeChip(optId);
        });
        chip.addEventListener('dragstart', function (e) {
          e.dataTransfer.setData('text/plain', chip.getAttribute('data-option-id'));
        });
      });

      if (dropZone) {
        dropZone.addEventListener('dragover', function (e) {
          e.preventDefault();
          dropZone.classList.add('is-hovered');
        });
        dropZone.addEventListener('dragleave', function () {
          dropZone.classList.remove('is-hovered');
        });
        dropZone.addEventListener('drop', function (e) {
          e.preventDefault();
          dropZone.classList.remove('is-hovered');
          var optId = parseInt(e.dataTransfer.getData('text/plain'), 10);
          if (!isNaN(optId)) {
            placeChip(optId);
          }
        });
      }

      if (removeBtn) {
        removeBtn.addEventListener('click', function () {
          clearChip();
        });
      }
    }
  };
})(window.ExamSitting);
