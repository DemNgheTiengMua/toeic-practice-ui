(function () {
    var deck = document.querySelector(".lesson-deck");
    if (!deck) return;
    var currentCard = Number(deck.dataset.initialIndex);
    var totalCards = Number(deck.dataset.totalCards);
    var microCheckAnswered = false;

    var btnPrev = document.getElementById("btnPrev");
    var btnNext = document.getElementById("btnNext");
    var btnFinish = document.getElementById("btnFinish");
    var cardTypeBadge = document.getElementById("cardTypeBadge");
    var cardCounter = document.getElementById("cardCounter");

    function updateCardView() {
      for (var i = 1; i <= totalCards; i++) {
        var el = document.getElementById("card-" + i);
        if (el) el.style.display = (i === currentCard) ? "block" : "none";
      }

      btnPrev.disabled = (currentCard === 1);

      if (currentCard === totalCards) {
        btnNext.style.display = "none";
        btnFinish.style.display = "inline-block";
      } else {
        btnNext.style.display = "inline-block";
        btnFinish.style.display = "none";
      }

      var currentEl = document.getElementById("card-" + currentCard);
      var typeText = currentEl ? (currentEl.getAttribute("data-card-type") || "CARD").toUpperCase() : "CARD";
      cardTypeBadge.textContent = "CARD " + currentCard + ": " + typeText;
      cardCounter.textContent = currentCard + " / " + totalCards + " cards";
    }

    btnPrev.addEventListener("click", function () {
      if (currentCard > 1) {
        currentCard--;
        updateCardView();
      }
    });

    btnNext.addEventListener("click", function () {
      if (currentCard < totalCards) {
        currentCard++;
        updateCardView();
      }
    });

    // Micro-check interactions
    var btnCheckAnswer = document.getElementById("btnCheckAnswer");
    var explanation = document.getElementById("microCheckExplanation");
    var options = document.querySelectorAll(".micro-check-option");

    options.forEach(function (opt) {
      opt.addEventListener("click", function () {
        options.forEach(function (o) { o.classList.remove("is-selected"); });
        opt.classList.add("is-selected");
        var radio = opt.querySelector('input[type="radio"]');
        if (radio) radio.checked = true;
      });
    });

    if (btnCheckAnswer) {
      btnCheckAnswer.addEventListener("click", function () {
        var selected = document.querySelector('input[name="microCheck"]:checked');
        if (!selected) {
          alert("Please select an option first.");
          return;
        }

        var microCheckCard = document.querySelector('[data-card-type="micro_check"]');
        var correctOption = microCheckCard ? microCheckCard.getAttribute("data-correct") : "B";

        options.forEach(function (opt) {
          var val = opt.getAttribute("data-opt");
          if (val === correctOption) {
            opt.classList.add("is-correct");
          } else if (opt.classList.contains("is-selected")) {
            opt.classList.add("is-wrong");
          }
        });

        if (explanation) explanation.style.display = "block";
        btnCheckAnswer.disabled = true;
      });
    }

    updateCardView();
  })();
