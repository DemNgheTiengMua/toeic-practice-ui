document.querySelectorAll('.js-target-radio').forEach(radio => {
    radio.addEventListener('change', function () {
      document.querySelectorAll('.option').forEach(l => l.classList.remove('is-selected'));
      this.closest('.option')?.classList.add('is-selected');

      const code = this.value;
      const min = parseInt(this.getAttribute('data-min'), 10) || 0;
      const targetDisplay = document.getElementById('cefr-target-display');
      if (targetDisplay) targetDisplay.textContent = code;

      const neededDisplay = document.getElementById('cefr-needed-display');
      if (neededDisplay) neededDisplay.textContent = min + ' needed';

      const currentScore = Number(document.getElementById("target-band-page").dataset.currentScore);
      const noteDisplay = document.getElementById('cefr-note-display');
      if (noteDisplay && currentScore > 0) {
        const gap = Math.max(0, min - currentScore);
        noteDisplay.textContent = `Points to target: ${gap} points gap from your starting placement score.`;
      }
    });
  });
