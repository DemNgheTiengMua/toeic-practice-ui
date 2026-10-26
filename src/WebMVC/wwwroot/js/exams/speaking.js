(function (sitting) {
  "use strict";

  sitting.isSystemPacedAuto = function (q) {
    return !!(q && (q.navigationMode === 'SYSTEM_PACED_AUTO' || q.navigationMode === 'SystemPacedAuto'));
  };
  sitting.playBeepCue = function () {
    try {
      var AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (!AudioCtx) return;
      var ctx = new AudioCtx();
      var osc = ctx.createOscillator();
      var gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.setValueAtTime(1000, ctx.currentTime);
      gain.gain.setValueAtTime(0.3, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.3);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.3);
    } catch (e) {
      console.warn('AudioContext beep cue failed:', e);
    }
  };
  sitting.cleanupSpeakingPacer = function () {
    if (sitting.speakingPacerTimer) {
      clearInterval(sitting.speakingPacerTimer);
      sitting.speakingPacerTimer = null;
    }
    if (sitting.speakingPacerAnimFrame) {
      cancelAnimationFrame(sitting.speakingPacerAnimFrame);
      sitting.speakingPacerAnimFrame = null;
    }
    if (sitting.speakingPacerAudioCtx && sitting.speakingPacerAudioCtx.state !== 'closed') {
      try { sitting.speakingPacerAudioCtx.close(); } catch (e) {}
      sitting.speakingPacerAudioCtx = null;
    }
    if (sitting.activeRecorder && sitting.activeRecorder.state !== 'inactive') {
      try { sitting.activeRecorder.stop(); } catch (e) {}
      sitting.activeRecorder = null;
    }
    if (sitting.activeRecordingStream) {
      try {
        sitting.activeRecordingStream.getTracks().forEach(function (t) { t.stop(); });
      } catch (e) {}
      sitting.activeRecordingStream = null;
    }
    sitting.isSpeakingPreparing = false;
    sitting.isSpeakingRecording = false;
    sitting.isAudioUploading = false;
  };
  sitting.startSpeakingPacerStateMachine = function (q) {
    sitting.cleanupSpeakingPacer();

    var pacerContainer = document.getElementById('speaking-pacer-container');
    var prepPlacard = document.getElementById('speaking-prep-placard');
    var prepTimer = document.getElementById('speaking-prep-timer');
    var prepBar = document.getElementById('speaking-prep-bar');
    var prepBarBox = document.getElementById('speaking-prep-bar-box');
    var recordPlacard = document.getElementById('speaking-record-placard');
    var recordTimer = document.getElementById('speaking-record-timer');
    var recordBar = document.getElementById('speaking-record-bar');
    var recordBarBox = document.getElementById('speaking-record-bar-box');
    var levelContainer = document.getElementById('audio-level-container');
    var levelBar = document.getElementById('audio-level-bar');
    var levelText = document.getElementById('audio-level-text');
    var uploadPlacard = document.getElementById('speaking-upload-placard');

    if (!pacerContainer) return;
    pacerContainer.style.display = 'block';

    var totalPrep = (q.prepSeconds != null && q.prepSeconds > 0) ? q.prepSeconds : 15;
    var totalResponse = (q.responseSeconds != null && q.responseSeconds > 0) ? q.responseSeconds : 30;

    // Step 1: Prep Countdown
    sitting.isSpeakingPreparing = true;
    sitting.isSpeakingRecording = false;
    sitting.isAudioUploading = false;

    if (prepPlacard) prepPlacard.style.display = 'flex';
    if (prepBarBox) prepBarBox.style.display = 'block';
    if (prepTimer) prepTimer.textContent = totalPrep + 's';
    if (prepBar) prepBar.style.width = '100%';

    if (recordPlacard) recordPlacard.style.display = 'none';
    if (recordBarBox) recordBarBox.style.display = 'none';
    if (levelContainer) levelContainer.style.display = 'none';
    if (uploadPlacard) uploadPlacard.style.display = 'none';

    var remainingPrep = totalPrep;
    sitting.speakingPacerTimer = setInterval(function () {
      remainingPrep--;
      if (prepTimer) prepTimer.textContent = Math.max(0, remainingPrep) + 's';
      if (prepBar) prepBar.style.width = Math.max(0, (remainingPrep / totalPrep) * 100) + '%';

      if (remainingPrep <= 0) {
        clearInterval(sitting.speakingPacerTimer);
        sitting.speakingPacerTimer = null;
        sitting.isSpeakingPreparing = false;

        // Step 2: Audible Beep Cue
        sitting.playBeepCue();

        // Step 3: Recording Countdown
        sitting.startSpeakingPacerRecording(q, totalResponse);
      }
    }, 1000);
  };
  sitting.startSpeakingPacerRecording = function (q, totalResponse) {
    var prepPlacard = document.getElementById('speaking-prep-placard');
    var prepBarBox = document.getElementById('speaking-prep-bar-box');
    var recordPlacard = document.getElementById('speaking-record-placard');
    var recordTimer = document.getElementById('speaking-record-timer');
    var recordBar = document.getElementById('speaking-record-bar');
    var recordBarBox = document.getElementById('speaking-record-bar-box');
    var levelContainer = document.getElementById('audio-level-container');
    var levelBar = document.getElementById('audio-level-bar');
    var levelText = document.getElementById('audio-level-text');

    sitting.isSpeakingRecording = true;
    if (prepPlacard) prepPlacard.style.display = 'none';
    if (prepBarBox) prepBarBox.style.display = 'none';

    if (recordPlacard) recordPlacard.style.display = 'flex';
    if (recordBarBox) recordBarBox.style.display = 'block';
    if (recordTimer) recordTimer.textContent = totalResponse + 's';
    if (recordBar) recordBar.style.width = '100%';
    if (levelContainer) levelContainer.style.display = 'block';

    sitting.activeRecordingStartedAt = Date.now();
    sitting.activeAudioChunks = [];

    var streamPromise = (navigator.mediaDevices && navigator.mediaDevices.getUserMedia)
      ? navigator.mediaDevices.getUserMedia({ audio: true })
      : Promise.reject(new Error('MediaDevices unavailable'));

    streamPromise.then(function (stream) {
      sitting.activeRecordingStream = stream;
      try {
        var AudioCtx = window.AudioContext || window.webkitAudioContext;
        if (AudioCtx) {
          sitting.speakingPacerAudioCtx = new AudioCtx();
          var srcNode = sitting.speakingPacerAudioCtx.createMediaStreamSource(stream);
          sitting.speakingPacerAnalyser = sitting.speakingPacerAudioCtx.createAnalyser();
          sitting.speakingPacerAnalyser.fftSize = 256;
          srcNode.connect(sitting.speakingPacerAnalyser);
          var freqArr = new Uint8Array(sitting.speakingPacerAnalyser.frequencyBinCount);

          function pumpLevel() {
            if (!sitting.isSpeakingRecording || !sitting.speakingPacerAnalyser) return;
            sitting.speakingPacerAnalyser.getByteFrequencyData(freqArr);
            var total = 0;
            for (var i = 0; i < freqArr.length; i++) total += freqArr[i];
            var pct = Math.min(100, Math.round(((total / freqArr.length) / 128) * 100));
            if (levelBar) levelBar.style.width = pct + '%';
            if (levelText) levelText.textContent = pct + '%';
            sitting.speakingPacerAnimFrame = requestAnimationFrame(pumpLevel);
          }
          sitting.speakingPacerAnimFrame = requestAnimationFrame(pumpLevel);
        }
      } catch (e) {
        console.warn('Audio level meter init failed:', e);
      }

      if (typeof MediaRecorder !== 'undefined') {
        sitting.activeRecorder = new MediaRecorder(stream);
        sitting.activeRecorder.ondataavailable = function (evt) {
          if (evt.data && evt.data.size > 0) sitting.activeAudioChunks.push(evt.data);
        };
        sitting.activeRecorder.onstop = function () {
          sitting.finishSpeakingPacerRecording(q);
        };
        sitting.activeRecorder.start();
      }
    }).catch(function (err) {
      console.warn('Microphone error in speaking pacer:', err);
    });

    var remainingRec = totalResponse;
    sitting.speakingPacerTimer = setInterval(function () {
      remainingRec--;
      if (recordTimer) recordTimer.textContent = Math.max(0, remainingRec) + 's';
      if (recordBar) recordBar.style.width = Math.max(0, (remainingRec / totalResponse) * 100) + '%';

      if (remainingRec <= 0) {
        clearInterval(sitting.speakingPacerTimer);
        sitting.speakingPacerTimer = null;
        // Step 4: Stop Recording and Advance
        sitting.stopSpeakingPacerRecording(q);
      }
    }, 1000);
  };
  sitting.stopSpeakingPacerRecording = function (q) {
    sitting.isSpeakingRecording = false;
    if (sitting.speakingPacerAnimFrame) {
      cancelAnimationFrame(sitting.speakingPacerAnimFrame);
      sitting.speakingPacerAnimFrame = null;
    }
    if (sitting.speakingPacerAudioCtx && sitting.speakingPacerAudioCtx.state !== 'closed') {
      try { sitting.speakingPacerAudioCtx.close(); } catch (e) {}
      sitting.speakingPacerAudioCtx = null;
    }
    if (sitting.activeRecorder && sitting.activeRecorder.state !== 'inactive') {
      sitting.activeRecorder.stop();
    } else {
      sitting.finishSpeakingPacerRecording(q);
    }
  };
  sitting.finishSpeakingPacerRecording = function (q) {
    if (sitting.activeRecordingStream) {
      try { sitting.activeRecordingStream.getTracks().forEach(function (t) { t.stop(); }); } catch (e) {}
      sitting.activeRecordingStream = null;
    }

    var uploadPlacard = document.getElementById('speaking-upload-placard');
    if (uploadPlacard) uploadPlacard.style.display = 'block';
    sitting.isAudioUploading = true;

    var blob = sitting.activeAudioChunks.length > 0
      ? new Blob(sitting.activeAudioChunks, { type: sitting.activeAudioChunks[0].type || 'audio/webm' })
      : new Blob([new Uint8Array(50)], { type: 'audio/webm' });

    var blobUrl = URL.createObjectURL(blob);
    var durationMs = Math.max(1000, Date.now() - (sitting.activeRecordingStartedAt || (Date.now() - 1000)));

    sitting.recordedSpeakingClips[q.questionId] = {
      questionId: q.questionId,
      displayOrder: q.displayOrder,
      stem: q.stem,
      blobUrl: blobUrl,
      blob: blob,
      durationMs: durationMs,
      isRecorded: true
    };

    var formData = new FormData();
    formData.append('attemptId', sitting.attemptId);
    formData.append('questionId', q.questionId);
    formData.append('audioFile', blob, 'speaking-q' + q.questionId + '.webm');
    formData.append('durationMs', durationMs);
    formData.append('__RequestVerificationToken', sitting.csrfToken);

    fetch('/exams/take/' + sitting.attemptId + '/upload-audio', {
      method: 'POST',
      body: formData
    }).then(function (res) {
      if (!res.ok) {
        return fetch('/speaking/upload-audio', { method: 'POST', body: formData });
      }
      return res;
    }).then(function (res) {
      return res.json();
    }).then(function (data) {
      var state = sitting.localAnswers[q.questionId] || {};
      state.responseAudioPath = (data && data.audioPath) ? data.audioPath : blobUrl;
      state.responseDurationMs = durationMs;
      sitting.localAnswers[q.questionId] = state;
      sitting.saveAnswer(q.questionId, null, !!state.isFlagged, state.responseText || '');
      sitting.updateProgressStats();
    }).catch(function (err) {
      console.warn('Speaking upload error:', err);
      var state = sitting.localAnswers[q.questionId] || {};
      state.responseAudioPath = blobUrl;
      state.responseDurationMs = durationMs;
      sitting.localAnswers[q.questionId] = state;
      sitting.saveAnswer(q.questionId, null, !!state.isFlagged, state.responseText || '');
      sitting.updateProgressStats();
    }).finally(function () {
      sitting.isAudioUploading = false;
      sitting.activeRecorder = null;
      sitting.activeAudioChunks = [];
      sitting.transitionAfterSpeakingItem(q);
    });
  };
  sitting.transitionAfterSpeakingItem = function (q) {
    var sectionSpeakingQuestions = sitting.questions.filter(function (item) {
      return item.sectionId === q.sectionId && (item.navigationMode === 'SYSTEM_PACED_AUTO' || (item.modality || '').toLowerCase() === 'speaking');
    });
    if (sectionSpeakingQuestions.length === 0) {
      sectionSpeakingQuestions = sitting.questions.filter(function (item) {
        return item.navigationMode === 'SYSTEM_PACED_AUTO';
      });
    }

    var currentIdx = sectionSpeakingQuestions.findIndex(function (item) {
      return item.questionId === q.questionId;
    });

    var isLast = (currentIdx === -1) || (currentIdx >= sectionSpeakingQuestions.length - 1);

    if (!isLast) {
      var nextQ = sectionSpeakingQuestions[currentIdx + 1];
      sitting.goToQuestion(nextQ.displayOrder);
    } else {
      sitting.renderSpeakingVerificationScreen(sectionSpeakingQuestions);
    }
  };
  sitting.renderSpeakingVerificationScreen = function (speakingQuestions) {
    sitting.cleanupSpeakingPacer();

    var pacerContainer = document.getElementById('speaking-pacer-container');
    if (pacerContainer) pacerContainer.style.display = 'none';

    var verificationScreen = document.getElementById('speaking-verification-screen');
    var verificationItems = document.getElementById('speaking-verification-items');
    var countBadge = document.getElementById('speaking-verification-count-badge');
    if (!verificationScreen || !verificationItems) return;

    verificationScreen.style.display = 'block';

    if (sitting.btnPrev) sitting.btnPrev.style.display = 'none';
    if (sitting.btnNext) sitting.btnNext.style.display = 'none';

    var qs = speakingQuestions && speakingQuestions.length > 0
      ? speakingQuestions
      : sitting.questions.filter(function (it) { return it.navigationMode === 'SYSTEM_PACED_AUTO' || (it.modality || '').toLowerCase() === 'speaking'; });

    if (countBadge) countBadge.textContent = qs.length + ' items';

    var itemsHtml = '';
    qs.forEach(function (sq) {
      var clip = sitting.recordedSpeakingClips[sq.questionId];
      var ans = sitting.localAnswers[sq.questionId];
      var audioUrl = clip ? clip.blobUrl : (ans ? ans.responseAudioPath : null);
      var hasRecording = !!audioUrl;

      itemsHtml += '<div class="speaking-verification-card" data-question-id="' + sq.questionId + '">';
      itemsHtml += '<div class="row row--between mb-2" style="align-items: center;">';
      itemsHtml += '<strong style="font-family:\'IBM Plex Sans Condensed\',sans-serif; text-transform:uppercase; font-size:1rem;">Question ' + sq.displayOrder + ' / ' + sitting.totalQuestions + '</strong>';
      if (hasRecording) {
        itemsHtml += '<span class="badge status-badge--recorded">Recorded [DONE]</span>';
      } else {
        itemsHtml += '<span class="badge status-badge--missing">No recording [!]</span>';
      }
      itemsHtml += '</div>';

      itemsHtml += '<p class="text-sm text-muted mb-2">' + sitting.escapeHtml(sq.stem || 'Spoken response') + '</p>';

      if (hasRecording) {
        itemsHtml += '<audio controls src="' + sitting.escapeHtml(audioUrl) + '" style="width: 100%; border: 1px solid #14181d; border-radius: 0; background: #fff;"></audio>';
      } else {
        itemsHtml += '<div class="text-xs text-muted" style="font-style: italic; padding: 0.5rem; background: #fff; border: 1px dashed #ccd2d6;">No recording available for this question.</div>';
      }

      itemsHtml += '</div>';
    });

    verificationItems.innerHTML = itemsHtml;

    var btnSubmitVerification = document.getElementById('btn-submit-speaking-verification');
    if (btnSubmitVerification) {
      var maxSpeakingOrder = qs.reduce(function (max, it) { return Math.max(max, it.displayOrder); }, 0);
      var nextSectionQ = sitting.questions.find(function (it) { return it.displayOrder > maxSpeakingOrder; });

      if (nextSectionQ) {
        btnSubmitVerification.textContent = 'Continue to Next Phase →';
        btnSubmitVerification.className = 'btn btn--primary';
        btnSubmitVerification.onclick = function () {
          verificationScreen.style.display = 'none';
          var nextPhaseIdx = sitting.getQuestionPhaseIndex(nextSectionQ);
          if (nextPhaseIdx >= 0) sitting.activePhaseIndex = nextPhaseIdx;
          sitting.cleanupSpeakingPacer();
          fetch('/exams/take/' + sitting.attemptId + '/transition/skill', {
            method: 'POST',
            headers: {
              'RequestVerificationToken': sitting.csrfToken,
              'Content-Type': 'application/json'
            }
          }).finally(function () {
            sitting.goToQuestion(nextSectionQ.displayOrder);
          });
        };
      } else {
        btnSubmitVerification.textContent = 'Submit Speaking Phase for AI Grading';
        btnSubmitVerification.className = 'btn btn--danger';
        btnSubmitVerification.onclick = function () {
          btnSubmitVerification.disabled = true;
          btnSubmitVerification.textContent = 'Submitting...';
          var submitForm = document.getElementById('submit-exam-form') || document.getElementById('auto-submit-form');
          if (submitForm) {
            submitForm.submit();
          } else {
            var form = document.createElement('form');
            form.method = 'POST';
            form.action = '/exams/take/' + sitting.attemptId + '/submit';
            var tokenInput = document.createElement('input');
            tokenInput.type = 'hidden';
            tokenInput.name = '__RequestVerificationToken';
            tokenInput.value = sitting.csrfToken;
            form.appendChild(tokenInput);
            document.body.appendChild(form);
            form.submit();
          }
        };
      }
    }
  };
  sitting.renderFreeResponse = function (q) {
    var answer = sitting.localAnswers[q.questionId] || {};
    var isSpeaking = (q.modality || '').toLowerCase() === 'speaking';
    sitting.optionList.style.display = 'none';
    sitting.freeResponseContainer.style.display = 'block';

    if (!isSpeaking) {
      var minWords = Number(q.minWords) || 0;
      var maxWords = Number(q.maxWords) || 0;
      sitting.freeResponseContainer.innerHTML =
        '<label class="field__label" for="free-response-text">Written response</label>' +
        '<p class="text-sm text-muted">Word limit: ' + minWords + '–' + maxWords + ' words.</p>' +
        '<textarea id="free-response-text" class="field__input" rows="12" maxlength="20000" aria-describedby="free-response-word-count"></textarea>' +
        '<p id="free-response-word-count" class="text-sm text-muted" aria-live="polite"></p>';
      var responseInput = document.getElementById('free-response-text');
      var wordCount = document.getElementById('free-response-word-count');
      responseInput.value = answer.responseText || '';

      function updateWordCount() {
        var trimmed = responseInput.value.trim();
        var count = trimmed ? trimmed.split(/\s+/).length : 0;
        wordCount.textContent = count + ' words' +
          (minWords || maxWords ? ' · required ' + minWords + '–' + maxWords : '');
      }

      responseInput.addEventListener('input', function () {
        updateWordCount();
        sitting.saveAnswer(q.questionId, null, !!(sitting.localAnswers[q.questionId] && sitting.localAnswers[q.questionId].isFlagged), responseInput.value);
      });
      updateWordCount();
      return;
    }

    var existingAudio = answer.responseAudioPath;
    sitting.freeResponseContainer.innerHTML =
      '<p class="text-sm">Preparation time: ' + (Number(q.prepSeconds) || 0) + ' seconds · ' +
        'Response time: ' + (Number(q.responseSeconds) || 0) + ' seconds.</p>' +
      '<p id="speaking-recording-status" class="text-sm" aria-live="polite">' +
        (existingAudio ? 'A saved recording is attached. Record again to replace it.' : 'No recording saved yet.') +
      '</p>' +
      '<div class="row" style="gap:.5rem; flex-wrap:wrap;">' +
        '<button type="button" id="speaking-record-start" class="btn btn--primary">Start recording</button>' +
        '<button type="button" id="speaking-record-stop" class="btn" style="display:none;">Stop recording</button>' +
      '</div>';
    var startButton = document.getElementById('speaking-record-start');
    var stopButton = document.getElementById('speaking-record-stop');
    var status = document.getElementById('speaking-recording-status');

    startButton.addEventListener('click', function () {
      sitting.startMockSpeakingCapture(q, status, startButton, stopButton);
    });
    stopButton.addEventListener('click', function () {
      sitting.stopMockSpeakingCapture();
    });
  };
  sitting.startMockSpeakingCapture = async function (q, status, startButton, stopButton) {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || typeof MediaRecorder === 'undefined') {
      status.textContent = 'This browser does not support audio recording.';
      return;
    }

    startButton.disabled = true;
    sitting.isSpeakingPreparing = true;
    status.textContent = 'Requesting microphone access…';
    try {
      sitting.activeRecordingStream = await navigator.mediaDevices.getUserMedia({ audio: true });
      var prepSeconds = Number(q.prepSeconds) || 0;
      if (prepSeconds > 0) {
        var remainingPrep = prepSeconds;
        status.textContent = 'Preparation time: ' + remainingPrep + ' seconds.';
        await new Promise(function (resolve) {
          sitting.prepTimer = window.setInterval(function () {
            remainingPrep--;
            if (remainingPrep <= 0) {
              window.clearInterval(sitting.prepTimer);
              sitting.prepTimer = null;
              resolve();
            } else {
              status.textContent = 'Preparation time: ' + remainingPrep + ' seconds.';
            }
          }, 1000);
        });
      }

      sitting.isSpeakingPreparing = false;
      sitting.activeAudioChunks = [];
      sitting.activeRecorder = new MediaRecorder(sitting.activeRecordingStream);
      sitting.activeRecorder.addEventListener('dataavailable', function (event) {
        if (event.data && event.data.size > 0) sitting.activeAudioChunks.push(event.data);
      });
      sitting.activeRecorder.addEventListener('stop', function () {
        sitting.uploadMockSpeakingRecording(q, status, startButton, stopButton);
      }, { once: true });
      sitting.activeRecorder.start();
      sitting.activeRecordingStartedAt = Date.now();
      sitting.isSpeakingRecording = true;
      startButton.style.display = 'none';
      stopButton.style.display = '';
      status.textContent = 'Recording…';
      var responseSeconds = Number(q.responseSeconds) || 0;
      if (responseSeconds > 0) {
        sitting.activeAudioTimer = window.setTimeout(sitting.stopMockSpeakingCapture, responseSeconds * 1000);
      }
    } catch (error) {
      sitting.isSpeakingPreparing = false;
      if (sitting.activeRecordingStream) {
        sitting.activeRecordingStream.getTracks().forEach(function (track) { track.stop(); });
        sitting.activeRecordingStream = null;
      }
      if (sitting.prepTimer) {
        window.clearInterval(sitting.prepTimer);
        sitting.prepTimer = null;
      }
      startButton.disabled = false;
      status.textContent = 'Microphone access failed. Check browser permissions and try again.';
    }
  };
  sitting.stopMockSpeakingCapture = function () {
    if (sitting.activeAudioTimer) {
      window.clearTimeout(sitting.activeAudioTimer);
      sitting.activeAudioTimer = null;
    }
    if (sitting.prepTimer) {
      window.clearInterval(sitting.prepTimer);
      sitting.prepTimer = null;
    }
    if (sitting.activeRecorder && sitting.activeRecorder.state !== 'inactive') {
      sitting.activeRecorder.stop();
    }
  };
  sitting.uploadMockSpeakingRecording = async function (q, status, startButton, stopButton) {
    sitting.isSpeakingRecording = false;
    sitting.isAudioUploading = true;
    if (sitting.activeRecordingStream) {
      sitting.activeRecordingStream.getTracks().forEach(function (track) { track.stop(); });
      sitting.activeRecordingStream = null;
    }
    stopButton.style.display = 'none';
    status.textContent = 'Uploading recording…';
    var blob = new Blob(sitting.activeAudioChunks, { type: sitting.activeAudioChunks[0] ? sitting.activeAudioChunks[0].type : 'audio/webm' });
    var durationMs = Math.max(1, Date.now() - sitting.activeRecordingStartedAt);

    try {
      var tokenInput = document.querySelector('#anti-forgery-holder input[name="__RequestVerificationToken"]');
      var formData = new FormData();
      formData.append('attemptId', sitting.attemptId);
      formData.append('questionId', q.questionId);
      formData.append('audioFile', blob, 'recording.webm');
      formData.append('durationMs', durationMs);
      formData.append('__RequestVerificationToken', tokenInput ? tokenInput.value : '');
      var response = await fetch('/speaking/upload-audio', { method: 'POST', body: formData });
      if (!response.ok) {
        throw new Error('Audio upload failed (' + response.status + ').');
      }

      var state = sitting.localAnswers[q.questionId] || {};
      state.responseAudioPath = 'saved';
      state.responseDurationMs = durationMs;
      sitting.localAnswers[q.questionId] = state;
      status.textContent = 'Recording saved. Record again to replace it.';
      sitting.saveAnswer(q.questionId, null, !!state.isFlagged, state.responseText || '');
      sitting.updateProgressStats();
    } catch (error) {
      status.textContent = 'Recording could not be saved. Check your connection and record again.';
      startButton.style.display = '';
    } finally {
      sitting.isAudioUploading = false;
      startButton.disabled = false;
      sitting.activeRecorder = null;
      sitting.activeAudioChunks = [];
    }
  };

  sitting.initializeSpeaking = function () {
    sitting.activeRecorder = null;
    sitting.activeRecordingStream = null;
    sitting.activeAudioChunks = [];
    sitting.activeAudioTimer = null;
    sitting.prepTimer = null;
    sitting.activeRecordingStartedAt = 0;
    sitting.isSpeakingPreparing = false;
    sitting.isSpeakingRecording = false;
    sitting.isAudioUploading = false;
    sitting.speakingPacerTimer = null;
    sitting.speakingPacerAudioCtx = null;
    sitting.speakingPacerAnalyser = null;
    sitting.speakingPacerAnimFrame = null;
    sitting.recordedSpeakingClips = {};
  };
})(window.ExamSitting);
