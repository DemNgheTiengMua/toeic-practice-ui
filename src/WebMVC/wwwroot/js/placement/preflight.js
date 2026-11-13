/* ============================================================================
   #290: Placement preflight / retake confirmation interaction.
   - Enables the confirm button only after a scope is explicitly chosen.
   - Keeps the audio equipment panel aligned with the chosen scope (hiding the
     microphone check when Listening and Reading only is chosen).
   - Guards against double-submit alongside the server idempotency key.
   ============================================================================ */
(function () {
  "use strict";

  document.addEventListener("DOMContentLoaded", function () {
    var form = document.getElementById("placement-confirm-form");
    if (!form) return;

    var confirmBtn = document.getElementById("confirm-start-btn");
    var radios = form.querySelectorAll('input[type="radio"][name="scope"]');
    var selectionHint = document.getElementById("scope-selection-hint");
    var equipmentPanel = document.querySelector("[data-audio-equipment]");
    var inputPanel = equipmentPanel ? equipmentPanel.querySelector('[data-audio-role="input-panel"]') : null;

    var lrRequiresMic = form.getAttribute("data-lr-requires-mic") === "true";
    var lrRequiresAudio = form.getAttribute("data-lr-requires-audio") === "true";
    var allRequiresMic = form.getAttribute("data-all-requires-mic") === "true";
    var allRequiresAudio = form.getAttribute("data-all-requires-audio") === "true";
    var submitting = false;

    function selectedScope() {
      for (var i = 0; i < radios.length; i++) {
        if (radios[i].checked && !radios[i].disabled) {
          return radios[i].value;
        }
      }
      return null;
    }

    function syncAudioPanel(scope) {
      if (!equipmentPanel) return;
      var needsMic = scope === "listening_and_reading" ? lrRequiresMic : allRequiresMic;
      var needsAudio = scope === "listening_and_reading" ? lrRequiresAudio : allRequiresAudio;
      equipmentPanel.hidden = !needsMic && !needsAudio;
      if (inputPanel) {
        inputPanel.hidden = !needsMic;
      }
    }

    function syncState() {
      var scope = selectedScope();
      if (confirmBtn) {
        confirmBtn.disabled = !scope || submitting;
      }
      if (selectionHint) {
        selectionHint.hidden = !!scope;
      }
      syncAudioPanel(scope);
    }

    for (var i = 0; i < radios.length; i++) {
      radios[i].addEventListener("change", syncState);
    }

    form.addEventListener("submit", function (e) {
      if (submitting || !selectedScope()) {
        e.preventDefault();
        return;
      }
      submitting = true;
      if (confirmBtn) {
        confirmBtn.disabled = true;
        confirmBtn.textContent = "Starting sitting...";
      }
    });

    syncState();
  });
})();
