(function () {
    "use strict";
    var partSelect = document.getElementById("review-part-select");
    var filterSelect = document.getElementById("review-filter-select");
    var questions = document.querySelectorAll(".question-item");
    var navCells = document.querySelectorAll(".navigator__cell[data-target-id]");
    var emptyBlock = document.getElementById("no-filtered-results");

    function applyFilters() {
      var selectedPart = partSelect ? partSelect.value : "all";
      var selectedFilter = filterSelect ? filterSelect.value : "all";
      var visibleCount = 0;

      questions.forEach(function (card) {
        var part = card.getAttribute("data-part");
        var status = card.getAttribute("data-status");
        var flagged = card.getAttribute("data-flagged") === "true";
        var qId = card.getAttribute("data-question-id");

        var matchesPart = (selectedPart === "all" || part === selectedPart);
        var matchesStatus = true;
        if (selectedFilter === "incorrect") matchesStatus = (status === "incorrect");
        else if (selectedFilter === "blank") matchesStatus = (status === "blank");
        else if (selectedFilter === "flagged") matchesStatus = flagged;
        else if (selectedFilter === "correct") matchesStatus = (status === "correct");

        var isVisible = matchesPart && matchesStatus;
        card.style.display = isVisible ? "" : "none";
        if (isVisible) visibleCount++;

        var cell = document.getElementById("nav-cell-" + qId);
        if (cell) {
          cell.style.opacity = isVisible ? "1" : "0.25";
        }
      });

      if (emptyBlock) {
        emptyBlock.style.display = visibleCount === 0 ? "" : "none";
      }
    }

    if (partSelect) partSelect.addEventListener("change", applyFilters);
    if (filterSelect) filterSelect.addEventListener("change", applyFilters);

    navCells.forEach(function (cell) {
      cell.addEventListener("click", function (e) {
        var targetId = cell.getAttribute("data-target-id");
        var targetCard = document.getElementById("question-card-target-" + targetId) || document.getElementById("question-card-" + targetId);
        if (targetCard) {
          e.preventDefault();
          targetCard.scrollIntoView({ behavior: "smooth", block: "start" });
          targetCard.style.outline = "2px solid var(--sign-blue, #0b4ea2)";
          setTimeout(function () {
            targetCard.style.outline = "";
          }, 1500);
        }
      });
    });
  })();
