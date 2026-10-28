(function (sitting) {
  "use strict";
  sitting.updateNavigationForPhase = function () {
    var phase = sitting.phases[sitting.activePhaseIndex];
    var isListening = phase && phase.skillCode === 'LIST';
    var isReading = phase && phase.skillCode === 'READ';
    var currentQuestion = sitting.questionMap[sitting.currentDisplayOrder];
    var currentPhaseQuestions = phase ? phase.questions : [];
    var currentPhaseQuestionIndex = currentPhaseQuestions.findIndex(function (question) {
      return question.displayOrder === sitting.currentDisplayOrder;
    });
    var isAudioDriven = currentQuestion && (currentQuestion.navigationMode === 'AUDIO_DRIVEN');
    var isUnitLockedSandbox = currentQuestion && (currentQuestion.navigationMode === 'UNIT_LOCKED_SANDBOX');

    document.querySelectorAll('#navigator-grid .navigator__cell[data-question-id]').forEach(function (cell) {
      var question = sitting.questions.find(function (item) {
        return item.questionId === Number(cell.getAttribute('data-question-id'));
      });
      var samePhase = question && phase && question.skillCode === phase.skillCode;
      var answer = question ? sitting.localAnswers[question.questionId] : null;
      var isUnitLocked = isUnitLockedSandbox && question && (question.unitNumber || 1) !== sitting.activeUnitNumber;
      var isFutureUnit = isUnitLockedSandbox && question && (question.unitNumber || 1) > sitting.activeUnitNumber;
      var isBackInAudio = isAudioDriven && question && question.displayOrder < sitting.currentDisplayOrder;

      cell.hidden = !!(sitting.flaggedOnly && (!answer || !answer.isFlagged)) || isFutureUnit;
      cell.disabled = !samePhase || (isListening && question.displayOrder !== sitting.currentDisplayOrder) || isBackInAudio || isUnitLocked;
      if (isUnitLocked) {
        cell.classList.add('is-locked');
      } else {
        cell.classList.remove('is-locked');
      }
    });

    // Update bottom palette chips
    document.querySelectorAll('#bottom-palette-chips .palette-chip').forEach(function (chip) {
      var order = Number(chip.getAttribute('data-display-order'));
      var question = sitting.questionMap[order];
      var samePhase = question && phase && question.skillCode === phase.skillCode;
      var isUnitLocked = isUnitLockedSandbox && question && (question.unitNumber || 1) !== sitting.activeUnitNumber;
      var isFutureUnit = isUnitLockedSandbox && question && (question.unitNumber || 1) > sitting.activeUnitNumber;
      var isBackInAudio = isAudioDriven && order < sitting.currentDisplayOrder;

      chip.hidden = isFutureUnit;
      chip.disabled = !samePhase || isBackInAudio || isUnitLocked;
      if (isBackInAudio || isUnitLocked) {
        chip.classList.add('palette-chip--disabled');
      } else {
        chip.classList.remove('palette-chip--disabled');
      }
      // A chip that cannot be entered must read as locked even when the reason is the phase
      // boundary rather than a future adaptive unit (ticket d05).
      var isGated = chip.disabled || isFutureUnit;
      chip.classList.toggle('palette-chip--locked', isGated);
      chip.setAttribute('aria-disabled', isGated ? 'true' : 'false');
    });

    if (sitting.flaggedFilterButton) {
      sitting.flaggedFilterButton.disabled = !isReading && !isUnitLockedSandbox;
      sitting.flaggedFilterButton.textContent = sitting.flaggedOnly ? 'Show all questions' : 'Show flagged';
      sitting.flaggedFilterButton.setAttribute('aria-pressed', sitting.flaggedOnly ? 'true' : 'false');
    }

    if (sitting.btnOpenReviewScreen) {
      sitting.btnOpenReviewScreen.style.display = (isReading || isUnitLockedSandbox) ? 'inline-block' : 'none';
    }

    if (sitting.btnPrev) {
      var isOneWay = isAudioDriven || isListening;
      var prevQ = currentPhaseQuestionIndex > 0 ? currentPhaseQuestions[currentPhaseQuestionIndex - 1] : null;
      var isGroupLockedPrev = currentQuestion && currentQuestion.navigationMode === 'HYBRID_GROUP_LOCKED' && prevQ && prevQ.groupId !== currentQuestion.groupId;
      sitting.btnPrev.disabled = !phase
        || isOneWay
        || isGroupLockedPrev
        || currentPhaseQuestionIndex <= 0
        || sitting.getQuestionPhaseIndex(currentQuestion) !== sitting.activePhaseIndex;
      if (isAudioDriven || sitting.isSystemPacedAuto(currentQuestion)) {
        sitting.btnPrev.style.display = 'none';
      } else {
        sitting.btnPrev.style.display = '';
      }
    }

    if (sitting.btnNext && currentQuestion && sitting.isSystemPacedAuto(currentQuestion)) {
      sitting.btnNext.style.display = 'none';
    } else if (sitting.btnNext && phase && currentQuestion) {
      // Restore the button: a system-paced question used to hide it and no branch ever brought it
      // back, which left the Speaking → Writing path without a Next control (ticket d03).
      sitting.btnNext.style.display = '';
      var lastInPhase = currentPhaseQuestionIndex === currentPhaseQuestions.length - 1;
      var isFinalQuestion = sitting.currentDisplayOrder === sitting.totalQuestions;

      // Check if on final question of Unit 1 in UNIT_LOCKED_SANDBOX
      var unit1Questions = currentPhaseQuestions.filter(function (q) { return (q.unitNumber || 1) === 1; });
      var isLastInUnit1 = isUnitLockedSandbox && sitting.activeUnitNumber === 1 && unit1Questions.length > 0 &&
        currentQuestion.questionId === unit1Questions[unit1Questions.length - 1].questionId;

      if (isLastInUnit1) {
        sitting.btnNext.textContent = 'Finish Stage 1 & Proceed to Stage 2';
        sitting.btnNext.className = 'btn btn--primary js-open-unit-lock-modal';
        sitting.btnNext.disabled = false;
      } else if (isFinalQuestion) {
        sitting.btnNext.textContent = 'Review & Submit exam';
        sitting.btnNext.className = 'btn btn--danger js-open-submit-modal';
        sitting.btnNext.disabled = false;
      } else if (lastInPhase) {
        // The candidate finished the skill's last question: offer an explicit advance instead of
        // idling until the clock expires (ticket d02). The transition endpoint is the only mutator.
        var nextPhase = sitting.phases[sitting.activePhaseIndex + 1];
        if (nextPhase) {
          sitting.btnNext.textContent = 'Finish ' + (phase.skillName || 'this skill') + ' & start ' + (nextPhase.skillName || 'next skill');
          sitting.btnNext.className = 'btn btn--primary js-open-skill-advance';
        } else {
          // No next skill to advance to. This must submit — never a control that says "Review"
          // while actually transitioning the attempt to grading.
          sitting.btnNext.textContent = 'Review & Submit exam';
          sitting.btnNext.className = 'btn btn--danger js-open-submit-modal';
        }
        sitting.btnNext.disabled = false;
      } else {
        sitting.btnNext.textContent = 'Next question';
        sitting.btnNext.className = 'btn btn--primary';
        sitting.btnNext.disabled = false;
      }
    }
  };
  // 3. In-Memory 0ms Question Swapping (Exam Shell)
  sitting.renderQuestion = function (displayOrder) {
    var q = sitting.questionMap[displayOrder];
    if (!q) return;

    sitting.currentDisplayOrder = displayOrder;
    if (sitting.questionNumberDisplay) sitting.questionNumberDisplay.textContent = 'Question ' + q.displayOrder;

    // Topbar badge & Document title
    if (sitting.topbarBadge) sitting.topbarBadge.textContent = (q.skillName || '') + ' · ' + (q.sectionName || '');
    document.title = sitting.sittingData.examName + ' — Question ' + q.displayOrder + ' — extra efficient';

    // URL synchronization without history pollution
    window.history.replaceState({ page: 'exam-shell', q: displayOrder }, '', '/exams/take/' + sitting.attemptId + '?q=' + displayOrder);

    // Audio continuity & isolation
    var newAudioKey = sitting.getQuestionAudioKey(q);

    if (newAudioKey !== sitting.currentAudioKey) {
      if (sitting.currentAudioKey !== null) {
        var stoppedList = null;
        if (window.stopExamAudio) {
          stoppedList = window.stopExamAudio(sitting.audioWrapper);
        }
        var stoppedTime = (stoppedList && stoppedList[0]) ? stoppedList[0].currentTime : 0;
        if (stoppedTime > 0) {
          sitting.recordAudioProgress(sitting.currentAudioKey, { currentTime: stoppedTime });
        }
      }

      if (newAudioKey) {
        sitting.audioWrapper.style.display = 'block';
        var progress = sitting.getAudioProgress(newAudioKey);
        sitting.audioWrapper.innerHTML = window.createExamAudioHtml({
          mode: 'exam',
          audioPath: q.audioPath,
          startMs: q.audioStartMs,
          endMs: q.audioEndMs,
          playerId: 'main-exam-audio',
          completed: progress.completed,
          currentTime: progress.currentTime
        });
        if (window.initExamAudio) window.initExamAudio();
      } else {
        sitting.audioWrapper.style.display = 'none';
        sitting.audioWrapper.innerHTML = '';
      }
      sitting.currentAudioKey = newAudioKey;
    } else if (newAudioKey) {
      sitting.audioWrapper.style.display = 'block';
    }

    // Passage Pane & Split-Screen Viewport
    var isSplitMode = (q.layoutType === 'SPLIT_HORIZONTAL' || q.requirePassage) && q.passage && q.passage.trim().length > 0;
    var sittingContainer = document.querySelector('.exam-sitting-container');
    var examLayout = document.getElementById('exam-main-layout');
    var splitStimulusPane = document.getElementById('split-stimulus-pane');
    var splitPassageText = document.getElementById('split-passage-text');

    if (isSplitMode) {
      if (splitStimulusPane) splitStimulusPane.style.display = 'block';
      if (examLayout) examLayout.classList.add('exam-layout--split');
      if (sittingContainer) {
        sittingContainer.style.maxWidth = '100%';
        sittingContainer.style.padding = '0 1.5rem';
      }
      if (splitPassageText) {
        sitting.renderTokenizedText(splitPassageText, q.passage, q.sectionId, q.questionId, 'passage');
      }
      if (sitting.passageCard) sitting.passageCard.style.display = 'none';
    } else {
      if (splitStimulusPane) splitStimulusPane.style.display = 'none';
      if (examLayout) examLayout.classList.remove('exam-layout--split');
      if (sittingContainer) {
        sittingContainer.style.maxWidth = '1200px';
        sittingContainer.style.padding = '';
      }
      if (q.passage && q.passage.trim().length > 0) {
        sitting.passageText.textContent = q.passage;
        sitting.passageCard.style.display = 'block';
      } else {
        sitting.passageCard.style.display = 'none';
      }
    }

    // Image Pane
    if (q.imagePath && q.imagePath.trim().length > 0) {
      sitting.questionImage.src = q.imagePath;
      sitting.questionImage.alt = q.imageAlt || 'Question illustration';
      sitting.imageContainer.style.display = 'block';
    } else {
      sitting.imageContainer.style.display = 'none';
    }

    var isFreeResponse = (q.questionType || '').toLowerCase() === 'free_response';
    var isCluster = q.clusterSize === 3;
    var hideOptText = !!q.hideOptionText;
    var optCount = q.optionCount || 4;

    // Image Prominence for RequireImage
    if (q.requireImage) {
      sitting.imageContainer.style.border = '2px solid #14181d';
      sitting.imageContainer.style.background = '#fff';
      sitting.imageContainer.style.padding = '0.5rem';
      sitting.imageContainer.style.textAlign = 'center';
    }

    // Clustered Questions Card Handling (ClusterSize == 3)
    if (isCluster) {
      if (sitting.questionMetaBar) sitting.questionMetaBar.style.display = 'none';
      sitting.questionStemDisplay.style.display = 'none';
      sitting.optionList.style.display = 'none';
      sitting.optionList.innerHTML = '';
      if (sitting.clusterContainer) sitting.clusterContainer.style.display = 'flex';

      var clusterQuestions = sitting.questions.filter(function (item) {
        return (q.groupId && item.groupId === q.groupId) ||
          (item.sectionId === q.sectionId && Math.floor((item.displayOrder - 1) / 3) === Math.floor((q.displayOrder - 1) / 3));
      });
      if (clusterQuestions.length === 0) clusterQuestions = [q];

      var clusterHtml = '';
      clusterQuestions.forEach(function (cq) {
        var cAns = sitting.localAnswers[cq.questionId];
        var cSelOptId = cAns ? cAns.selectedOptionId : cq.selectedOptionId;
        var cIsFlagged = !!(cAns ? cAns.isFlagged : cq.isFlagged);
        clusterHtml += '<div class="cluster-item" data-question-id="' + cq.questionId + '">';
        clusterHtml += '<div class="row row--between mb-1" style="align-items: center;">';
        clusterHtml += '<span class="question__number">Question ' + cq.displayOrder + '</span>';
        clusterHtml += '<button type="button" class="btn btn--ghost btn--sm js-cluster-flag-btn ' + (cIsFlagged ? 'btn--primary' : '') + '" data-question-id="' + cq.questionId + '" data-flagged="' + (cIsFlagged ? 'true' : 'false') + '">';
        clusterHtml += (cIsFlagged ? 'Flagged for review' : 'Flag for review') + '</button>';
        clusterHtml += '</div>';
        clusterHtml += '<p class="question__stem" data-cluster-stem="' + cq.questionId + '"></p>';
        clusterHtml += '<div class="option-list" role="radiogroup" aria-label="Answer for question ' + cq.displayOrder + '">';
        (cq.options || []).forEach(function (opt) {
          var isSelected = opt.optionId === cSelOptId;
          clusterHtml += '<label class="option ' + (isSelected ? 'is-selected' : '') + '">';
          clusterHtml += '<input class="option__input js-cluster-radio" type="radio" name="cluster_opt_' + cq.questionId + '" value="' + opt.optionId + '" data-question-id="' + cq.questionId + '" ' + (isSelected ? 'checked' : '') + '>';
          clusterHtml += '<span class="option__marker">' + sitting.escapeHtml(opt.label) + '</span>';
          clusterHtml += '<span class="option__text">' + sitting.escapeHtml(opt.text) + '</span>';
          clusterHtml += '</label>';
        });
        clusterHtml += '</div></div>';
      });

      if (sitting.clusterContainer) {
        sitting.clusterContainer.innerHTML = clusterHtml;
        // Cluster stems are tokenized after injection so they carry real .ee-token spans,
        // exactly like the split passage (ticket d04).
        sitting.clusterContainer.querySelectorAll('[data-cluster-stem]').forEach(function (stemEl) {
          var clusterQuestion = sitting.questions.find(function (item) {
            return item.questionId === Number(stemEl.getAttribute('data-cluster-stem'));
          });
          if (clusterQuestion) {
            sitting.renderTokenizedText(stemEl, clusterQuestion.stem, clusterQuestion.sectionId, clusterQuestion.questionId, 'stem');
          }
        });
        sitting.clusterContainer.querySelectorAll('.js-cluster-radio').forEach(function (radio) {
          radio.addEventListener('change', function () {
            var qId = Number(this.getAttribute('data-question-id'));
            var val = parseInt(this.value, 10);
            var parentCluster = this.closest('.cluster-item');
            if (parentCluster) {
              parentCluster.querySelectorAll('.option').forEach(function (lbl) { lbl.classList.remove('is-selected'); });
            }
            var parentLabel = this.closest('.option');
            if (parentLabel) parentLabel.classList.add('is-selected');
            var isFl = !!(sitting.localAnswers[qId] && sitting.localAnswers[qId].isFlagged);
            sitting.saveAnswer(qId, val, isFl);
          });
        });
        sitting.clusterContainer.querySelectorAll('.js-cluster-flag-btn').forEach(function (fBtn) {
          fBtn.addEventListener('click', function () {
            var qId = Number(this.getAttribute('data-question-id'));
            var isFl = this.getAttribute('data-flagged') === 'true';
            var newFl = !isFl;
            this.setAttribute('data-flagged', newFl ? 'true' : 'false');
            this.classList.toggle('btn--primary', newFl);
            this.textContent = newFl ? 'Flagged for review' : 'Flag for review';
            var curAns = sitting.localAnswers[qId];
            sitting.saveAnswer(qId, curAns ? curAns.selectedOptionId : null, newFl);
          });
        });
      }
    } else {
      if (sitting.questionMetaBar) sitting.questionMetaBar.style.display = '';
      if (sitting.clusterContainer) {
        sitting.clusterContainer.style.display = 'none';
        sitting.clusterContainer.innerHTML = '';
      }

      // Question Meta & Stem
      sitting.questionNumberDisplay.textContent = 'Question ' + q.displayOrder;
      sitting.questionSectionDisplay.textContent = q.sectionName;
      if (hideOptText && optCount === 3) {
        sitting.questionStemDisplay.textContent = '';
        sitting.questionStemDisplay.style.display = 'none';
      } else {
        // Tokenize the stem through the same helper the split passage uses, so the highlighter
        // and sticky-note tools can address words here too (ticket d04).
        sitting.renderTokenizedText(sitting.questionStemDisplay, q.stem, q.sectionId, q.questionId, 'stem');
        sitting.questionStemDisplay.style.display = '';
      }

      // Flag State
      var isFlagged = !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged);
      sitting.flagBtn.parentElement.style.display = isFreeResponse ? 'none' : '';
      sitting.flagBtn.setAttribute('data-flagged', isFlagged ? 'true' : 'false');
      sitting.flagBtn.setAttribute('aria-pressed', isFlagged ? 'true' : 'false');
      sitting.flagBtn.textContent = isFlagged ? 'Flagged for review' : 'Flag for review';
      if (isFlagged) {
        sitting.flagBtn.classList.add('btn--primary');
      } else {
        sitting.flagBtn.classList.remove('btn--primary');
      }

      // Options List (Instant render, masked options if configured)
      var selectedOptId = sitting.localAnswers[q.questionId] ? sitting.localAnswers[q.questionId].selectedOptionId : null;
      sitting.optionList.setAttribute('aria-label', 'Answer for question ' + q.displayOrder);
      sitting.optionList.className = 'option-list' + (hideOptText ? ' option-list--masked' : '');
      var optsToRender = q.options || [];
      if (hideOptText && optCount > 0) {
        optsToRender = optsToRender.slice(0, optCount);
      }
      var optionsHtml = '';
      optsToRender.forEach(function (opt) {
        var isSelected = opt.optionId === selectedOptId;
        optionsHtml += '<label class="option ' + (isSelected ? 'is-selected ' : '') + (hideOptText ? 'option--masked ' : '') + '">';
        optionsHtml += '<input class="option__input js-option-radio" type="radio" name="selectedOptionId" value="' + opt.optionId + '" ' + (isSelected ? 'checked' : '') + '>';
        optionsHtml += '<span class="option__marker">' + sitting.escapeHtml(opt.label) + '</span>';
        if (!hideOptText) {
          optionsHtml += '<span class="option__text">' + sitting.escapeHtml(opt.text) + '</span>';
        }
        optionsHtml += '</label>';
      });
      sitting.optionList.innerHTML = optionsHtml;
      sitting.optionList.style.display = isFreeResponse ? 'none' : '';
    }
    sitting.writingPane.style.display = isFreeResponse ? 'flex' : 'none';
    if (isFreeResponse) {
      var draft = sitting.responseDrafts[q.questionId];
      sitting.writingEditor.value = draft ? draft.text : '';
      sitting.writingEditor.setAttribute('data-question-id', q.questionId);
      sitting.undoStack = [sitting.writingEditor.value];
      sitting.redoStack = [];
      sitting.updateWritingEditorTools(q);
      sitting.renderRequiredKeywords(q);
      sitting.updateWritingCounters(q);
    } else {
      sitting.renderRequiredKeywords(q);
    }

    // Rebind radio selection listeners
    var radios = sitting.optionList.querySelectorAll('.js-option-radio');
    radios.forEach(function (radio) {
      radio.addEventListener('change', function () {
        var val = parseInt(this.value, 10);
        sitting.optionList.querySelectorAll('.option').forEach(function (lbl) { lbl.classList.remove('is-selected'); });
        var parentLabel = radio.closest('.option');
        if (parentLabel) parentLabel.classList.add('is-selected');
        sitting.saveAnswer(q.questionId, val, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged));
      });
    });
    if (isFreeResponse) {
      var isSpeaking = (q.modality || '').toLowerCase() === 'speaking';
      if (isSpeaking) {
        sitting.renderFreeResponse(q);
      } else {
        sitting.freeResponseContainer.style.display = 'none';
        sitting.freeResponseContainer.innerHTML = '';
      }
    } else {
      sitting.freeResponseContainer.style.display = 'none';
      sitting.freeResponseContainer.innerHTML = '';
    }
    sitting.renderComponentWidget(q);

    // Previous & Next navigation buttons
    // Navigator Grid active indicator
    document.querySelectorAll('.navigator__cell').forEach(function (cell) {
      cell.classList.remove('is-current');
    });
    var activeCell = document.getElementById('nav-cell-' + q.questionId);
    if (activeCell) {
      activeCell.classList.add('is-current');
    }

    sitting.updateNavigationForPhase();
    if (sitting.initializedPositionSync && typeof sitting.scheduleSync === 'function') {
      sitting.positionDirty = true;
      sitting.scheduleSync(true);
    }
    sitting.initializedPositionSync = true;
    window.scrollTo({ top: 0, behavior: 'instant' });
  };
  sitting.goToQuestion = function (targetOrder) {
    if (targetOrder < 1 || targetOrder > sitting.totalQuestions) return;
    if (sitting.isSpeakingPreparing || sitting.isSpeakingRecording || sitting.isAudioUploading) {
      sitting.saveStatus.textContent = sitting.isAudioUploading
        ? 'Wait for the recording to finish uploading.'
        : (sitting.isSpeakingPreparing ? 'Wait for preparation to finish before changing questions.' : 'Stop the recording before changing questions.');
      sitting.saveStatus.className = 'save-status save-status--error';
      return;
    }

    var targetQuestion = sitting.questionMap[targetOrder];
    var phase = sitting.phases[sitting.activePhaseIndex];
    var currentQuestion = sitting.questionMap[sitting.currentDisplayOrder];
    if (sitting.isSystemPacedAuto(currentQuestion)) {
      // Manual navigation disabled during automated speaking flow
      return;
    }
    if (!targetQuestion || !phase || targetQuestion.skillCode !== phase.skillCode) return;
    var isOneWay = currentQuestion && (currentQuestion.navigationMode === 'AUDIO_DRIVEN');
    if (isOneWay && targetOrder < sitting.currentDisplayOrder) return;
    if (currentQuestion && currentQuestion.navigationMode === 'HYBRID_GROUP_LOCKED') {
      if (sitting.activeGroupId && targetQuestion.groupId !== sitting.activeGroupId) return;
    }
    if (isOneWay && phase.skillCode === 'LIST' && targetOrder < sitting.currentDisplayOrder) return;

    // Strict Unit Locked Sandbox routing
    if (targetQuestion.navigationMode === 'UNIT_LOCKED_SANDBOX') {
      var targetUnit = targetQuestion.unitNumber || 1;
      if (targetUnit !== sitting.activeUnitNumber) return;
    }
    var currentDraft = currentQuestion && sitting.responseDrafts[currentQuestion.questionId];
    if (currentQuestion && currentQuestion.questionType === 'free_response' && currentDraft && currentDraft.dirty) {
      sitting.saveDraft(currentQuestion.questionId);
    }
    sitting.renderQuestion(targetOrder);
  };

  // 4. Progress Stats Updater
  sitting.updateProgressStats = function () {
    var answered = 0;
    var flagged = 0;
    sitting.questions.forEach(function (q) {
      var a = sitting.localAnswers[q.questionId];
      var draft = sitting.responseDrafts[q.questionId];
      var hasResponse = q.questionType === 'free_response'
        ? !!((draft && draft.text.trim()) ||
            (a && ((a.responseText || '').trim() || a.responseAudioPath)))
        : !!(a && a.selectedOptionId !== null && a.selectedOptionId !== undefined);
      if (hasResponse) answered++;
      if (a && a.isFlagged) flagged++;

      var cell = document.getElementById('nav-cell-' + q.questionId);
      if (cell) {
        cell.classList.toggle('is-answered', !!hasResponse);
        cell.classList.toggle('is-unanswered', !hasResponse);
      }
    });
    var unanswered = sitting.totalQuestions - answered;
    var pct = Math.round((answered / Math.max(1, sitting.totalQuestions)) * 100);

    var progressText = document.getElementById('progress-text');
    var progressBar = document.getElementById('progress-bar');
    var flaggedText = document.getElementById('sidebar-flagged-count');
    var unansweredText = document.getElementById('sidebar-unanswered-count');

    if (progressText) progressText.textContent = answered + '/' + sitting.totalQuestions + ' answered';
    if (progressBar) progressBar.style.width = pct + '%';
    if (flaggedText) flaggedText.textContent = flagged;
    if (unansweredText) unansweredText.textContent = unanswered;

    // Modal counters
    var mAns = document.getElementById('modal-stat-answered');
    var mFlag = document.getElementById('modal-stat-flagged');
    var mUnans = document.getElementById('modal-stat-unanswered');
    var mUnansCount = document.getElementById('modal-unanswered-count');
    var mUnansWarn = document.getElementById('modal-unanswered-warning');
    var mAllAns = document.getElementById('modal-all-answered');

    if (mAns) mAns.textContent = answered;
    if (mFlag) mFlag.textContent = flagged;
    if (mUnans) mUnans.textContent = unanswered;
    if (mUnansCount) mUnansCount.textContent = unanswered;
    if (mUnansWarn) mUnansWarn.style.display = (unanswered > 0) ? 'block' : 'none';
    if (mAllAns) mAllAns.style.display = (unanswered <= 0) ? 'block' : 'none';
  };

  sitting.bindFlagFilter = function () {
    if (sitting.flaggedFilterButton) {
      sitting.flaggedFilterButton.addEventListener('click', function () {
        if (sitting.flaggedFilterButton.disabled) return;
        sitting.flaggedOnly = !sitting.flaggedOnly;
        sitting.updateNavigationForPhase();
      });
    }
  };

  sitting.bindPreviousNext = function () {
    // 6. Navigation Event Listeners
    if (sitting.btnPrev) {
      sitting.btnPrev.addEventListener('click', function () {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (currentQ && currentQ.clusterSize === 3) {
          var clusterQs = sitting.questions.filter(function (item) {
            return (currentQ.groupId && item.groupId === currentQ.groupId) ||
              (item.sectionId === currentQ.sectionId && Math.floor((item.displayOrder - 1) / 3) === Math.floor((currentQ.displayOrder - 1) / 3));
          });
          if (clusterQs.length > 0) {
            sitting.goToQuestion(clusterQs[0].displayOrder - 1);
            return;
          }
        }
        sitting.goToQuestion(sitting.currentDisplayOrder - 1);
      });
    }

    if (sitting.btnNext) {
      sitting.btnNext.addEventListener('click', function () {
        if (sitting.btnNext.classList.contains('js-open-skill-advance')) {
          // Advancing to the next skill locks the current one: this is irreversible, so d02
          // requires an explicit confirmation. Skippable per section via confirmOnSkillAdvance.
          var cfg = sitting.parseToolsConfig((sitting.questionMap[sitting.currentDisplayOrder] || {}).toolsConfigJson);
          if (cfg.confirmOnSkillAdvance === false) {
            sitting.advanceToNextSkill();
            return;
          }
          var nextPhase = sitting.phases[sitting.activePhaseIndex + 1];
          var message = 'You are about to finish ' + ((sitting.phases[sitting.activePhaseIndex] || {}).skillName || 'this skill')
            + ' and start ' + ((nextPhase || {}).skillName || 'the next skill')
            + '. You cannot return to this skill afterwards. Continue?';
          if (window.confirm(message)) {
            sitting.advanceToNextSkill();
          }
          return;
        }
        if (sitting.btnNext.classList.contains('js-open-unit-lock-modal')) {
          sitting.openUnitLockModal();
          return;
        }
        if (sitting.currentDisplayOrder >= sitting.totalQuestions) {
          sitting.openSubmitModal();
          return;
        }
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        var nextQ = sitting.questionMap[sitting.currentDisplayOrder + 1];
        if (currentQ && nextQ && currentQ.navigationMode === 'HYBRID_GROUP_LOCKED' && currentQ.groupId !== nextQ.groupId) {
          var cfg = sitting.parseToolsConfig(currentQ.toolsConfigJson);
          if (cfg.confirmOnNext !== false) {
            sitting.openGroupLockModal(nextQ.groupId, sitting.currentDisplayOrder + 1);
            return;
          }
        }
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (currentQ && currentQ.clusterSize === 3) {
          var clusterQs = sitting.questions.filter(function (item) {
            return (currentQ.groupId && item.groupId === currentQ.groupId) ||
              (item.sectionId === currentQ.sectionId && Math.floor((item.displayOrder - 1) / 3) === Math.floor((currentQ.displayOrder - 1) / 3));
          });
          if (clusterQs.length > 0) {
            sitting.goToQuestion(clusterQs[clusterQs.length - 1].displayOrder + 1);
            return;
          }
        }
        sitting.goToQuestion(sitting.currentDisplayOrder + 1);
      });
    }
  };

  sitting.bindPalette = function () {
    // Bottom question palette click handler
    sitting.paletteChipsTrack = document.getElementById('bottom-palette-chips');
    if (sitting.paletteChipsTrack) {
      sitting.paletteChipsTrack.addEventListener('click', function (e) {
        var targetChip = e.target.closest('.palette-chip');
        if (targetChip && !targetChip.disabled && !targetChip.classList.contains('palette-chip--disabled')) {
          if (sitting.isSystemPacedAuto(sitting.questionMap[sitting.currentDisplayOrder])) return;
          var targetOrder = parseInt(targetChip.getAttribute('data-display-order'), 10);
          if (!isNaN(targetOrder)) {
            var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
            var isOneWay = currentQ && currentQ.navigationMode === 'AUDIO_DRIVEN';
            if (isOneWay && targetOrder < sitting.currentDisplayOrder) return;
            sitting.goToQuestion(targetOrder);
          }
        }
      });
    }

    // Navigator grid cells click handler
    sitting.navGrid = document.getElementById('navigator-grid');
    if (sitting.navGrid) {
      sitting.navGrid.addEventListener('click', function (e) {
        var targetCell = e.target.closest('.navigator__cell');
        if (targetCell) {
          if (sitting.isSystemPacedAuto(sitting.questionMap[sitting.currentDisplayOrder])) return;
          var targetOrder = parseInt(targetCell.getAttribute('data-display-order'), 10);
          if (!isNaN(targetOrder)) {
            sitting.goToQuestion(targetOrder);
          }
        }
      });
    }

    // Flag toggle button
    if (sitting.flagBtn) {
      sitting.flagBtn.addEventListener('click', function () {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (!currentQ) return;
        var curFlagged = !!(sitting.localAnswers[currentQ.questionId] && sitting.localAnswers[currentQ.questionId].isFlagged);
        var newFlagged = !curFlagged;

        sitting.flagBtn.setAttribute('data-flagged', newFlagged ? 'true' : 'false');
        sitting.flagBtn.setAttribute('aria-pressed', newFlagged ? 'true' : 'false');
        sitting.flagBtn.textContent = newFlagged ? 'Flagged for review' : 'Flag for review';
        if (newFlagged) {
          sitting.flagBtn.classList.add('btn--primary');
        } else {
          sitting.flagBtn.classList.remove('btn--primary');
        }

        var curSelected = sitting.localAnswers[currentQ.questionId] ? sitting.localAnswers[currentQ.questionId].selectedOptionId : null;
        sitting.saveAnswer(currentQ.questionId, curSelected, newFlagged);
      });
    }

    // Initial option change listeners
    sitting.initialRadios = sitting.optionList.querySelectorAll('.js-option-radio');
    sitting.initialRadios.forEach(function (radio) {
      radio.addEventListener('change', function () {
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (!currentQ) return;
        var val = parseInt(this.value, 10);
        sitting.optionList.querySelectorAll('.option').forEach(function (lbl) { lbl.classList.remove('is-selected'); });
        var parentLabel = radio.closest('.option');
        if (parentLabel) parentLabel.classList.add('is-selected');
        sitting.saveAnswer(currentQ.questionId, val, !!(sitting.localAnswers[currentQ.questionId] && sitting.localAnswers[currentQ.questionId].isFlagged));
      });
    });
  };
})(window.ExamSitting);
