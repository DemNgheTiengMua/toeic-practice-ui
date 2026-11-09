(function() {
    const editor = document.getElementById('writing-editor');
    const wordCountSpan = document.getElementById('word-count');
    const wordStatusSpan = document.getElementById('word-status');
    const wordMeterFill = document.getElementById('word-meter-fill');
    const saveStatusText = document.getElementById('save-status-text');
    const btnSaveDraft = document.getElementById('btn-save-draft');

    if (!editor) return;

    const attemptId = parseInt(editor.getAttribute('data-attempt-id'), 10);
    const questionId = parseInt(editor.getAttribute('data-question-id'), 10);
    const minWords = parseInt(editor.getAttribute('data-min-words'), 10) || 0;
    const maxWords = parseInt(editor.getAttribute('data-max-words'), 10) || 0;

    // #239: the server's own separator expression, sent with the page. Counting with `\s+` here would
    // split a no-break space or a hyphen and the page would show one word more than the row stores.
    // The browser has no lookbehind-free fallback need: the pattern is a plain character class.
    const wordPattern = editor.getAttribute('data-word-pattern') || '[ \\t\\r\\n]+';
    const wordSeparators = new RegExp(wordPattern);

    function countWords(str) {
      if (!str) return 0;
      return str.split(wordSeparators).filter(function (token) { return token.length > 0; }).length;
    }

    const checklistLengthItem = document.querySelector('[data-checklist="length"]');
    const checklistLengthStatus = document.querySelector('[data-checklist-status="length"]');
    const checklistAnsweredItem = document.querySelector('[data-checklist="answered"]');

    // One state rule for every checklist row: `true` paints it met, `false` paints it unmet, and `null`
    // leaves it plain — a row the task does not require cannot claim to be satisfied.
    function setChecklistState(item, met) {
      if (!item) return;
      item.classList.toggle('is-met', met === true);
      item.classList.toggle('is-unmet', met === false);
    }

    function updateWordMeter() {
      const words = countWords(editor.value);
      wordCountSpan.textContent = words;

      const wordMeterContainer = document.getElementById('word-meter-container');
      const hasRange = minWords > 0 || maxWords > 0;
      let pct = 0;
      let statusDesc = 'words';
      let lengthMet = null;

      if (minWords > 0) {
        if (words < minWords) {
          const under = minWords - words;
          statusDesc = `words · ${under} under the ${minWords}-word minimum`;
          pct = Math.min(70, Math.round((words / minWords) * 70));
          lengthMet = false;
        } else if (maxWords > 0 && words > maxWords) {
          const over = words - maxWords;
          statusDesc = `words · ${over} over the ${maxWords}-word maximum`;
          pct = 100;
          lengthMet = false;
        } else {
          statusDesc = `words · the ${minWords}-word minimum is met`;
          pct = maxWords > 0
            ? 70 + Math.min(30, Math.round(((words - minWords) / (maxWords - minWords)) * 30))
            : 85;
          lengthMet = true;
        }
      } else if (maxWords > 0) {
        if (words > maxWords) {
          const over = words - maxWords;
          statusDesc = `words · ${over} over the ${maxWords}-word maximum`;
          pct = 100;
          lengthMet = false;
        } else {
          statusDesc = `words · within ${maxWords}-word limit`;
          pct = Math.round((words / maxWords) * 100);
          lengthMet = true;
        }
      }

      wordStatusSpan.textContent = statusDesc;
      wordMeterFill.style.width = pct + '%';
      if (wordMeterContainer) {
        wordMeterContainer.className = 'word-meter'
          + (lengthMet === true ? ' word-meter--met' : lengthMet === false ? ' word-meter--over' : '');
      }

      // The checklist reads the same number the meter does, because it is the same number.
      setChecklistState(checklistLengthItem, lengthMet);
      if (checklistLengthStatus) {
        checklistLengthStatus.textContent = !hasRange ? ''
          : lengthMet === true ? 'The length requirement is met.'
          : 'The length requirement is not met yet.';
      }
      setChecklistState(checklistAnsweredItem, words > 0);
    }

    editor.addEventListener('input', updateWordMeter);
    updateWordMeter();

    // Auto-save logic
    let pendingSave = null;
    let dirty = false;
    let saveTimeout = null;

    editor.addEventListener('input', function() {
      dirty = true;
      if (saveTimeout) clearTimeout(saveTimeout);
      saveTimeout = setTimeout(saveDraft, 4000);
    });

    async function saveDraft() {
      if (pendingSave) {
        if (!await pendingSave) return false;
        return saveDraft();
      }
      if (!dirty) return true;
      pendingSave = persistDraft();
      let saved;
      try { saved = await pendingSave; }
      finally { pendingSave = null; }
      return saved && (dirty ? await saveDraft() : true);
    }

    async function persistDraft() {
      const savedText = editor.value;
      saveStatusText.textContent = 'Saving…';

      try {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        const resp = await fetch('/writing/save-draft', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
          },
          body: JSON.stringify({
            attemptId: attemptId,
            questionId: questionId,
            responseText: savedText
          })
        });

        if (resp.ok) {
          const d = new Date();
          const timeStr = d.toTimeString().split(' ')[0];
          saveStatusText.textContent = `Saved ${timeStr}`;
          dirty = editor.value !== savedText;
          return true;
        } else {
          saveStatusText.textContent = 'Save failed';
          return false;
        }
      } catch (err) {
        saveStatusText.textContent = 'Save failed';
        return false;
      }
    }

    btnSaveDraft.addEventListener('click', saveDraft);

    // Prevent draft loss on submit: await save before proceeding
    const submitForm = document.getElementById('submit-form');
    if (submitForm) {
      submitForm.addEventListener('submit', async function(e) {
        if (dirty) {
          e.preventDefault();
          btnSaveDraft.disabled = true;
          const submitBtn = document.getElementById('btn-submit-exam');
          if (submitBtn) submitBtn.disabled = true;
          try {
            if (await saveDraft()) submitForm.submit();
          } finally {
            btnSaveDraft.disabled = false;
            if (submitBtn) submitBtn.disabled = false;
          }
        }
      });
    }

    // Prevent draft loss on question navigation
    document.querySelectorAll('.navigator__cell[data-question-id]').forEach(link => {
      link.addEventListener('click', async event => {
        if (!dirty) return;
        event.preventDefault();
        if (await saveDraft()) window.location.href = link.href;
      });
    });

    // Timer countdown
    const timerDisplay = document.getElementById('timer-display');
    let remaining = parseInt(timerDisplay?.getAttribute('data-remaining') || '0', 10);

    function updateTimer() {
      if (!timerDisplay) return;
      if (remaining <= 0) {
        timerDisplay.textContent = '00:00';
        return;
      }
      const m = Math.floor(remaining / 60);
      const s = remaining % 60;
      timerDisplay.textContent = `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
      remaining--;
    }

    setInterval(updateTimer, 1000);
    updateTimer();

    // Submit modal wiring
    const submitModal = document.getElementById('submit-modal');
    const btnOpenSubmit = document.getElementById('btn-open-submit-modal');
    const closeSubmitBtns = document.querySelectorAll('.js-close-submit-modal');

    function openModal() {
      if (!submitModal) return;
      submitModal.classList.add('modal-overlay--open');
      submitModal.setAttribute('aria-hidden', 'false');
    }

    function closeModal() {
      if (!submitModal) return;
      submitModal.classList.remove('modal-overlay--open');
      submitModal.setAttribute('aria-hidden', 'true');
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
      if (e.key === 'Escape' && submitModal && submitModal.getAttribute('aria-hidden') === 'false') {
        closeModal();
      }
    });
  })();
