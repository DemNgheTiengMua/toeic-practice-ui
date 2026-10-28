(function() {
      const attemptId = Number(document.querySelector(".context-bar").dataset.attemptId);

      async function pollStatus() {
        try {
          const resp = await fetch(`/writing/status/${attemptId}`);
          if (resp.ok) {
            const data = await resp.json();
            if (data.attemptStatus === 'graded' || data.attemptStatus === 2) {
              window.location.href = `/writing/review/${attemptId}`;
              return;
            }
            if (data.canRetry || data.failedResponses > 0) {
              window.location.reload();
              return;
            }
            // Update counts if partially graded
            const desc = document.getElementById('timer-display');
            if (desc) {
              desc.textContent = `Processing questions (${data.gradedResponses} of ${data.totalResponses} complete)…`;
            }
            if (data.totalResponses > 0) {
              const pct = Math.max(20, Math.round((data.gradedResponses / data.totalResponses) * 100));
              const bar = document.getElementById('progress-bar');
              if (bar) bar.style.width = pct + '%';
            }
          }
        } catch (err) {
          // Keep polling
        }
        setTimeout(pollStatus, 2000);
      }

      setTimeout(pollStatus, 2000);
    })();