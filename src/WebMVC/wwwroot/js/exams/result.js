(function () {
      "use strict";
      var card = document.getElementById("grading-status-card");
      if (!card) return;

      var message = document.getElementById("grading-status-message");
      var title = document.getElementById("grading-status-title");
      var intervalId;

      async function refreshGradingStatus() {
        try {
          var response = await fetch(card.dataset.statusUrl, {
            credentials: "same-origin",
            headers: { "Accept": "application/json" },
            cache: "no-store"
          });
          if (!response.ok) throw new Error("Status request failed.");

          var status = await response.json();
          if (status.isGraded) {
            window.clearInterval(intervalId);
            window.location.reload();
            return;
          }

          var pending = Math.max(0, status.freeResponseCount - status.gradedFreeResponseCount - status.failedFreeResponseCount);
          if (status.failedFreeResponseCount > 0 && pending === 0) {
            title.textContent = "Some responses could not be graded";
            message.textContent = status.failedFreeResponseCount + " response(s) need grading service attention. Objective skill scores remain available below.";
            window.clearInterval(intervalId);
            return;
          }

          message.textContent = "Objective skill scores are available below. " + pending + " free response(s) are waiting for grading.";
        } catch (error) {
          message.textContent = "Unable to refresh grading status. The page will try again shortly.";
        }
      }

      intervalId = window.setInterval(refreshGradingStatus, 3000);
      refreshGradingStatus();
    })();
