(function () {
    var modal = document.getElementById('cancel-modal');
    var form = document.getElementById('cancel-form');
    var cancelBtns = document.querySelectorAll('.js-cancel-exam-btn');
    var closeBtns = document.querySelectorAll('.js-close-cancel-modal');

    cancelBtns.forEach(function (btn) {
      btn.addEventListener('click', function () {
        var attemptId = btn.getAttribute('data-attempt-id');
        var examName = btn.getAttribute('data-exam-name');
        document.getElementById('cancel-modal-desc').textContent = examName + ' · The exam will not be graded and will not appear in your completed tests.';
        form.action = '/exams/take/' + attemptId + '/cancel';
        modal.style.display = 'flex';
      });
    });

    closeBtns.forEach(function (btn) {
      btn.addEventListener('click', function () {
        modal.style.display = 'none';
      });
    });

    window.addEventListener('click', function (e) {
      if (e.target === modal) {
        modal.style.display = 'none';
      }
    });
  })();
