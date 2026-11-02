(function (sitting) {
  "use strict";

  // #158: admin preview of the real sitting view. Loaded ONLY by the preview page
  // (Views/Exams/_TakePreviewBar.cshtml), after the shell has initialised, and it exits at once on
  // a learner's sitting.
  if (!sitting.isPreview) return;

  // --- Read-only -------------------------------------------------------------------------
  // The preview has no server write path: every attempt-scoped action would refuse attempt id 0,
  // and a refusal queue that retries forever is worse than no queue. Belt: the shared shell's
  // writers cannot leave the page. Braces: the answer controls are disabled below, so a click
  // never becomes an answer either.
  var nativeFetch = window.fetch;
  window.fetch = function (input, init) {
    var method = ((init && init.method) || (input && input.method) || 'GET').toUpperCase();
    var url = typeof input === 'string' ? input : (input && input.url) || '';
    if (method !== 'GET' && method !== 'HEAD' && url.indexOf('/exams/') !== -1) {
      return Promise.resolve(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }));
    }
    return nativeFetch.apply(window, arguments);
  };

  // The shared countdown auto-submits at zero. A preview has nothing to submit: this flag is the
  // guard that branch already reads, and it also silences the "unsaved work" unload prompt.
  sitting.isSubmitting = true;

  // Update this, not the online/offline handler: a preview starts with nothing queued, so the
  // only thing that could make it say "Offline · Saved locally" is a restored stale queue.
  // The position sync would otherwise paint "Saving…" over the placard on every question swap.
  // Re-stamp instead of going quiet, so the placard cannot be overwritten by other shell writers.
  sitting.scheduleSync = function () {};
  sitting.triggerSync = function () {};
  sitting.stampPreviewStatus = function () {
    var label = document.getElementById('save-status');
    if (label) {
      label.textContent = 'PREVIEW · read only';
      label.className = 'save-status save-status--saved';
    }
  };

  var status = document.getElementById('save-status');
  if (status) {
    status.textContent = 'PREVIEW · read only';
    status.className = 'save-status save-status--saved';
  }

  function markReadOnly() {
    document.querySelectorAll('#option-list .option__input, #cluster-card-container .option__input, #writing-editor, #free-response-text')
      .forEach(function (control) { control.disabled = true; });

    // A preview has nothing to submit, exit or flag: those controls exist only to mutate an
    // attempt, so showing them would offer an action the page cannot honour.
    ['btn-sidebar-submit', 'btn-open-exit-modal', 'btn-open-review-screen', 'flag-btn',
      'btn-prev', 'btn-next', 'flagged-filter-btn'].forEach(function (id) {
      var control = document.getElementById(id);
      if (control) control.style.display = 'none';
    });
    document.querySelectorAll('.js-open-submit-modal').forEach(function (control) {
      control.style.display = 'none';
    });
  }

  // A speaking item would open the microphone and upload; in a preview neither may happen.
  sitting.startSpeakingPacerStateMachine = function (q) {
    var container = document.getElementById('speaking-pacer-container');
    var script = document.getElementById('speaking-pacer-script');
    if (script) script.textContent = 'Recording is disabled in preview.';
    if (container) container.style.display = 'block';
  };

  // --- Answer key ---------------------------------------------------------------------
  var answers = {};
  try {
    answers = JSON.parse((document.getElementById('preview-answers') || {}).textContent || '{}');
  } catch (e) {
    answers = {};
  }

  var toggle = document.getElementById('preview-answer-toggle');
  var shown = false;

  function paint() {
    var host = document.getElementById('preview-answer-key');
    if (!host) return;
    var q = sitting.questionMap[sitting.currentDisplayOrder];
    if (!shown || !q) {
      host.style.display = 'none';
      host.innerHTML = '';
      document.querySelectorAll('#option-list .option--correct').forEach(function (label) {
        label.classList.remove('option--correct');
      });
      return;
    }

    var key = answers[q.questionId] || {};
    var correct = key.correctOptionIds || [];
    document.querySelectorAll('#option-list .option').forEach(function (label) {
      var input = label.querySelector('.option__input');
      label.classList.toggle('option--correct', !!input && correct.indexOf(Number(input.value)) !== -1);
    });

    var letters = [];
    (q.options || []).forEach(function (option) {
      if (correct.indexOf(option.optionId) !== -1) letters.push(option.label);
    });

    host.style.display = 'block';
    host.innerHTML =
      '<div class="preview-answer-key__head">ANSWER KEY · QUESTION ' + q.displayOrder + '</div>' +
      '<p class="preview-answer-key__line">Correct: <strong>' +
      (letters.length ? sitting.escapeHtml(letters.join(', ')) : 'no key recorded') + '</strong></p>' +
      (key.explanation
        ? '<p class="preview-answer-key__line"><span class="preview-answer-key__label">WHY</span> ' +
          sitting.escapeHtml(key.explanation) + '</p>'
        : '<p class="preview-answer-key__line text-muted">No explanation recorded for this question.</p>');
  }

  if (toggle) {
    toggle.addEventListener('click', function () {
      shown = !shown;
      toggle.setAttribute('aria-pressed', shown ? 'true' : 'false');
      toggle.textContent = shown ? 'Hide answers' : 'Show answers';
      toggle.classList.toggle('btn--primary', shown);
      paint();
    });
  }

  // --- Fast navigation ------------------------------------------------------------------
  var jump = document.getElementById('preview-jump-input');
  if (jump) {
    jump.addEventListener('keydown', function (event) {
      if (event.key !== 'Enter') return;
      var target = parseInt(jump.value, 10);
      if (!isNaN(target)) sitting.goToQuestion(target);
      jump.select();
    });
  }

  var sectionSelect = document.getElementById('preview-section-select');
  if (sectionSelect) {
    sectionSelect.addEventListener('change', function () {
      var target = parseInt(sectionSelect.value, 10);
      if (!isNaN(target)) sitting.goToQuestion(target);
    });
  }

  document.querySelectorAll('[data-preview-step]').forEach(function (button) {
    button.addEventListener('click', function () {
      sitting.goToQuestion(sitting.currentDisplayOrder + parseInt(button.getAttribute('data-preview-step'), 10));
    });
  });

  // j / k walk the exam without the mouse. A preview is the one place "next" may cross a skill,
  // which goToQuestion already allows when isPreview is set.
  document.addEventListener('keydown', function (event) {
    var active = document.activeElement;
    if (active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.tagName === 'SELECT')) return;
    if (event.key === 'j' || event.key === 'PageDown') {
      event.preventDefault();
      sitting.goToQuestion(sitting.currentDisplayOrder + 1);
    } else if (event.key === 'k' || event.key === 'PageUp') {
      event.preventDefault();
      sitting.goToQuestion(sitting.currentDisplayOrder - 1);
    }
  });

  // The shell replaces the option list on every swap; the key, the read-only controls and the
  // section selector must follow the question, not only lead it.
  var renderQuestion = sitting.renderQuestion;
  sitting.renderQuestion = function () {
    renderQuestion.apply(sitting, arguments);
    markReadOnly();
    paint();
    sitting.stampPreviewStatus();
    if (sectionSelect) {
      var current = sitting.questionMap[sitting.currentDisplayOrder];
      var option = current && sectionSelect.querySelector('option[data-section-id="' + current.sectionId + '"]');
      if (option) sectionSelect.value = option.value;
    }
  };

  markReadOnly();
  paint();
})(window.ExamSitting);
