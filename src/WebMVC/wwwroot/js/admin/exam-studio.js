(function() {
  var config = JSON.parse(document.getElementById("studio-csrf-holder").dataset.configuration);
  var examId = config.ExamId;
  var currentSectionId = config.CurrentSectionId;
  var activeSlicerGroupId = null;
  var activeSlicerSectionId = null;
  var stagedSlicerStarts = Object.create(null);
  var isSaving = false;
  var saveDebounceTimer = null;
  var statusIndicator = document.getElementById('autosave-status');

  function setSlicerStatus(sectionId, message, state) {
    var status = document.getElementById('slicer-status-' + sectionId);
    if (!status) return;

    status.textContent = message;
    status.hidden = !message;
    status.setAttribute('data-state', state || 'info');
    status.setAttribute('aria-live', state === 'error' ? 'assertive' : 'polite');
  }

  function pauseStudioAudio(exceptAudio) {
    document.querySelectorAll('audio').forEach(function(audio) {
      if (audio !== exceptAudio && !audio.paused) audio.pause();
    });
  }

  window.studioAudioManager = {
    pauseAll: function() {
      pauseStudioAudio(null);
    }
  };

  document.addEventListener('play', function(event) {
    if (event.target && event.target.tagName === 'AUDIO') {
      pauseStudioAudio(event.target);
    }
  }, true);

  document.getElementById('studio-workstation')?.addEventListener('click', function(event) {
    if (event.target.closest('.question-card, .card-editor')) {
      pauseStudioAudio(null);
    }
  });

  document.getElementById('studio-workstation')?.addEventListener('focusin', function(event) {
    if (event.target.closest('.question-card, .card-editor')) {
      pauseStudioAudio(null);
    }
  });

  function getCsrfToken() {
    var input = document.querySelector('#studio-csrf-holder input[name="__RequestVerificationToken"]')
      || document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function normalizeMediaUrl(url) {
    if (!url) return '';
    var trimmed = ('' + url).trim();
    if (trimmed.startsWith('/media/') || trimmed.startsWith('http://') || trimmed.startsWith('https://') || trimmed.startsWith('/audio/') || trimmed.startsWith('/images/')) {
      return trimmed;
    }
    var clean = trimmed.replace(/^\/+/, '');
    if (clean.startsWith('exams/') || clean.startsWith('imports/')) {
      return '/media/' + clean;
    }
    return trimmed.startsWith('/') ? trimmed : '/' + trimmed;
  }

  // --- Autosave Engine ---
  function markSaving() {
    isSaving = true;
    if (statusIndicator) {
      statusIndicator.textContent = 'Saving...';
      statusIndicator.style.color = '#0b4ea2';
    }
  }

  function markSaved() {
    isSaving = false;
    if (statusIndicator) {
      var now = new Date();
      var timeStr = now.toTimeString().split(' ')[0];
      statusIndicator.textContent = 'Saved at ' + timeStr;
      statusIndicator.style.color = '#0a6b3c';
    }
  }

  function markSaveFailed(msg) {
    isSaving = false;
    if (statusIndicator) {
      statusIndicator.textContent = 'Save failed: ' + (msg || 'Error');
      statusIndicator.style.color = '#b31217';
    }
  }

  window.addEventListener('beforeunload', function(e) {
    if (isSaving) {
      e.preventDefault();
      e.returnValue = 'You have unsaved changes in progress. Are you sure you want to leave?';
      return e.returnValue;
    }
  });

  // Attach autosave listeners to inputs
  function bindAutosaveInputs() {
    document.querySelectorAll('.studio-autosave').forEach(function(input) {
      if (input._hasAutosave) return;
      input._hasAutosave = true;

      input.addEventListener('input', function() {
        clearTimeout(saveDebounceTimer);
        markSaving();
        saveDebounceTimer = setTimeout(function() {
          performAutosave(input);
        }, 1500);
      });

      input.addEventListener('blur', function() {
        clearTimeout(saveDebounceTimer);
        performAutosave(input);
      });

      input.addEventListener('change', function() {
        clearTimeout(saveDebounceTimer);
        performAutosave(input);
      });
    });

    document.querySelectorAll('.studio-opt-radio, .studio-opt-checkbox').forEach(function(optChoice) {
      if (optChoice._hasAutosave) return;
      optChoice._hasAutosave = true;
      optChoice.addEventListener('change', function() {
        saveSingleQuestion(parseInt(optChoice.getAttribute('data-qid'), 10));
      });
    });

    document.querySelectorAll('.studio-opt-text, .studio-opt-rationale').forEach(function(optInput) {
      if (optInput._hasAutosave) return;
      optInput._hasAutosave = true;
      optInput.addEventListener('input', function() {
        clearTimeout(saveDebounceTimer);
        markSaving();
        saveDebounceTimer = setTimeout(function() {
          saveSingleQuestion(parseInt(optInput.getAttribute('data-qid'), 10));
        }, 1500);
      });
    });

    document.querySelectorAll('.studio-question-type').forEach(function(typeInput) {
      if (typeInput._hasTypeChange) return;
      typeInput._hasTypeChange = true;
      typeInput.addEventListener('change', function() {
        setQuestionTypeDisplay(typeInput);
        saveSingleQuestion(parseInt(typeInput.getAttribute('data-qid'), 10));
      });
    });

    document.querySelectorAll('.studio-criterion-checkbox').forEach(function(checkbox) {
      if (checkbox._hasCriterionChange) return;
      checkbox._hasCriterionChange = true;
      checkbox.addEventListener('change', function() {
        saveSingleQuestion(parseInt(checkbox.getAttribute('data-qid'), 10));
      });
    });
  }

  bindAutosaveInputs();

  function performAutosave(input) {
    var type = input.getAttribute('data-type');
    var id = parseInt(input.getAttribute('data-id'), 10);
    if (type === 'question') {
      saveSingleQuestion(id);
    } else if (type === 'group') {
      saveSingleGroup(id);
    }
  }

  window.saveSingleQuestion = function(questionId) {
    var card = document.getElementById('question-card-' + questionId);
    if (!card) return;

    markSaving();

    var stem = card.querySelector('[data-field="Stem"]')?.value || '';
    var catId = card.querySelector('[data-field="CategoryId"]')?.value;
    var expl = card.querySelector('[data-field="Explanation"]')?.value;
    var prep = card.querySelector('[data-field="PrepSeconds"]')?.value;
    var resp = card.querySelector('[data-field="ResponseSeconds"]')?.value;
    var minW = card.querySelector('[data-field="MinWords"]')?.value;
    var maxW = card.querySelector('[data-field="MaxWords"]')?.value;
    var hint = card.querySelector('[data-field="Hint"]')?.value;
    var maxSel = card.querySelector('[data-field="MaxSelections"]')?.value;
    var allowNumCheck = card.querySelector('[data-field="AllowNumber"]');
    var allowNumber = allowNumCheck ? allowNumCheck.checked : true;
    var acceptedAns = card.querySelector('[data-field="AcceptedAnswersJson"]')?.value;
    var reqKeywords = card.querySelector('[data-field="RequiredKeywords"]')?.value;
    var typeInput = card.querySelector('.studio-question-type');
    var questionTypeStr = typeInput ? typeInput.value : (card.dataset.questionType || 'mcq');

    var options = [];
    if (questionTypeStr === 'multi_select') {
      var multiPanel = card.querySelector('.component-multi-select');
      if (multiPanel) {
        multiPanel.querySelectorAll('.studio-opt-row-multi').forEach(function(row) {
          var label = row.getAttribute('data-label');
          var txtInput = row.querySelector('.studio-opt-text');
          var isCorrCheck = row.querySelector('.studio-opt-checkbox');
          var ratInput = row.querySelector('.studio-opt-rationale');
          options.push({
            label: label,
            text: txtInput ? txtInput.value : '',
            isCorrect: isCorrCheck ? isCorrCheck.checked : false,
            rationale: ratInput ? ratInput.value : null
          });
        });
      }
    } else if (questionTypeStr === 'mcq' || questionTypeStr === 'mcq_3' || questionTypeStr === 'dropdown') {
      var mcqPanel = card.querySelector('.component-mcq');
      if (mcqPanel) {
        mcqPanel.querySelectorAll('.studio-opt-row-single').forEach(function(row) {
          var label = row.getAttribute('data-label');
          var txtInput = row.querySelector('.studio-opt-text');
          var isCorrRadio = row.querySelector('.studio-opt-radio');
          var ratInput = row.querySelector('.studio-opt-rationale');
          options.push({
            label: label,
            text: txtInput ? txtInput.value : '',
            isCorrect: isCorrRadio ? isCorrRadio.checked : false,
            rationale: ratInput ? ratInput.value : null
          });
        });
      }
    } else if (questionTypeStr === 'drag_drop') {
      var dragPanel = card.querySelector('.component-drag-drop');
      if (dragPanel) {
        dragPanel.querySelectorAll('.studio-opt-row-drag, .studio-opt-row-single').forEach(function(row) {
          var label = row.getAttribute('data-label');
          var txtInput = row.querySelector('.studio-opt-text');
          var isCorrRadio = row.querySelector('.studio-opt-radio');
          var ratInput = row.querySelector('.studio-opt-rationale');
          var textVal = txtInput ? txtInput.value.trim() : '';
          if (textVal) {
            options.push({
              label: label,
              text: textVal,
              isCorrect: isCorrRadio ? isCorrRadio.checked : false,
              rationale: ratInput ? ratInput.value : null
            });
          }
        });
      }
    }

    var criterionIds = [];
    card.querySelectorAll('.studio-criterion-checkbox:checked').forEach(function(checkbox) {
      criterionIds.push(parseInt(checkbox.getAttribute('data-criterion-id'), 10));
    });

    var isFreeResponse = questionTypeStr === 'free_response';
    var isInline = questionTypeStr === 'inline_completion';

    var diffLevel = parseInt(card.getAttribute('data-difficulty'), 10);
    if (!diffLevel || isNaN(diffLevel) || diffLevel < 1 || diffLevel > 5) diffLevel = 3;

    var dispOrder = parseInt(card.getAttribute('data-display-order'), 10) || 1;
    var grpId = parseInt(card.closest('.card-editor')?.dataset.groupId, 10) || null;

    var payload = {
      groupId: grpId,
      displayOrder: dispOrder,
      stem: stem,
      difficultyLevel: diffLevel,
      questionType: questionTypeStr,
      explanation: expl,
      categoryId: catId ? parseInt(catId, 10) : null,
      prepSeconds: isFreeResponse && prep ? parseInt(prep, 10) : null,
      responseSeconds: isFreeResponse && resp ? parseInt(resp, 10) : null,
      minWords: isFreeResponse && minW ? parseInt(minW, 10) : null,
      maxWords: isFreeResponse && maxW ? parseInt(maxW, 10) : (isInline && maxW ? parseInt(maxW, 10) : null),
      hint: isFreeResponse ? hint : null,
      maxSelections: questionTypeStr === 'multi_select' && maxSel ? parseInt(maxSel, 10) : null,
      allowNumber: allowNumber,
      acceptedAnswersJson: isInline ? acceptedAns : null,
      requiredKeywords: reqKeywords,
      options: options.length > 0 ? options : null,
      criterionIds: isFreeResponse && criterionIds.length > 0 ? criterionIds : null
    };

    fetch('/admin/exams/' + examId + '/studio/question/' + questionId, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      },
      body: JSON.stringify(payload)
    })
    .then(async function(res) {
      if (!res.ok) {
        var errDetail = 'HTTP ' + res.status;
        try {
          var data = await res.json();
          if (data && (data.message || data.title || data.detail)) {
            errDetail = data.message || data.title || data.detail;
          }
        } catch (_) {}
        throw new Error(errDetail);
      }
      return res.json();
    })
    .then(function() {
      if (typeInput) {
        typeInput.dataset.savedType = questionTypeStr;
        card.dataset.questionType = questionTypeStr;
      }
      markSaved();
      if (isFreeResponse) {
        syncSectionCriteria(card, criterionIds);
      }
      refreshAudit();
    })
    .catch(function(err) {
      if (typeInput) {
        typeInput.value = typeInput.dataset.savedType || card.dataset.questionType || 'mcq';
        setQuestionTypeDisplay(typeInput);
      }
      markSaveFailed(err.message);
    });
  };

  function setQuestionTypeDisplay(typeInput) {
    var card = document.getElementById('question-card-' + typeInput.getAttribute('data-qid'));
    if (!card) return;

    var selectedType = typeInput.value;
    card.dataset.questionType = selectedType;

    // Toggle panels
    var mcqPanel = card.querySelector('.component-mcq');
    var multiPanel = card.querySelector('.component-multi-select');
    var inlinePanel = card.querySelector('.component-inline-completion');
    var dragPanel = card.querySelector('.component-drag-drop');
    var freeResponsePanel = card.querySelector('.component-free-response');
    var explanationLabel = card.querySelector('[data-role="explanation-label"]');

    if (mcqPanel) mcqPanel.style.display = (selectedType === 'mcq' || selectedType === 'mcq_3' || selectedType === 'dropdown') ? 'block' : 'none';
    if (multiPanel) multiPanel.style.display = (selectedType === 'multi_select') ? 'block' : 'none';
    if (inlinePanel) inlinePanel.style.display = (selectedType === 'inline_completion') ? 'block' : 'none';
    if (dragPanel) dragPanel.style.display = (selectedType === 'drag_drop') ? 'block' : 'none';
    if (freeResponsePanel) freeResponsePanel.style.display = (selectedType === 'free_response') ? 'block' : 'none';

    if (explanationLabel) {
      explanationLabel.textContent = selectedType === 'free_response'
        ? 'Sample response / answer guidance'
        : 'Explanation / pedagogical rationale';
    }
  }

  function syncSectionCriteria(sourceCard, criterionIds) {
    var sectionId = sourceCard.dataset.sectionId;
    var selected = new Set(criterionIds.map(function(id) { return '' + id; }));
    document.querySelectorAll('.question-card[data-section-id="' + sectionId + '"] .studio-criterion-checkbox')
      .forEach(function(checkbox) {
        checkbox.checked = selected.has(checkbox.getAttribute('data-criterion-id'));
      });
  }

  window.saveSingleGroup = function(groupId, slicerSectionId) {
    var card = document.getElementById('group-card-' + groupId);
    if (!card) return;

    markSaving();

    var secId = parseInt(card.getAttribute('data-section-id'), 10);
    var audioPath = card.querySelector('[data-field="AudioPath"]')?.value;
    var startInput = card.querySelector('[data-field="AudioStartMs"]');
    var endInput = card.querySelector('[data-field="AudioEndMs"]');
    var startMs = startInput?.value;
    var endMs = endInput?.value;
    var sliceWasStaged = Object.prototype.hasOwnProperty.call(stagedSlicerStarts, groupId);
    if (slicerSectionId === undefined && sliceWasStaged) {
      startMs = startInput?.getAttribute('data-saved-value');
      endMs = endInput?.getAttribute('data-saved-value');
    }
    var imgPath = card.querySelector('[data-field="ImagePath"]')?.value;
    var imgAlt = card.querySelector('[data-field="ImageAlt"]')?.value;
    var passage = card.querySelector('[data-field="Passage"]')?.value;
    var transcript = card.querySelector('[data-field="Transcript"]')?.value;
    var unitSelect = card.querySelector('[data-field="UnitNumber"]');
    var unitNumber = unitSelect ? parseInt(unitSelect.value, 10) : parseInt(card.getAttribute('data-unit-number') || '1', 10);
    var dispOrder = parseInt(card.getAttribute('data-display-order'), 10) || 1;

    var payload = {
      sectionId: secId,
      displayOrder: dispOrder,
      audioPath: audioPath,
      audioStartMs: startMs ? parseInt(startMs, 10) : null,
      audioEndMs: endMs ? parseInt(endMs, 10) : null,
      imagePath: imgPath,
      imageAlt: imgAlt,
      passage: passage,
      transcript: transcript,
      unitNumber: unitNumber
    };

    fetch('/admin/exams/' + examId + '/studio/group/' + groupId, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      },
      body: JSON.stringify(payload)
    })
    .then(async function(res) {
      if (!res.ok) {
        var errDetail = 'HTTP ' + res.status;
        try {
          var data = await res.json();
          if (data && (data.message || data.title || data.detail)) {
            errDetail = data.message || data.title || data.detail;
          }
        } catch (_) {}
        throw new Error(errDetail);
      }
      return res.json();
    })
    .then(function() {
      markSaved();
      card.setAttribute('data-unit-number', unitNumber);
      if (slicerSectionId !== undefined) {
        delete stagedSlicerStarts[groupId];
        if (startInput) startInput.setAttribute('data-saved-value', startInput.value);
        if (endInput) endInput.setAttribute('data-saved-value', endInput.value);
        setSlicerStatus(slicerSectionId, 'Saved', 'success');
      }
      refreshAudit();
    })
    .catch(function(err) {
      markSaveFailed(err.message);
      if (slicerSectionId !== undefined) {
        setSlicerStatus(slicerSectionId, 'Save failed: ' + err.message, 'error');
      }
    });
  };

  // --- Visual Audio Slicer Widget Mechanics ---
  function formatTime(sec) {
    if (isNaN(sec) || sec < 0) sec = 0;
    var m = Math.floor(sec / 60);
    var s = Math.floor(sec % 60);
    var ms = Math.floor((sec % 1) * 1000);
    return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s + '.' + (ms < 100 ? (ms < 10 ? '00' : '0') : '') + ms;
  }

  function formatClock(milliseconds) {
    return formatTime(milliseconds / 1000).split('.')[0];
  }

  function readMilliseconds(input) {
    if (!input || input.value.trim() === '') return null;
    var milliseconds = Number(input.value);
    return Number.isInteger(milliseconds) && milliseconds >= 0 ? milliseconds : null;
  }

  function getTargetGroupId(sectionId) {
    if (activeSlicerGroupId && String(activeSlicerSectionId) === String(sectionId)) {
      return activeSlicerGroupId;
    }

    return document.querySelector('#ws-sec-' + sectionId + ' .card-editor')?.getAttribute('data-group-id');
  }

  window.setActiveSlicerGroup = function(groupId, sectionId) {
    activeSlicerGroupId = groupId;
    activeSlicerSectionId = sectionId;
    var grpCard = document.getElementById('group-card-' + groupId);
    if (!grpCard) return;

    var audio = document.getElementById('slicer-audio-' + sectionId);
    var grpAudioPath = grpCard.querySelector('[data-field="AudioPath"]')?.value;
    if (audio && grpAudioPath) {
      var normPath = normalizeMediaUrl(grpAudioPath);
      if (audio.getAttribute('src') !== normPath) {
        audio.src = normPath;
        var label = document.getElementById('slicer-track-label-' + sectionId);
        if (label) label.textContent = normPath;
      }
    }

    var startVal = grpCard.querySelector('[data-field="AudioStartMs"]')?.value;
    var endVal = grpCard.querySelector('[data-field="AudioEndMs"]')?.value;
    updateSlicerRegion(sectionId, readMilliseconds({ value: startVal || '' }), readMilliseconds({ value: endVal || '' }));
    setSlicerStatus(sectionId, '', 'info');
  };

  function updateSlicerRegion(secId, startMs, endMs) {
    var region = document.getElementById('slicer-region-' + secId);
    var marker = document.getElementById('slicer-start-marker-' + secId);
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!region || !marker || !audio || !audio.duration || !isFinite(audio.duration)) {
      if (region) region.style.display = 'none';
      if (marker) marker.style.display = 'none';
      return;
    }

    if (startMs !== null && startMs >= 0) {
      marker.style.left = Math.min(100, (startMs / 1000 / audio.duration) * 100) + '%';
      marker.style.display = 'block';
    } else {
      marker.style.display = 'none';
    }

    if (startMs !== null && endMs !== null && endMs > startMs) {
      var leftPct = Math.min(100, (startMs / 1000 / audio.duration) * 100);
      var widthPct = ((endMs - startMs) / 1000 / audio.duration) * 100;
      region.style.left = leftPct + '%';
      region.style.width = widthPct + '%';
      region.style.display = 'block';
    } else {
      region.style.display = 'none';
    }
  }

  window.toggleSlicerPlay = function(secId) {
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!audio) return;

    if (audio.paused) {
      audio.play().catch(function() {
        setSlicerStatus(secId, 'Audio playback could not be started.', 'error');
      });
    } else {
      audio.pause();
    }
  };

  window.onScrubberClick = function(e, secId) {
    var scrubber = document.getElementById('slicer-scrubber-' + secId);
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!scrubber || !audio || !audio.duration) return;

    var rect = scrubber.getBoundingClientRect();
    var x = e.clientX - rect.left;
    var pct = Math.max(0, Math.min(1, x / rect.width));
    audio.currentTime = pct * audio.duration;
  };

  // Attach timeupdate to slicer audio
  document.querySelectorAll('[id^="slicer-audio-"]').forEach(function(audio) {
    var secId = audio.id.replace('slicer-audio-', '');
    var syncPlayButton = function() {
      var btn = document.getElementById('btn-slicer-play-' + secId);
      if (btn) btn.textContent = audio.paused ? 'Play' : 'Pause';
    };

    audio.addEventListener('play', syncPlayButton);
    audio.addEventListener('pause', syncPlayButton);
    audio.addEventListener('loadedmetadata', function() {
      var groupId = getTargetGroupId(secId);
      var group = groupId ? document.getElementById('group-card-' + groupId) : null;
      updateSlicerRegion(
        secId,
        readMilliseconds(group?.querySelector('[data-field="AudioStartMs"]')),
        readMilliseconds(group?.querySelector('[data-field="AudioEndMs"]'))
      );
    });

    audio.addEventListener('timeupdate', function() {
      var timer = document.getElementById('slicer-timer-' + secId);
      var progress = document.getElementById('slicer-progress-' + secId);
      var cursor = document.getElementById('slicer-cursor-' + secId);

      if (timer && audio.duration) {
        timer.textContent = formatTime(audio.currentTime) + ' / ' + formatTime(audio.duration);
      }
      if (progress && audio.duration) {
        var pct = (audio.currentTime / audio.duration) * 100;
        progress.style.width = pct + '%';
        if (cursor) cursor.style.left = pct + '%';
      }
    });

    audio.addEventListener('ended', function() {
      syncPlayButton();
    });
  });

  window.markCurrentStart = function(secId) {
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!audio) return;
    var ms = Math.floor(audio.currentTime * 1000);
    if (!isFinite(ms) || ms < 0) return;
    var targetGroupId = getTargetGroupId(secId);
    if (!targetGroupId) return;

    var input = document.getElementById('start-ms-' + targetGroupId);
    if (!input) return;

    input.value = ms;
    stagedSlicerStarts[targetGroupId] = ms;
    var endMs = readMilliseconds(document.getElementById('end-ms-' + targetGroupId));
    updateSlicerRegion(secId, ms, endMs);
    setSlicerStatus(secId, 'Start marked: ' + formatClock(ms) + ' — click Mark End to save', 'info');
  };

  window.markCurrentEnd = function(secId) {
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!audio) return;
    var endMs = Math.floor(audio.currentTime * 1000);
    var targetGroupId = getTargetGroupId(secId);
    if (!targetGroupId) {
      setSlicerStatus(secId, 'End time must be after Start time (00:00)', 'error');
      return;
    }

    var startInput = document.getElementById('start-ms-' + targetGroupId);
    var endInput = document.getElementById('end-ms-' + targetGroupId);
    var startMs = readMilliseconds(startInput);
    if (!startInput || !endInput || startMs === null || !isFinite(endMs) || endMs <= startMs) {
      setSlicerStatus(secId, 'End time must be after Start time (' + formatClock(startMs === null ? 0 : startMs) + ')', 'error');
      return;
    }

    endInput.value = endMs;
    updateSlicerRegion(secId, startMs, endMs);
    setSlicerStatus(secId, 'Saving...', 'info');
    saveSingleGroup(parseInt(targetGroupId, 10), secId);
  };

  window.previewCurrentSlice = function(secId) {
    var targetGroupId = getTargetGroupId(secId);
    if (!targetGroupId) return;

    var startMs = readMilliseconds(document.getElementById('start-ms-' + targetGroupId));
    var endMs = readMilliseconds(document.getElementById('end-ms-' + targetGroupId));
    var audio = document.getElementById('slicer-audio-' + secId);
    if (!audio || startMs === null || endMs === null || endMs <= startMs) {
      setSlicerStatus(secId, 'Set valid Start and End times before previewing the slice.', 'error');
      return;
    }

    audio.currentTime = startMs / 1000;
    audio.play().catch(function() {
      setSlicerStatus(secId, 'Audio playback could not be started.', 'error');
    });

    var checkSliceEnd = function() {
      if (audio.currentTime >= endMs / 1000) {
        audio.pause();
        audio.removeEventListener('timeupdate', checkSliceEnd);
      }
    };
    audio.addEventListener('timeupdate', checkSliceEnd);
  };

  window.clearCurrentSlice = function(secId) {
    var targetGroupId = getTargetGroupId(secId);
    if (!targetGroupId) return;

    var sInput = document.getElementById('start-ms-' + targetGroupId);
    var eInput = document.getElementById('end-ms-' + targetGroupId);
    if (sInput) sInput.value = '';
    if (eInput) eInput.value = '';
    updateSlicerRegion(secId, null, null);
    setSlicerStatus(secId, 'Saving...', 'info');
    saveSingleGroup(parseInt(targetGroupId, 10), secId);
  };

  // --- Upload Audio / Media Handling ---
  function probeAudioDurationSec(file, timeoutMs) {
    return new Promise(function(resolve) {
      var url;
      var timer;
      var probe = new Audio();
      function done(seconds) {
        clearTimeout(timer);
        probe.removeAttribute('src');
        if (url) URL.revokeObjectURL(url);
        resolve(seconds);
      }
      try {
        url = URL.createObjectURL(file);
      } catch (e) {
        resolve(null);
        return;
      }
      timer = setTimeout(function() { done(null); }, timeoutMs);
      probe.preload = 'metadata';
      probe.addEventListener('loadedmetadata', function() {
        var d = probe.duration;
        done(isFinite(d) && d > 0 ? Math.max(1, Math.round(d)) : null);
      }, { once: true });
      probe.addEventListener('error', function() { done(null); }, { once: true });
      probe.src = url;
    });
  }

  var FALLBACK_AUDIO_DURATION_SEC = 300;

  window.uploadAudioFile = function(inputEl, secId) {
    if (!inputEl.files || inputEl.files.length === 0) return;
    var file = inputEl.files[0];

    var durationProbe = probeAudioDurationSec(file, 3000);
    var formData = new FormData();
    formData.append('file', file);

    markSaving();
    fetch('/admin/exams/' + examId + '/media', {
      method: 'POST',
      headers: {
        'RequestVerificationToken': getCsrfToken()
      },
      body: formData
    })
    .then(function(res) {
      if (!res.ok) throw new Error('Upload failed');
      return Promise.all([res.json(), durationProbe]);
    })
    .then(function(uploaded) {
      var result = uploaded[0];
      var durationSec = uploaded[1] || FALLBACK_AUDIO_DURATION_SEC;
      var mediaUrl = normalizeMediaUrl(result.url || result.path);
      var audio = document.getElementById('slicer-audio-' + secId);
      if (audio) {
        audio.src = mediaUrl;
        var label = document.getElementById('slicer-track-label-' + secId);
        if (label) label.textContent = mediaUrl;
      }

      var audioFormData = new FormData();
      audioFormData.append('SectionId', secId);
      audioFormData.append('AudioPath', mediaUrl);
      audioFormData.append('DurationSec', durationSec);
      audioFormData.append('SizeBytes', file.size || 0);

      return fetch('/admin/exams/' + examId + '/audio', {
        method: 'POST',
        headers: {
          'RequestVerificationToken': getCsrfToken(),
          'Accept': 'application/json',
          'X-Requested-With': 'XMLHttpRequest'
        },
        body: audioFormData
      });
    })
    .then(function() {
      markSaved();
    })
    .catch(function(err) {
      markSaveFailed('Audio upload failed: ' + err.message);
    });
  };

  window.triggerFileInput = function(id) {
    document.getElementById(id)?.click();
  };

  window.uploadImageInput = function(inputEl, groupId) {
    if (!inputEl.files || inputEl.files.length === 0) return;
    uploadImageFile(inputEl.files[0], groupId);
  };

  function uploadImageFile(file, groupId) {
    var formData = new FormData();
    formData.append('file', file);

    markSaving();
    fetch('/admin/exams/' + examId + '/media', {
      method: 'POST',
      headers: {
        'RequestVerificationToken': getCsrfToken()
      },
      body: formData
    })
    .then(function(res) {
      if (!res.ok) throw new Error('Upload failed');
      return res.json();
    })
    .then(function(result) {
      markSaved();
      var mediaUrl = normalizeMediaUrl(result.url || result.path);
      var pathInput = document.getElementById('img-path-' + groupId);
      if (pathInput) pathInput.value = mediaUrl;

      var preview = document.getElementById('preview-img-' + groupId);
      var placeholder = document.getElementById('dropzone-placeholder-' + groupId);
      if (preview) {
        preview.src = mediaUrl;
        preview.style.display = 'block';
      }
      if (placeholder) placeholder.style.display = 'none';

      saveSingleGroup(groupId);
    })
    .catch(function(err) {
      markSaveFailed('Image upload failed: ' + err.message);
    });
  }

  window.onDragOver = function(e) {
    e.preventDefault();
    e.currentTarget.classList.add('is-dragover');
  };

  window.onDragLeave = function(e) {
    e.currentTarget.classList.remove('is-dragover');
  };

  window.onDropFile = function(e, groupId, type) {
    e.preventDefault();
    e.currentTarget.classList.remove('is-dragover');
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      uploadImageFile(e.dataTransfer.files[0], groupId);
    }
  };

  // --- Helpers for Templates ---
  window.insertBlankInStem = function(qid) {
    var stemInput = document.getElementById('stem-' + qid);
    if (!stemInput) return;
    var start = stemInput.selectionStart || stemInput.value.length;
    var val = stemInput.value;
    stemInput.value = val.substring(0, start) + ' _____ ' + val.substring(start);
    stemInput.focus();
    saveSingleQuestion(qid);
  };

  window.insertP6Blank = function(groupId, blankNum) {
    var ta = document.getElementById('p6-passage-' + groupId);
    if (!ta) return;
    var token = '[' + blankNum + ']';
    var start = ta.selectionStart || ta.value.length;
    var val = ta.value;
    ta.value = val.substring(0, start) + ' ' + token + ' ' + val.substring(start);
    ta.focus();
    renderP6Preview(groupId);
    saveSingleGroup(groupId);
  };

  window.renderP6Preview = function(groupId) {
    var ta = document.getElementById('p6-passage-' + groupId);
    var preview = document.getElementById('p6-preview-' + groupId);
    if (!ta || !preview) return;

    var text = ta.value || '';
    var escaped = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/\n/g, '<br/>');

    var rendered = escaped.replace(/\[([1-9])\]/g, function(match, p1) {
      return '<span class="blank-badge" onclick="focusP6Question(' + groupId + ', ' + p1 + ')">[' + p1 + ']</span>';
    });

    preview.innerHTML = rendered || '<span class="text-muted italic">Type passage text above to preview blanks...</span>';
  };

  window.focusP6Question = function(groupId, blankNum) {
    var grpCard = document.getElementById('group-card-' + groupId);
    if (!grpCard) return;
    var qCards = grpCard.querySelectorAll('.question-card');
    if (qCards && qCards[blankNum - 1]) {
      qCards[blankNum - 1].scrollIntoView({ behavior: 'smooth', block: 'center' });
      qCards[blankNum - 1].classList.add('is-highlighted');
      setTimeout(function() { qCards[blankNum - 1].classList.remove('is-highlighted'); }, 2000);
    }
  };

  // Initial render of P6 previews
  document.querySelectorAll('.p6-passage-input').forEach(function(ta) {
    var gId = parseInt(ta.getAttribute('data-id'), 10);
    if (gId) renderP6Preview(gId);
  });

  window.switchDocTab = function(groupId, tabIndex) {
    var tabs = document.querySelectorAll('#doc-tabs-' + groupId + ' .doc-tab');
    tabs.forEach(function(t, i) {
      if (i + 1 === tabIndex) t.classList.add('is-active');
      else t.classList.remove('is-active');
    });
  };

  // --- Filtering & Navigation Tabs ---
  window.selectSkill = function(skillKey) {
    document.querySelectorAll('.studio-skill-tab').forEach(function(tab) {
      if (tab.getAttribute('data-skill-key') === skillKey) tab.classList.add('is-active');
      else tab.classList.remove('is-active');
    });

    // Show only section tabs matching skill
    var firstVisibleSecId = null;
    document.querySelectorAll('.studio-section-tab').forEach(function(secTab) {
      if (secTab.getAttribute('data-skill-key') === skillKey) {
        secTab.style.display = 'inline-flex';
        if (!firstVisibleSecId) firstVisibleSecId = parseInt(secTab.getAttribute('data-section-id'), 10);
      } else {
        secTab.style.display = 'none';
      }
    });

    // Switch to first section of skill if current section is not in this skill
    var currentTab = document.getElementById('tab-sec-' + currentSectionId);
    if (!currentTab || currentTab.getAttribute('data-skill-key') !== skillKey) {
      if (firstVisibleSecId) selectSection(firstVisibleSecId);
    }
  };

  window.selectSection = function(sectionId) {
    pauseStudioAudio(null);
    currentSectionId = sectionId;

    // Highlight section tab
    document.querySelectorAll('.studio-section-tab').forEach(function(t) {
      t.classList.remove('is-active');
    });
    var targetTab = document.getElementById('tab-sec-' + sectionId);
    if (targetTab) {
      targetTab.classList.add('is-active');
      var skillKey = targetTab.getAttribute('data-skill-key');
      if (skillKey) {
        document.querySelectorAll('.studio-skill-tab').forEach(function(sTab) {
          if (sTab.getAttribute('data-skill-key') === skillKey) sTab.classList.add('is-active');
          else sTab.classList.remove('is-active');
        });
      }
    }

    // Left Rail active state
    document.querySelectorAll('.rail-section__header').forEach(function(h) {
      h.classList.remove('is-active');
    });
    var targetSecHeader = document.querySelector('.rail-section[data-section-id="' + sectionId + '"] .rail-section__header');
    if (targetSecHeader) targetSecHeader.classList.add('is-active');

    // Workstation panel display
    document.querySelectorAll('.section-workstation-content').forEach(function(content) {
      content.style.display = 'none';
    });
    var secContent = document.getElementById('ws-sec-' + sectionId);
    if (secContent) {
      secContent.style.display = 'block';

      // Check unit count for unit selector
      var uCount = parseInt(secContent.getAttribute('data-unit-count') || '1', 10);
      var uContainer = document.getElementById('ws-unit-selector-container');
      if (uContainer) uContainer.style.display = uCount > 1 ? 'block' : 'none';
    }

    var secObj = config.Sections;
    var match = secObj.find(function(s) { return s.SectionId === sectionId; });
    if (match) {
      document.getElementById('ws-section-name').textContent = match.Name;
      document.getElementById('ws-section-modality').textContent = match.Modality;
    }
  };

  window.filterWorkstationUnit = function(unitNum) {
    var secContent = document.getElementById('ws-sec-' + currentSectionId);
    if (!secContent) return;
    var targetUnit = parseInt(unitNum, 10);
    secContent.querySelectorAll('.card-editor').forEach(function(card) {
      var cardUnit = parseInt(card.getAttribute('data-unit-number') || '1', 10);
      if (targetUnit === 0 || cardUnit === targetUnit) {
        card.style.display = 'block';
      } else {
        card.style.display = 'none';
      }
    });
  };

  // --- Adding Questions and Clusters ---
  document.getElementById('btn-add-question')?.addEventListener('click', function() {
    createInitialCluster(currentSectionId, 'single', 1);
  });

  document.getElementById('btn-add-cluster')?.addEventListener('click', function() {
    var secContent = document.getElementById('ws-sec-' + currentSectionId);
    var count = secContent ? parseInt(secContent.getAttribute('data-cluster-size') || '1', 10) : 3;
    createInitialCluster(currentSectionId, 'cluster', count);
  });

  window.createInitialCluster = function(secId, mode, customCount) {
    var count = customCount || (mode === 'cluster' ? 3 : 1);
    var uFilter = document.getElementById('ws-unit-filter');
    var unitNum = uFilter && parseInt(uFilter.value, 10) > 0 ? parseInt(uFilter.value, 10) : 1;

    markSaving();
    fetch('/admin/exams/' + examId + '/clusters', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      },
      body: JSON.stringify({
        sectionId: secId,
        questionCount: count,
        unitNumber: unitNum
      })
    })
    .then(function(res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    })
    .then(function() {
      markSaved();
      window.location.reload();
    })
    .catch(function(err) {
      markSaveFailed('Add failed: ' + err.message);
    });
  };

  window.deleteStudioGroup = function(groupId, secId) {
    var ok = confirm('Delete this group and all its questions?');
    if (!ok) return;

    markSaving();
    fetch('/admin/exams/' + examId + '/studio/group/' + groupId, {
      method: 'DELETE',
      headers: {
        'RequestVerificationToken': getCsrfToken()
      }
    })
    .then(function(res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    })
    .then(function() {
      markSaved();
      var el = document.getElementById('group-card-' + groupId);
      if (el) el.remove();
      refreshAudit();
      window.location.reload();
    })
    .catch(function(err) {
      markSaveFailed('Delete group failed: ' + err.message);
    });
  };

  window.deleteStudioQuestion = function(questionId, secId) {
    var ok = confirm('Delete this question?');
    if (!ok) return;

    markSaving();
    fetch('/admin/exams/' + examId + '/studio/question/' + questionId, {
      method: 'DELETE',
      headers: {
        'RequestVerificationToken': getCsrfToken()
      }
    })
    .then(function(res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    })
    .then(function() {
      markSaved();
      var el = document.getElementById('question-card-' + questionId);
      if (el) el.remove();
      var railEl = document.getElementById('rail-item-' + questionId);
      if (railEl) railEl.remove();
      refreshAudit();
    })
    .catch(function(err) {
      markSaveFailed('Delete question failed: ' + err.message);
    });
  };

  window.focusQuestionCard = function(questionId) {
    pauseStudioAudio(null);
    var card = document.getElementById('question-card-' + questionId);
    if (!card) return;

    var grpCard = card.closest('.card-editor');
    if (grpCard) {
      var secId = parseInt(grpCard.getAttribute('data-section-id'), 10);
      if (secId && secId !== currentSectionId) {
        selectSection(secId);
      }
    }

    document.querySelectorAll('.rail-item').forEach(function(item) {
      item.classList.remove('is-selected');
    });
    var railItem = document.getElementById('rail-item-' + questionId);
    if (railItem) railItem.classList.add('is-selected');

    document.querySelectorAll('.card-editor, .question-card').forEach(function(c) {
      c.classList.remove('is-highlighted');
    });
    card.classList.add('is-highlighted');
    card.scrollIntoView({ behavior: 'smooth', block: 'center' });

    var input = card.querySelector('[data-field="Stem"]');
    if (input) input.focus();
  };

  // --- Reordering via Up/Down buttons ---
  window.moveItemUp = function(sectionId, questionId) {
    reorderRailItem(sectionId, questionId, -1);
  };

  window.moveItemDown = function(sectionId, questionId) {
    reorderRailItem(sectionId, questionId, 1);
  };

  function reorderRailItem(sectionId, questionId, direction) {
    var list = document.getElementById('rail-items-' + sectionId);
    if (!list) return;

    var items = Array.from(list.children);
    var idx = items.findIndex(function(it) {
      return parseInt(it.getAttribute('data-question-id'), 10) === questionId;
    });

    if (idx === -1) return;
    var targetIdx = idx + direction;
    if (targetIdx < 0 || targetIdx >= items.length) return;

    var currentEl = items[idx];
    var targetEl = items[targetIdx];

    if (direction < 0) {
      list.insertBefore(currentEl, targetEl);
    } else {
      list.insertBefore(targetEl, currentEl);
    }

    var reorderedList = Array.from(list.children);
    var payloadItems = [];

    reorderedList.forEach(function(el, newIdx) {
      var qId = parseInt(el.getAttribute('data-question-id'), 10);
      var gId = parseInt(el.getAttribute('data-group-id'), 10);
      var newOrder = newIdx + 1;
      el.setAttribute('data-display-order', newOrder);
      var labelEl = el.querySelector('strong');
      if (labelEl) labelEl.textContent = '#' + newOrder;

      payloadItems.push({
        groupId: gId,
        questionId: qId,
        displayOrder: newOrder
      });
    });

    markSaving();
    fetch('/admin/exams/' + examId + '/reorder', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      },
      body: JSON.stringify({ sectionId: sectionId, items: payloadItems })
    })
    .then(function(res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    })
    .then(function() {
      markSaved();
    })
    .catch(function(err) {
      markSaveFailed('Reorder failed: ' + err.message);
    });
  }

  // --- Audit Placard & Jumping ---
  window.jumpToAuditElement = function(targetId) {
    pauseStudioAudio(null);
    var el = document.getElementById(targetId);

    if (targetId.startsWith('section-link-')) {
      var secId = parseInt(targetId.replace('section-link-', ''), 10);
      selectSection(secId);
      return;
    }

    if (!el) {
      // Check if it's a question card inside another section
      if (targetId.startsWith('question-card-')) {
        var qId = parseInt(targetId.replace('question-card-', ''), 10);
        focusQuestionCard(qId);
      }
      return;
    }

    var grpCard = el.closest('.card-editor');
    if (grpCard) {
      var secId = parseInt(grpCard.getAttribute('data-section-id'), 10);
      if (secId && secId !== currentSectionId) {
        selectSection(secId);
      }
    }

    document.querySelectorAll('.card-editor, .question-card').forEach(function(c) {
      c.classList.remove('is-highlighted');
    });
    el.classList.add('is-highlighted');
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
  };

  function refreshAudit() {
    fetch('/admin/exams/' + examId + '/audit')
      .then(function(res) { return res.json(); })
      .then(function(audit) {
        var statusBadge = document.getElementById('audit-status-badge');
        var qCount = document.getElementById('audit-q-count');
        var errCount = document.getElementById('audit-error-count');
        var warnCount = document.getElementById('audit-warn-count');
        var pubBtn = document.getElementById('btn-publish-exam');
        var issuesList = document.getElementById('audit-issues-list');

        if (qCount) qCount.textContent = audit.totalQuestions + ' / ' + audit.expectedQuestions;
        if (errCount) {
          errCount.textContent = audit.blockingErrorCount;
          errCount.style.color = audit.blockingErrorCount > 0 ? '#b31217' : '#0a6b3c';
        }
        if (warnCount) warnCount.textContent = audit.warningCount;

        if (audit.canPublish) {
          if (statusBadge) {
            statusBadge.textContent = 'PASSED';
            statusBadge.style.background = '#e6f1ea';
            statusBadge.style.color = '#0a6b3c';
          }
          if (pubBtn) pubBtn.disabled = false;
        } else {
          if (statusBadge) {
            statusBadge.textContent = audit.blockingErrorCount + ' BLOCKS';
            statusBadge.style.background = '#fbe9e9';
            statusBadge.style.color = '#b31217';
          }
          if (pubBtn) pubBtn.disabled = true;
        }

        if (issuesList) {
          issuesList.innerHTML = '';
          if (audit.issues.length === 0 && audit.canPublish) {
            issuesList.innerHTML = '<li class="audit-item audit-item--success"><span>&#10004;</span><div>All certificate invariants satisfied. Ready to publish!</div></li>';
          } else {
            audit.issues.forEach(function(iss) {
              var li = document.createElement('li');
              li.className = 'audit-item ' + (iss.severity === 'error' ? 'audit-item--error' : 'audit-item--warning');
              li.id = 'audit-issue-' + iss.code;
              var jumpHtml = iss.elementTarget ? '<span class="audit-jump-link" onclick="jumpToAuditElement(\'' + iss.elementTarget + '\')">Jump to issue &rarr;</span>' : '';
              li.innerHTML = '<span>' + (iss.severity === 'error' ? '&#10008;' : '&#9888;') + '</span><div><div>' + iss.message + '</div>' + jumpHtml + '</div>';
              issuesList.appendChild(li);
            });
          }
        }
      })
      .catch(function(err) {
        console.warn('Audit refresh failed:', err);
      });
  }

  // --- Publish Exam Handler ---
  document.getElementById('btn-publish-exam')?.addEventListener('click', function() {
    var btn = this;
    if (btn.disabled) return;

    var ok = confirm('Publish this exam now? Once published, learners will be able to take it.');
    if (!ok) return;

    btn.disabled = true;
    fetch('/admin/exams/' + examId + '/publish', {
      method: 'POST',
      headers: {
        'Accept': 'application/json',
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      }
    })
    .then(function(res) {
      return res.json().then(function(data) {
        if (!res.ok || data.success === false) {
          throw new Error(data.message || ('HTTP ' + res.status));
        }
        return data;
      });
    })
    .then(function() {
      alert('Exam published successfully!');
      window.location.reload();
    })
    .catch(function(err) {
      alert('Publish failed: ' + err.message);
      btn.disabled = false;
    });
  });

  // --- Quick Ingest Integration ---
  var sectionsData = config.IngestSections;

  document.getElementById('btn-studio-quick-ingest')?.addEventListener('click', function() {
    window.QuickIngestModal.open(examId, sectionsData, currentSectionId, function() {
      window.location.reload();
    });
  });

  window.triggerQuickIngestForSection = function(secId) {
    window.QuickIngestModal.open(examId, sectionsData, secId, function() {
      window.location.reload();
    });
  };

  window.aiFillSection = function(secId) {
    var btn = document.getElementById('btn-ai-fill-' + secId) || document.getElementById('btn-ai-fill-empty-' + secId);
    var ok = confirm('Generate questions for missing slots in this section using Claude AI based on section blueprint?');
    if (!ok) return;

    if (btn) {
      btn.disabled = true;
      btn.textContent = 'Generating with AI...';
    }

    fetch('/admin/exams/' + examId + '/sections/' + secId + '/ai-generate', {
      method: 'POST',
      headers: {
        'Accept': 'application/json',
        'Content-Type': 'application/json',
        'RequestVerificationToken': getCsrfToken()
      },
      body: JSON.stringify({})
    })
    .then(function(res) {
      return res.json().then(function(data) {
        if (!res.ok) throw new Error(data.message || ('HTTP ' + res.status));
        return data;
      });
    })
    .then(function(data) {
      alert(data.message || 'Questions generated successfully!');
      window.location.reload();
    })
    .catch(function(err) {
      alert('AI generation failed: ' + err.message);
      if (btn) {
        btn.disabled = false;
        btn.textContent = 'AI Fill Missing Slots';
      }
    });
  };

  // Auto-select section and focus question if passed in URL query
  try {
    var urlParams = new URLSearchParams(window.location.search);
    var paramSecId = parseInt(urlParams.get('sectionId'), 10);
    if (paramSecId && !isNaN(paramSecId)) {
      window.selectSection(paramSecId);
    }
    var paramQId = parseInt(urlParams.get('questionId'), 10);
    if (paramQId && !isNaN(paramQId)) {
      setTimeout(function() {
        if (typeof window.focusQuestionCard === 'function') {
          window.focusQuestionCard(paramQId);
        }
      }, 50);
    }
  } catch (e) {
    // Graceful fallback
  }
})();
