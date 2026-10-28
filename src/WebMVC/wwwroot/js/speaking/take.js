(function() {
  const data = document.getElementById('practice-data');
  if (!data) return;
  const attemptId = Number(data.dataset.attemptId);
  const questionId = Number(data.dataset.questionId);
  const prepSecondsTotal = Number(data.dataset.prepSeconds);
  const speakSecondsTotal = Number(data.dataset.responseSeconds);
  let remainingTestSeconds = Number(data.dataset.remainingSeconds);
  const hasExistingAudio = data.dataset.hasAudio === 'true';
  const existingAudioUrl = '/speaking/audio/' + attemptId + '/' + questionId;

  // Elements
  const blockPrepare = document.getElementById('block-prepare');
  const blockRecording = document.getElementById('block-recording');
  const blockRecorded = document.getElementById('block-recorded');
  const blockSaving = document.getElementById('block-saving');
  const saveFailedBanner = document.getElementById('save-failed-banner');

  const btnStartNow = document.getElementById('btn-start-now');
  const btnStopNow = document.getElementById('btn-stop-now');
  const btnSaveContinue = document.getElementById('btn-save-continue');
  const btnRecordAgain = document.getElementById('btn-record-again');
  const btnRetryUpload = document.getElementById('btn-retry-upload');
  const recordAgainHint = document.getElementById('record-again-hint');

  const prepTimerEl = document.getElementById('prep-timer');
  const prepProgressEl = document.getElementById('prep-progress');
  const speakTimerEl = document.getElementById('speak-timer');
  const speakProgressEl = document.getElementById('speak-progress');
  const recordedTitleEl = document.getElementById('recorded-title');
  const recordedDescEl = document.getElementById('recorded-desc');
  const audioPlayer = document.getElementById('audio-player');
  const testTimerVal = document.getElementById('test-timer-val');

  let prepCountdown = prepSecondsTotal;
  let speakCountdown = speakSecondsTotal;
  let prepInterval = null;
  let speakInterval = null;
  let mediaRecorder = null;
  let audioChunks = [];
  let recordedBlob = null;
  let recordedDurationMs = 0;
  let recordingStartTime = 0;

  // Format MM:SS
  function formatTime(sec) {
    const m = Math.floor(sec / 60);
    const s = sec % 60;
    return (m < 10 ? '0' : '') + m + ':' + (s < 10 ? '0' : '') + s;
  }

  // Update whole-exam timer
  function updateTestTimer() {
    testTimerVal.textContent = formatTime(remainingTestSeconds);
    if (remainingTestSeconds > 0) {
      remainingTestSeconds--;
    } else {
      alert("Exam time has expired. Submitting your saved answers...");
      document.querySelector('form[action^="/speaking/submit/"]').submit();
    }
  }
  setInterval(updateTestTimer, 1000);
  updateTestTimer();

  function showState(state) {
    blockPrepare.classList.add('d-none');
    blockRecording.classList.add('d-none');
    blockRecorded.classList.add('d-none');
    blockSaving.classList.add('d-none');

    btnStartNow.classList.add('d-none');
    btnStopNow.classList.add('d-none');
    btnSaveContinue.classList.add('d-none');
    btnRecordAgain.classList.add('d-none');
    btnRetryUpload.classList.add('d-none');
    recordAgainHint.style.display = 'none';

    if (state === 'prepare') {
      blockPrepare.classList.remove('d-none');
      btnStartNow.classList.remove('d-none');
    } else if (state === 'recording') {
      blockRecording.classList.remove('d-none');
      btnStopNow.classList.remove('d-none');
    } else if (state === 'recorded') {
      blockRecorded.classList.remove('d-none');
      btnSaveContinue.classList.remove('d-none');
      btnRecordAgain.classList.remove('d-none');
      recordAgainHint.style.display = 'block';
    } else if (state === 'saving') {
      blockSaving.classList.remove('d-none');
    } else if (state === 'save-failed') {
      blockRecorded.classList.remove('d-none');
      btnRetryUpload.classList.remove('d-none');
      btnRecordAgain.classList.remove('d-none');
      saveFailedBanner.classList.remove('d-none');
      recordAgainHint.style.display = 'block';
    }
  }

  // If question already had saved audio, show recorded state directly
  if (hasExistingAudio) {
    recordedTitleEl.textContent = 'Existing saved answer';
    recordedDescEl.textContent = 'You previously saved an answer for this question. You can listen to it or record again.';
    audioPlayer.src = existingAudioUrl;
    showState('recorded');
  } else {
    startPreparation();
  }

  function startPreparation() {
    showState('prepare');
    prepCountdown = prepSecondsTotal;
    updatePrepUI();

    clearInterval(prepInterval);
    prepInterval = setInterval(() => {
      prepCountdown--;
      updatePrepUI();
      if (prepCountdown <= 0) {
        clearInterval(prepInterval);
        startRecording();
      }
    }, 1000);
  }

  function updatePrepUI() {
    prepTimerEl.textContent = '00:' + (prepCountdown < 10 ? '0' : '') + prepCountdown;
    const pct = ((prepSecondsTotal - prepCountdown) / prepSecondsTotal) * 100;
    prepProgressEl.style.width = pct + '%';
  }

  async function startRecording() {
    clearInterval(prepInterval);
    audioChunks = [];
    showState('recording');
    speakCountdown = speakSecondsTotal;
    updateSpeakUI();

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: window.EeAudio ? window.EeAudio.inputConstraints() : true
      });
      mediaRecorder = new MediaRecorder(stream);
      mediaRecorder.ondataavailable = e => { if (e.data.size > 0) audioChunks.push(e.data); };
      mediaRecorder.onstop = () => {
        recordedDurationMs = Date.now() - recordingStartTime;
        recordedBlob = new Blob(audioChunks, { type: 'audio/webm' });
        audioPlayer.src = URL.createObjectURL(recordedBlob);
        recordedTitleEl.textContent = 'You spoke for ' + Math.round(recordedDurationMs / 1000) + ' of ' + speakSecondsTotal + ' seconds';
        recordedDescEl.textContent = 'Your recording is ready on this device. Save it to proceed, or record again.';
        stream.getTracks().forEach(t => t.stop());
        showState('recorded');
      };

      recordingStartTime = Date.now();
      mediaRecorder.start();

      clearInterval(speakInterval);
      speakInterval = setInterval(() => {
        speakCountdown--;
        updateSpeakUI();
        if (speakCountdown <= 0) {
          stopRecording();
        }
      }, 1000);
    } catch (err) {
      alert("Microphone permission was not granted or microphone is not available. Please allow microphone access.");
      showState('prepare');
    }
  }

  function updateSpeakUI() {
    speakTimerEl.textContent = '00:' + (speakCountdown < 10 ? '0' : '') + speakCountdown;
    const pct = ((speakSecondsTotal - speakCountdown) / speakSecondsTotal) * 100;
    speakProgressEl.style.width = pct + '%';
  }

  function stopRecording() {
    clearInterval(speakInterval);
    if (mediaRecorder && mediaRecorder.state === 'recording') {
      mediaRecorder.stop();
    }
  }

  btnStartNow.addEventListener('click', () => {
    clearInterval(prepInterval);
    startRecording();
  });

  btnStopNow.addEventListener('click', () => {
    stopRecording();
  });

  btnRecordAgain.addEventListener('click', () => {
    if (confirm("Recording again replaces this take. Proceed?")) {
      saveFailedBanner.classList.add('d-none');
      startPreparation();
    }
  });

  async function uploadRecording() {
    if (!recordedBlob) {
      // If user had existing audio and just clicked save, advance to next question
      advanceToNextQuestion();
      return;
    }

    showState('saving');
    saveFailedBanner.classList.add('d-none');

    const token = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]')?.value || '';
    const formData = new FormData();
    formData.append('attemptId', attemptId);
    formData.append('questionId', questionId);
    formData.append('audioFile', recordedBlob, 'recording.webm');
    formData.append('durationMs', Math.round(recordedDurationMs));
    formData.append('__RequestVerificationToken', token);

    try {
      const response = await fetch('/speaking/upload-audio', {
        method: 'POST',
        body: formData
      });

      if (response.ok) {
        advanceToNextQuestion();
      } else {
        showState('save-failed');
      }
    } catch (err) {
      showState('save-failed');
    }
  }

  function advanceToNextQuestion() {
    // Find next question from navigator
    const currentCell = document.querySelector('.navigator__cell.is-current');
    let nextLink = null;
    if (currentCell) {
      const allCells = Array.from(document.querySelectorAll('.navigator__cell:not(.navigator__cell--swatch)'));
      const idx = allCells.indexOf(currentCell);
      if (idx !== -1 && idx < allCells.length - 1) {
        nextLink = allCells[idx + 1].getAttribute('href');
      }
    }

    if (nextLink) {
      window.location.href = nextLink;
    } else {
      // All done or on last question
      alert("All questions recorded! You can now finish and submit your attempt.");
      window.location.reload();
    }
  }

  const submitModal = document.getElementById('submit-modal');
  const btnOpenSubmit = document.getElementById('btn-open-submit-modal');
  const closeSubmitBtns = document.querySelectorAll('.js-close-submit-modal');

  function openModal() {
    if (submitModal) {
      submitModal.classList.add('modal-overlay--open');
      submitModal.setAttribute('aria-hidden', 'false');
    }
  }

  function closeModal() {
    if (submitModal) {
      submitModal.classList.remove('modal-overlay--open');
      submitModal.setAttribute('aria-hidden', 'true');
    }
  }

  if (btnOpenSubmit) {
    btnOpenSubmit.addEventListener('click', openModal);
  }
  closeSubmitBtns.forEach(btn => {
    btn.addEventListener('click', closeModal);
  });
  if (submitModal) {
    submitModal.addEventListener('click', (e) => {
      if (e.target === submitModal) closeModal();
    });
  }
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && submitModal && submitModal.classList.contains('modal-overlay--open')) {
      closeModal();
    }
  });

  btnSaveContinue.addEventListener('click', uploadRecording);
  btnRetryUpload.addEventListener('click', uploadRecording);
})();