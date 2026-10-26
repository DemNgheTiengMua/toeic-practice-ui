(function () {
      var check = document.getElementById('confirm-ready');
      var startBtn = document.getElementById('start-btn');
      if (check && startBtn) {
        check.addEventListener('change', function () {
          startBtn.disabled = !check.checked;
        });
      }
    })();
