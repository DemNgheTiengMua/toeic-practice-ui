(function () {
    var deck = document.querySelector(".lesson-deck");
    if (!deck) return;
    var lessonId = Number(deck.dataset.lessonId);
    var currentCard = Number(deck.dataset.initialIndex);
    var totalCards = Number(deck.dataset.totalCards);
    var isLessonCompleted = deck.dataset.completed === "true";
    var microCheckAnswered = isLessonCompleted;

    var btnPrev = document.getElementById("btnPrev");
    var btnNext = document.getElementById("btnNext");
    var btnFinish = document.getElementById("btnFinish");
    var cardTypeBadge = document.getElementById("cardTypeBadge");
    var cardCounter = document.getElementById("cardCounter");
    var deckProgressLabel = document.getElementById("deckProgressLabel");
    var antiForgeryToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

    function recordCardProgression(cardId) {
      if (!cardId) return;
      fetch('/lessons/' + lessonId + '/cards/' + cardId + '/view', {
        method: 'POST',
        headers: {
          'RequestVerificationToken': antiForgeryToken,
          'Content-Type': 'application/json'
        }
      }).catch(function (err) {
        console.warn('Failed to record card progression', err);
      });
    }

    function updateCardView() {
      for (var i = 1; i <= totalCards; i++) {
        var el = document.getElementById("card-" + i);
        if (el) el.style.display = (i === currentCard) ? "block" : "none";
      }

      btnPrev.disabled = (currentCard === 1);

      if (currentCard === totalCards) {
        btnNext.style.display = "none";
        btnFinish.style.display = microCheckAnswered ? "inline-block" : "none";
      } else {
        btnNext.style.display = "inline-block";
        btnFinish.style.display = "none";
      }

      var currentEl = document.getElementById("card-" + currentCard);
      var typeText = currentEl ? (currentEl.getAttribute("data-card-type") || "CARD").toUpperCase() : "CARD";
      cardTypeBadge.textContent = "CARD " + currentCard + ": " + typeText;
      cardCounter.textContent = currentCard + " / " + totalCards + " cards";
      deckProgressLabel.textContent = "Card " + currentCard + " of " + totalCards;
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

        var nextEl = document.getElementById("card-" + currentCard);
        if (nextEl) {
          var cardId = nextEl.getAttribute("data-card-id");
          recordCardProgression(cardId);
        }
      }
    });

    // Micro-check interactions
    var btnCheckAnswer = document.getElementById("btnCheckAnswer");
    var explanation = document.getElementById("microCheckExplanation");
    var verdict = document.getElementById("microCheckVerdict");
    var correctAnswerLine = document.getElementById("microCheckCorrectAnswer");
    var options = document.querySelectorAll(".micro-check-option");

    options.forEach(function (opt) {
      opt.addEventListener("click", function () {
        if (microCheckAnswered && !isLessonCompleted) return;
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
        var cardId = microCheckCard ? microCheckCard.getAttribute("data-card-id") : null;

        btnCheckAnswer.disabled = true;

        // #155: graded by the server, so the answer counts toward mastery and the correct option is
        // not readable from the page before answering.
        var body = new URLSearchParams();
        body.set('selectedOption', selected.value);

        fetch('/lessons/' + lessonId + '/cards/' + cardId + '/micro-check', {
          method: 'POST',
          headers: {
            'RequestVerificationToken': antiForgeryToken,
            'Content-Type': 'application/x-www-form-urlencoded'
          },
          body: body.toString()
        }).then(function (response) {
          if (!response.ok) throw new Error('micro-check failed');
          return response.json();
        }).then(function (result) {
          microCheckAnswered = true;
          document.querySelectorAll('input[name="microCheck"]').forEach(function (input) {
            input.disabled = true;
          });

          options.forEach(function (opt) {
            var val = opt.getAttribute("data-opt");
            if (val === result.correctOption) {
              opt.classList.add("is-correct");
            } else if (opt.classList.contains("is-selected")) {
              opt.classList.add("is-wrong");
            }
          });

          if (verdict) {
            verdict.textContent = result.isCorrect ? "Correct." : "Not quite.";
          }
          if (correctAnswerLine) {
            correctAnswerLine.textContent = "Correct answer: (" + result.correctOption + ")";
          }
          if (explanation) explanation.style.display = "block";
          if (btnFinish) btnFinish.style.display = "inline-block";
        }).catch(function (err) {
          // Leave the button live so the learner can try again rather than being stuck mid-lesson.
          btnCheckAnswer.disabled = false;
          console.warn('Failed to grade the micro-check', err);
        });
      });
    }

    updateCardView();
  })();
