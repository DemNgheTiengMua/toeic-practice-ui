(function () {
      // Inline toggle for question review
      document.querySelectorAll('.review-toggle-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
          var targetId = btn.getAttribute('data-target');
          var panel = document.getElementById(targetId);
          if (!panel) return;

          var isHidden = panel.style.display === 'none';
          panel.style.display = isHidden ? 'block' : 'none';
          btn.setAttribute('aria-expanded', isHidden ? 'true' : 'false');
          btn.textContent = isHidden ? 'Hide explanation' : 'Review explanation';
        });
      });

      document.querySelectorAll('.close-review-btn').forEach(function (btn) {
        btn.addEventListener('click', function () {
          var targetId = btn.getAttribute('data-target');
          var panel = document.getElementById(targetId);
          if (!panel) return;
          panel.style.display = 'none';

          var toggleBtn = document.querySelector('[data-target="' + targetId + '"].review-toggle-btn');
          if (toggleBtn) {
            toggleBtn.setAttribute('aria-expanded', 'false');
            toggleBtn.textContent = 'Review explanation';
          }
        });
      });

      // AJAX remove handling with progressive enhancement
      document.querySelectorAll('.remove-form').forEach(function (form) {
        form.addEventListener('submit', function (e) {
          e.preventDefault();
          var questionId = form.getAttribute('data-question-id');
          var card = document.getElementById('card-' + questionId);
          var csrfInput = form.querySelector('input[name="__RequestVerificationToken"]');
          var token = csrfInput ? csrfInput.value : '';

          var btn = form.querySelector('button[type="submit"]');
          if (btn) btn.disabled = true;

          var formData = new URLSearchParams();
          formData.append('__RequestVerificationToken', token);

          fetch('/saved-questions/' + questionId + '/remove', {
            method: 'POST',
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'RequestVerificationToken': token,
              'X-Requested-With': 'XMLHttpRequest',
              'Accept': 'application/json'
            },
            body: formData.toString()
          })
          .then(function (res) {
            if (!res.ok) throw new Error('Failed to remove');
            return res.json();
          })
          .then(function () {
            if (card) {
              card.style.transition = 'opacity 0.2s ease, transform 0.2s ease';
              card.style.opacity = '0';
              card.style.transform = 'translateY(-10px)';
              setTimeout(function () {
                card.remove();
                var remaining = document.querySelectorAll('.saved-question-card');
                if (remaining.length === 0) {
                  window.location.reload();
                }
              }, 200);
            }
          })
          .catch(function (err) {
            console.error('Remove error', err);
            if (btn) btn.disabled = false;
            form.submit(); // fallback to standard POST
          });
        });
      });
    })();
