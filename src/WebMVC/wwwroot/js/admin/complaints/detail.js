(function() {
        const form = document.querySelector('form[action*="/resolve"]');
        if (!form) return;

        const resInput = document.getElementById('cd-resolution');
        const notesInput = document.getElementById('cd-notes');

        function checkField(input) {
          if (!input) return true;
          const field = input.closest('.field');
          const val = (input.value || '').trim();
          if (!val) {
            if (field) field.classList.add('field--error');
            input.setAttribute('aria-invalid', 'true');
            return false;
          } else {
            if (field) field.classList.remove('field--error');
            input.removeAttribute('aria-invalid');
            return true;
          }
        }

        [resInput, notesInput].forEach(function(input) {
          if (!input) return;
          input.addEventListener('input', function() {
            checkField(input);
          });
        });

        form.addEventListener('submit', function(e) {
          const resOk = checkField(resInput);
          const notesOk = checkField(notesInput);
          if (!resOk || !notesOk) {
            e.preventDefault();
            if (!resOk && resInput) {
              resInput.focus();
            } else if (!notesOk && notesInput) {
              notesInput.focus();
            }
          }
        });
      })();
