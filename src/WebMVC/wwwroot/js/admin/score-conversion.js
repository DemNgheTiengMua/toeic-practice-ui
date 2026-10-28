function recalculateConversionSandbox() {
      var inputs = document.querySelectorAll('.sc-sandbox-input');
      var total = 0;
      inputs.forEach(function(inp) {
        var skillId = inp.getAttribute('data-skill-id');
        var rawVal = parseInt(inp.value, 10) || 0;
        var maxQ = parseInt(inp.getAttribute('data-max-q'), 10) || 100;

        // Try to lookup from the scale table on the page
        var scaleInput = document.querySelector('input[name$=".RawScore"][value="' + rawVal + '"]');
        var scaled = 0;
        if (scaleInput) {
          var row = scaleInput.closest('tr');
          var scaledInput = row ? row.querySelector('input[name$=".ScaledScore"]') : null;
          if (scaledInput) scaled = parseInt(scaledInput.value, 10) || 0;
        } else {
          // Fallback proportional
          scaled = Math.round((rawVal / maxQ) * 495 / 5) * 5;
        }

        var scaledLabel = document.querySelector('.sc-sandbox-scaled[data-skill-id="' + skillId + '"]');
        if (scaledLabel) scaledLabel.textContent = scaled;
        total += scaled;
      });

      var totalEl = document.getElementById('scSandboxTotal');
      if (totalEl) totalEl.textContent = total;

      // Check bands on page
      var bandRows = document.querySelectorAll('input[name$=".MinTotal"]');
      var matchedBand = null;
      bandRows.forEach(function(minInp) {
        var row = minInp.closest('tr');
        var minVal = parseInt(minInp.value, 10) || 0;
        var maxInp = row ? row.querySelector('input[name$=".MaxTotal"]') : null;
        var maxVal = maxInp ? parseInt(maxInp.value, 10) || 0 : 0;
        var nameInp = row ? row.querySelector('input[name$=".Name"]') : null;
        var codeInp = row ? row.querySelector('input[name$=".Code"]') : null;

        if (total >= minVal && total <= maxVal) {
          matchedBand = (codeInp ? codeInp.value : '') + ' • ' + (nameInp ? nameInp.value : '');
        }
      });

      var bandEl = document.getElementById('scSandboxBand');
      if (bandEl) bandEl.textContent = matchedBand || 'NO BAND MATCH';
    }

    document.addEventListener('DOMContentLoaded', function() {
      recalculateConversionSandbox();
    });
