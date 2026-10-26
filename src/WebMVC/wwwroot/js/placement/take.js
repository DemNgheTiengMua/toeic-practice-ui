(function () {
      const config = JSON.parse(document.getElementById('placement-take-data').textContent);
      const placementId = config.placementId;
      const questionId = config.questionId;
      let remainingSeconds = config.remainingSeconds;
      let isFlagged = config.isFlagged;
      const totalQuestions = config.totalQuestions;
      let answeredCount = config.answeredCount;

      const antiForgeryToken = document.querySelector('#anti-forgery-holder input[name="__RequestVerificationToken"]')?.value || '';
      const saveStatusEl = document.getElementById('save-status');
      const timerDisplayEl = document.getElementById('timer-display');
      const timerBoxEl = document.getElementById('timer-box');
      const flagBtn = document.getElementById('flag-btn');
      const progressBar = document.getElementById('progress-bar');
      const progressText = document.getElementById('progress-text');
      if (progressBar) progressBar.style.width = config.progressPercent + '%';
      const modalAnsweredCount = document.getElementById('modal-answered-count');
      const navCell = document.getElementById('nav-cell-' + questionId);

      // --- Writing Draft Management ---
      const writingEditor = document.getElementById('writing-editor');
      const wordCountEl = document.getElementById('word-count');
      const characterCountEl = document.getElementById('character-count');
      const saveDraftBtn = document.getElementById('btn-save-writing-draft');
      let draftSaveTimeout = null;
      let draftDirty = false;

      function updateWritingCounts() {
        if (!writingEditor) return;
        const text = writingEditor.value || '';
        const words = text.trim() ? text.trim().split(/\s+/).length : 0;
        if (wordCountEl) wordCountEl.textContent = words;
        if (characterCountEl) characterCountEl.textContent = text.length;
      }

      async function saveDraftNow() {
        if (!writingEditor) return true;
        if (draftSaveTimeout) clearTimeout(draftSaveTimeout);
        const text = writingEditor.value;
        if (saveStatusEl) {
          saveStatusEl.textContent = 'Saving draft…';
          saveStatusEl.className = 'save-status';
        }

        try {
          const res = await fetch(`/placement/take/${placementId}/draft`, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'RequestVerificationToken': antiForgeryToken
            },
            body: JSON.stringify({
              questionId: questionId,
              responseText: text
            })
          });

          if (!res.ok) {
            throw new Error('Save draft failed with status ' + res.status);
          }

          draftDirty = false;
          if (saveStatusEl) {
            saveStatusEl.textContent = 'Draft saved';
            saveStatusEl.className = 'save-status save-status--saved';
          }

          if (text.trim().length > 0 && navCell) {
            navCell.classList.add('is-answered');
            navCell.classList.remove('is-unanswered');
          }
          return true;
        } catch (err) {
          console.error(err);
          if (saveStatusEl) {
            saveStatusEl.textContent = 'Save error';
            saveStatusEl.className = 'save-status save-status--error';
          }
          return false;
        }
      }

      if (writingEditor) {
        updateWritingCounts();
        writingEditor.addEventListener('input', () => {
          draftDirty = true;
          updateWritingCounts();
          if (saveStatusEl) {
            saveStatusEl.textContent = 'Draft not saved';
            saveStatusEl.className = 'save-status';
          }
          if (draftSaveTimeout) clearTimeout(draftSaveTimeout);
          draftSaveTimeout = setTimeout(saveDraftNow, 3000);
        });
      }

      if (saveDraftBtn) {
        saveDraftBtn.addEventListener('click', () => {
          saveDraftNow();
        });
      }

      // Flush draft when clicking navigation links
      document.querySelectorAll('a[href^="/placement/take/"]').forEach(link => {
        link.addEventListener('click', async function (e) {
          if (draftDirty && writingEditor) {
            e.preventDefault();
            const href = this.getAttribute('href');
            await saveDraftNow();
            window.location.href = href;
          }
        });
      });

      // --- Countdown Timer ---
      function formatTime(totalSec) {
        if (totalSec <= 0) return '00:00';
        const mins = Math.floor(totalSec / 60);
        const secs = totalSec % 60;
        return String(mins).padStart(2, '0') + ':' + String(secs).padStart(2, '0');
      }

      function updateTimerDisplay() {
        if (!timerDisplayEl) return;
        timerDisplayEl.textContent = formatTime(remainingSeconds);

        if (remainingSeconds <= 120) {
          timerBoxEl?.classList.remove('timer--warning');
          timerBoxEl?.classList.add('timer--critical');
        } else if (remainingSeconds <= 300) {
          timerBoxEl?.classList.add('timer--warning');
          timerBoxEl?.classList.remove('timer--critical');
        } else {
          timerBoxEl?.classList.remove('timer--warning', 'timer--critical');
        }
      }

      updateTimerDisplay();

      const timerInterval = setInterval(async () => {
        remainingSeconds--;
        if (remainingSeconds <= 0) {
          clearInterval(timerInterval);
          updateTimerDisplay();
          // Auto submit when time runs out
          if (draftDirty && writingEditor) {
            await saveDraftNow();
          }
          const autoInput = document.getElementById('is-auto-submit-input');
          if (autoInput) autoInput.value = 'true';
          document.getElementById('submit-form')?.submit();
        } else {
          updateTimerDisplay();
        }
      }, 1000);

      // --- Save Answer via AJAX ---
      async function sendAnswer(selectedOptionId, flagged) {
        if (!saveStatusEl) return;
        saveStatusEl.textContent = 'Saving…';
        saveStatusEl.className = 'save-status';

        try {
          const body = new URLSearchParams();
          if (selectedOptionId !== null && selectedOptionId !== undefined) {
            body.append('selectedOptionId', selectedOptionId);
          }
          body.append('isFlagged', flagged ? 'true' : 'false');
          body.append('__RequestVerificationToken', antiForgeryToken);

          const res = await fetch(`/placement/take/${placementId}/answers/${questionId}`, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'RequestVerificationToken': antiForgeryToken
            },
            body: body.toString()
          });

          if (!res.ok) {
            throw new Error('Save answer failed with status ' + res.status);
          }

          const data = await res.json();
          if (data.redirectUrl) {
            window.location.href = data.redirectUrl;
            return;
          }

          saveStatusEl.textContent = 'Saved';
          saveStatusEl.className = 'save-status save-status--saved';

          // Update remaining seconds if returned
          if (typeof data.remainingSeconds === 'number') {
            remainingSeconds = data.remainingSeconds;
            updateTimerDisplay();
          }

          // Update navigator cell appearance
          if (navCell) {
            if (data.selectedOptionId !== null) {
              navCell.classList.add('is-answered');
              navCell.classList.remove('is-unanswered');
            }
            if (data.isFlagged) {
              navCell.classList.add('is-flagged');
            } else {
              navCell.classList.remove('is-flagged');
            }
          }
        } catch (err) {
          console.error(err);
          saveStatusEl.textContent = 'Save error';
          saveStatusEl.className = 'save-status save-status--error';
        }
      }

      // Option selection change
      const optionInputs = document.querySelectorAll('.js-option-radio');
      optionInputs.forEach(input => {
        input.addEventListener('change', function () {
          document.querySelectorAll('.option').forEach(l => l.classList.remove('is-selected'));
          this.closest('.option')?.classList.add('is-selected');

          const wasAnswered = navCell?.classList.contains('is-answered');
          if (!wasAnswered) {
            answeredCount++;
            if (progressText) progressText.textContent = `${answeredCount}/${totalQuestions} answered`;
            if (modalAnsweredCount) modalAnsweredCount.textContent = answeredCount;
            const pct = Math.round((answeredCount / Math.max(1, totalQuestions)) * 100);
            if (progressBar) progressBar.style.width = pct + '%';
          }

          sendAnswer(this.value, isFlagged);
        });
      });

      // Flag for review button
      if (flagBtn) {
        flagBtn.addEventListener('click', function () {
          isFlagged = !isFlagged;
          this.setAttribute('data-flagged', isFlagged ? 'true' : 'false');
          this.setAttribute('aria-pressed', isFlagged ? 'true' : 'false');
          this.textContent = isFlagged ? 'Flagged for review' : 'Flag for review';
          if (isFlagged) {
            this.classList.add('btn--primary');
          } else {
            this.classList.remove('btn--primary');
          }

          const currentSelected = document.querySelector('.js-option-radio:checked')?.value || null;
          sendAnswer(currentSelected, isFlagged);
        });
      }

      // Submit Confirmation Modal
      const submitModal = document.getElementById('submit-modal');
      const openSubmitBtns = document.querySelectorAll('.js-open-submit-modal');
      const cancelSubmitBtn = document.getElementById('btn-cancel-submit');

      openSubmitBtns.forEach(btn => {
        btn.addEventListener('click', () => {
          if (submitModal) {
            submitModal.style.display = 'flex';
          }
        });
      });

      if (cancelSubmitBtn && submitModal) {
        cancelSubmitBtn.addEventListener('click', () => {
          submitModal.style.display = 'none';
        });
      }

      // Track completed/progressed audio per placement attempt
      var audioPlayer = document.querySelector('.exam-audio[data-mode="exam"]');
      var placementAudioStorageKey = 'ee_placement_audio_' + placementId;
      if (audioPlayer) {
        var qAudioKey = config.audioKey;
        var stored = {};
        try {
          stored = JSON.parse(localStorage.getItem(placementAudioStorageKey) || '{}');
        } catch (e) {}

        var progress = stored[qAudioKey];
        if (progress) {
          if (typeof progress === 'boolean' && progress) {
            audioPlayer.dataset.completed = 'true';
          } else if (progress.completed) {
            audioPlayer.dataset.completed = 'true';
          } else if (progress.currentTime > 0) {
            audioPlayer.dataset.currentTime = String(progress.currentTime);
          }
        }

        document.addEventListener('examaudiotimeupdate', function (e) {
          if (qAudioKey && e.detail) {
            try {
              var s = JSON.parse(localStorage.getItem(placementAudioStorageKey) || '{}');
              var cur = s[qAudioKey] || {};
              s[qAudioKey] = {
                completed: !!(e.detail.completed || cur.completed),
                currentTime: Math.max(Number(cur.currentTime) || 0, Number(e.detail.currentTime) || 0)
              };
              localStorage.setItem(placementAudioStorageKey, JSON.stringify(s));
            } catch (err) {}
          }
        });

        document.addEventListener('examaudioended', function () {
          if (qAudioKey) {
            try {
              var s = JSON.parse(localStorage.getItem(placementAudioStorageKey) || '{}');
              s[qAudioKey] = { completed: true, currentTime: Number(s[qAudioKey]?.currentTime) || 0 };
              localStorage.setItem(placementAudioStorageKey, JSON.stringify(s));
            } catch (err) {}
          }
        });
      }

      var placementSubmitForm = document.getElementById('submit-form');
      if (placementSubmitForm) {
        placementSubmitForm.addEventListener('submit', async function (e) {
          if (draftDirty && writingEditor) {
            e.preventDefault();
            await saveDraftNow();
            placementSubmitForm.submit();
            return;
          }
          try {
            localStorage.removeItem(placementAudioStorageKey);
          } catch (e) {}
        });
      }
    })();
