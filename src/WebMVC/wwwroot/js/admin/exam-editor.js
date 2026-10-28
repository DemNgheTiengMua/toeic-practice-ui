document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('input[type="file"][name="AudioFile"]').forEach(function(input) {
      input.addEventListener('change', function(e) {
        var file = e.target.files && e.target.files[0];
        if (!file) return;
        var form = input.closest('form');
        if (!form) return;

        if (file.size > 62914560) {
          alert('The selected audio file (' + (file.size / (1024 * 1024)).toFixed(1) + ' MB) exceeds the maximum allowed size of 60 MB.');
          input.value = '';
          return;
        }

        var durInput = form.querySelector('input[name="DurationSec"]');
        var sizeInput = form.querySelector('input[name="SizeBytes"]');
        var preview = form.querySelector('.upload-meta-preview');

        if (sizeInput) {
          sizeInput.value = file.size;
        }

        var formatSize = (file.size / (1024 * 1024)).toFixed(2) + ' MB';
        if (preview) {
          preview.style.display = 'block';
          preview.textContent = 'Selected: ' + file.name + ' (' + formatSize + ') — extracting duration...';
        }

        var objectUrl = URL.createObjectURL(file);
        var audio = new Audio(objectUrl);
        audio.addEventListener('loadedmetadata', function() {
          var dur = 300;
          if (audio.duration && !isNaN(audio.duration) && isFinite(audio.duration)) {
            dur = Math.max(1, Math.round(audio.duration));
            if (durInput) durInput.value = dur;
          }
          if (preview) {
            preview.textContent = 'Selected: ' + file.name + ' (' + formatSize + ', ' + dur + 's)';
          }
          URL.revokeObjectURL(objectUrl);
        });
        audio.addEventListener('error', function() {
          if (preview) {
            preview.textContent = 'Selected: ' + file.name + ' (' + formatSize + ')';
          }
          URL.revokeObjectURL(objectUrl);
        });
      });
    });
  });
