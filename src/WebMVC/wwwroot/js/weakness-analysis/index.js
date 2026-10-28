document.addEventListener('DOMContentLoaded', function () {
    const forms = document.querySelectorAll('.js-resolve-form');
    forms.forEach(function (form) {
      form.addEventListener('submit', async function (e) {
        e.preventDefault();
        const recoId = form.getAttribute('data-reco-id');
        const tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenInput ? tokenInput.value : '';

        try {
          const res = await fetch(form.action, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/x-www-form-urlencoded',
              'X-Requested-With': 'XMLHttpRequest',
              'Accept': 'application/json'
            },
            body: new URLSearchParams({
              '__RequestVerificationToken': token
            })
          });

          if (res.ok) {
            const row = document.getElementById('reco-' + recoId);
            if (row) {
              row.remove();
            }
            const remaining = document.querySelectorAll('.reco-item');
            if (remaining.length === 0) {
              const list = document.getElementById('recommendations-list');
              if (list && !document.getElementById('no-recos-message')) {
                const emptyMsg = document.createElement('div');
                emptyMsg.id = 'no-recos-message';
                emptyMsg.className = 'p-4 text-center text-muted border';
                emptyMsg.textContent = 'All recommendations resolved. Great work!';
                list.appendChild(emptyMsg);
              }
            }
          } else {
            form.submit();
          }
        } catch (err) {
          form.submit();
        }
      });
    });
  });
