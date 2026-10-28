(function() {
    var dialog = document.getElementById('import-exam-dialog');
    if (!dialog) return;

    window.openImportExamDialog = function() {
      dialog.style.display = 'grid';
      var certSelect = document.getElementById('import-certId');
      if (certSelect) certSelect.focus();
    };

    window.closeImportExamDialog = function() {
      dialog.style.display = 'none';
      if (window.location.search.toLowerCase().indexOf('import=') !== -1) {
        var cleanSearch = window.location.search.replace(/([?&])import=[^&]*(&?)/i, function(match, p1, p2) {
          return p2 ? p1 : '';
        }).replace(/\?$/, '');
        var newUrl = window.location.pathname + cleanSearch;
        window.history.replaceState(null, '', newUrl);
      }
    };

    window.downloadManifestTemplate = function(e) {
      if (e) e.preventDefault();
      var certSelect = document.getElementById('import-certId');
      if (!certSelect || !certSelect.value) {
        alert('Please select a target certificate first.');
        return;
      }
      window.location.href = '/admin/exams/manifest-template?certId=' + certSelect.value;
    };

    dialog.addEventListener('click', function(e) {
      if (e.target === dialog) {
        window.closeImportExamDialog();
      }
    });

    document.addEventListener('keydown', function(e) {
      if (e.key === 'Escape' && dialog.style.display !== 'none') {
        window.closeImportExamDialog();
      }
    });

    if (dialog.style.display === 'grid') {
      var certSelect = document.getElementById('import-certId');
      if (certSelect) certSelect.focus();
    }
    var genDialog = document.getElementById('generate-exam-dialog');
    if (genDialog) {
      window.openGenerateExamDialog = function() {
        genDialog.style.display = 'grid';
        var certCodeSelect = document.getElementById('gen-certCode');
        if (certCodeSelect) certCodeSelect.focus();
      };

      window.closeGenerateExamDialog = function() {
        genDialog.style.display = 'none';
        if (window.location.search.toLowerCase().indexOf('generate=') !== -1) {
          var cleanSearch = window.location.search.replace(/([?&])generate=[^&]*(&?)/i, function(match, p1, p2) {
            return p2 ? p1 : '';
          }).replace(/\?$/, '');
          var newUrl = window.location.pathname + cleanSearch;
          window.history.replaceState(null, '', newUrl);
        }
      };

      genDialog.addEventListener('click', function(e) {
        if (e.target === genDialog) {
          window.closeGenerateExamDialog();
        }
      });

      document.addEventListener('keydown', function(e) {
        if (e.key === 'Escape' && genDialog.style.display !== 'none') {
          window.closeGenerateExamDialog();
        }
      });

      if (genDialog.style.display === 'grid') {
        var certCodeSelect = document.getElementById('gen-certCode');
        if (certCodeSelect) certCodeSelect.focus();
      }
    }
  })();
