(function (sitting) {
  "use strict";
  // Accessibility & Volume Initialization
  sitting.initAccessibility = function () {
    var savedSize = localStorage.getItem('ee_exam_font_size') || 'standard';
    var savedTheme = localStorage.getItem('ee_exam_theme') || 'standard';

    function applySize(size) {
      document.body.classList.remove('font-size-standard', 'font-size-large', 'font-size-xlarge');
      document.body.classList.add('font-size-' + size);
      localStorage.setItem('ee_exam_font_size', size);
      document.querySelectorAll('.js-font-size-btn').forEach(function (b) {
        b.classList.toggle('btn--primary', b.getAttribute('data-size') === size);
      });
    }

    function applyTheme(theme) {
      document.body.setAttribute('data-theme', theme);
      localStorage.setItem('ee_exam_theme', theme);
      document.querySelectorAll('.js-theme-btn').forEach(function (b) {
        b.classList.toggle('btn--primary', b.getAttribute('data-theme') === theme);
      });
    }

    applySize(savedSize);
    applyTheme(savedTheme);

    document.querySelectorAll('.js-font-size-btn').forEach(function (b) {
      b.addEventListener('click', function () {
        applySize(b.getAttribute('data-size'));
      });
    });

    document.querySelectorAll('.js-theme-btn').forEach(function (b) {
      b.addEventListener('click', function () {
        applyTheme(b.getAttribute('data-theme'));
      });
    });

    var btnOpen = document.getElementById('btn-open-accessibility-modal');
    var btnClose = document.getElementById('btn-close-accessibility-modal');
    var accModal = document.getElementById('accessibility-modal');
    if (btnOpen && accModal) {
      btnOpen.addEventListener('click', function () {
        accModal.style.display = 'flex';
      });
    }
    if (btnClose && accModal) {
      btnClose.addEventListener('click', function () {
        accModal.style.display = 'none';
      });
    }
  };
})(window.ExamSitting);
