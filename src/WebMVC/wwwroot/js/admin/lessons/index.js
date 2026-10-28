(function() {
    var dialog = document.getElementById('import-dialog');
    if (dialog) {
      dialog.addEventListener('click', function(e) {
        if (e.target === dialog) {
          dialog.style.display = 'none';
        }
      });
      document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape' && dialog.style.display !== 'none') {
          dialog.style.display = 'none';
        }
      });
    }
  })();
