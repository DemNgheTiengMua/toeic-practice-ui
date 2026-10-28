document.getElementById('confirm-ready')?.addEventListener('change', function() {
      const btn = document.getElementById('btn-start');
      if (btn) btn.disabled = !this.checked;
    });