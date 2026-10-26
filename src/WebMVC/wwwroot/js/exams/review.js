(function (sitting) {
  "use strict";

  // 7. Reading Review Screen Modal & Multi-Stage Adaptive CAT Routing
  sitting.openReviewScreenModal = function () {
    if (!sitting.reviewScreenModal) return;
    sitting.updateReviewScreenStats();
    sitting.renderReviewQuestions('all');
    if (sitting.reviewUnitTransitionContainer) {
      var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
      var isUnitLockedSandbox = currentQ && currentQ.navigationMode === 'UNIT_LOCKED_SANDBOX';
      sitting.reviewUnitTransitionContainer.style.display = (isUnitLockedSandbox && sitting.activeUnitNumber === 1) ? 'block' : 'none';
    }
    sitting.reviewScreenModal.style.display = 'flex';
  };
  sitting.closeReviewScreenModal = function () {
    if (sitting.reviewScreenModal) sitting.reviewScreenModal.style.display = 'none';
  };
  sitting.updateReviewScreenStats = function () {
    var phase = sitting.phases[sitting.activePhaseIndex];
    var phaseQs = phase ? phase.questions : sitting.questions;
    var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
    if (currentQ && currentQ.navigationMode === 'UNIT_LOCKED_SANDBOX') {
      phaseQs = phaseQs.filter(function (q) { return (q.unitNumber || 1) === sitting.activeUnitNumber; });
    }
    var answered = 0;
    var flagged = 0;
    phaseQs.forEach(function (q) {
      var a = sitting.localAnswers[q.questionId];
      var hasResponse = (a && a.selectedOptionId !== null && a.selectedOptionId !== undefined)
        || (a && ((a.responseText || '').trim().length > 0 || a.responseAudioPath));
      if (hasResponse) answered++;
      if (a && a.isFlagged) flagged++;
    });
    var unanswered = phaseQs.length - answered;
    if (sitting.reviewStatAnswered) sitting.reviewStatAnswered.textContent = answered;
    if (sitting.reviewStatFlagged) sitting.reviewStatFlagged.textContent = flagged;
    if (sitting.reviewStatUnanswered) sitting.reviewStatUnanswered.textContent = unanswered;
  };
  sitting.renderReviewQuestions = function (filterMode) {
    if (!sitting.reviewQuestionsList) return;
    var phase = sitting.phases[sitting.activePhaseIndex];
    var phaseQs = phase ? phase.questions : sitting.questions;
    var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
    if (currentQ && currentQ.navigationMode === 'UNIT_LOCKED_SANDBOX') {
      phaseQs = phaseQs.filter(function (q) { return (q.unitNumber || 1) === sitting.activeUnitNumber; });
    }

    var filtered = phaseQs;
    if (filterMode === 'unanswered') {
      filtered = phaseQs.filter(function (q) {
        var a = sitting.localAnswers[q.questionId];
        return !a || (a.selectedOptionId === null && (!a.responseText || !a.responseText.trim()) && !a.responseAudioPath);
      });
    } else if (filterMode === 'flagged') {
      filtered = phaseQs.filter(function (q) {
        var a = sitting.localAnswers[q.questionId];
        return !!(a && a.isFlagged);
      });
    }

    var html = '';
    filtered.forEach(function (q) {
      var a = sitting.localAnswers[q.questionId];
      var isAns = (a && a.selectedOptionId !== null && a.selectedOptionId !== undefined)
        || (a && ((a.responseText || '').trim().length > 0 || a.responseAudioPath));
      var isFl = !!(a && a.isFlagged);
      html += '<button type="button" class="review-item-card" data-display-order="' + q.displayOrder + '">';
      html += '<div style="display:flex; justify-content:space-between; align-items:center;">';
      html += '<strong style="font-family:\'IBM Plex Mono\', monospace; font-size:0.9rem;">Q' + q.displayOrder + '</strong>';
      if (isFl) {
        html += '<span class="badge" style="background:#f2b705; color:#14181d; font-size:0.7rem; font-weight:700;">[FLAG] FLAGGED</span>';
      }
      html += '</div>';
      html += '<span style="font-size:0.75rem; font-family:\'IBM Plex Sans Condensed\', sans-serif; text-transform:uppercase; color:' + (isAns ? '#0a6b3c' : '#b31217') + '; font-weight:700;">' + (isAns ? 'ANSWERED' : 'UNANSWERED') + '</span>';
      html += '</button>';
    });
    if (filtered.length === 0) {
      html = '<div style="grid-column: 1 / -1; padding: 1.5rem; text-align:center; background:#fff; color:#555;">No questions match the selected filter.</div>';
    }
    sitting.reviewQuestionsList.innerHTML = html;
  };

  // Unit Lock Modal handlers
  sitting.openUnitLockModal = function () {
    sitting.closeReviewScreenModal();
    if (sitting.unitLockModal) sitting.unitLockModal.style.display = 'flex';
  };
  sitting.closeUnitLockModal = function () {
    if (sitting.unitLockModal) sitting.unitLockModal.style.display = 'none';
  };

  sitting.openGroupLockModal = function (targetGroupId, targetDisplayOrder) {
    sitting.pendingTargetGroupId = targetGroupId;
    sitting.pendingTargetDisplayOrder = targetDisplayOrder;
    if (sitting.groupLockModal) sitting.groupLockModal.style.display = 'flex';
  };
  sitting.closeGroupLockModal = function () {
    sitting.pendingTargetGroupId = null;
    sitting.pendingTargetDisplayOrder = null;
    if (sitting.groupLockModal) sitting.groupLockModal.style.display = 'none';
  };

  sitting.bindReviewControls = function () {
    if (sitting.btnOpenReviewScreen) {
      sitting.btnOpenReviewScreen.addEventListener('click', sitting.openReviewScreenModal);
    }
    document.querySelectorAll('.js-close-review-modal').forEach(function (btn) {
      btn.addEventListener('click', sitting.closeReviewScreenModal);
    });

    if (sitting.reviewQuestionsList) {
      sitting.reviewQuestionsList.addEventListener('click', function (e) {
        var card = e.target.closest('.review-item-card');
        if (card) {
          var order = parseInt(card.getAttribute('data-display-order'), 10);
          if (!isNaN(order)) {
            sitting.closeReviewScreenModal();
            sitting.goToQuestion(order);
          }
        }
      });
    }

    if (sitting.btnReviewFirstUnanswered) {
      sitting.btnReviewFirstUnanswered.addEventListener('click', function () {
        var phase = sitting.phases[sitting.activePhaseIndex];
        var phaseQs = phase ? phase.questions : sitting.questions;
        var currentQ = sitting.questionMap[sitting.currentDisplayOrder];
        if (currentQ && currentQ.navigationMode === 'UNIT_LOCKED_SANDBOX') {
          phaseQs = phaseQs.filter(function (q) { return (q.unitNumber || 1) === sitting.activeUnitNumber; });
        }
        var firstUnanswered = phaseQs.find(function (q) {
          var a = sitting.localAnswers[q.questionId];
          return !a || (a.selectedOptionId === null && (!a.responseText || !a.responseText.trim()) && !a.responseAudioPath);
        });
        if (firstUnanswered) {
          sitting.closeReviewScreenModal();
          sitting.goToQuestion(firstUnanswered.displayOrder);
        } else {
          alert('All questions in this section have been answered!');
        }
      });
    }

    if (sitting.btnReviewOnlyUnanswered) {
      sitting.btnReviewOnlyUnanswered.addEventListener('click', function () {
        sitting.renderReviewQuestions('unanswered');
      });
    }

    if (sitting.btnReviewOnlyFlagged) {
      sitting.btnReviewOnlyFlagged.addEventListener('click', function () {
        sitting.renderReviewQuestions('flagged');
      });
    }

    if (sitting.btnReviewShowAll) {
      sitting.btnReviewShowAll.addEventListener('click', function () {
        sitting.renderReviewQuestions('all');
      });
    }
  };

  sitting.bindGroupControls = function () {
    if (sitting.btnReviewFinishUnit) {
      sitting.btnReviewFinishUnit.addEventListener('click', sitting.openUnitLockModal);
    }

    document.querySelectorAll('.js-close-unit-lock-modal').forEach(function (btn) {
      btn.addEventListener('click', sitting.closeUnitLockModal);
    });

    sitting.pendingTargetGroupId = null;
    sitting.pendingTargetDisplayOrder = null;
    document.querySelectorAll('.js-close-group-lock-modal').forEach(function (btn) {
      btn.addEventListener('click', sitting.closeGroupLockModal);
    });

    if (sitting.btnConfirmGroupLock) {
      sitting.btnConfirmGroupLock.addEventListener('click', function () {
        if (!sitting.pendingTargetGroupId) return;
        sitting.btnConfirmGroupLock.disabled = true;
        sitting.btnConfirmGroupLock.textContent = 'Transitioning...';
        sitting.scheduleSync(true);

        fetch('/exams/attempts/' + sitting.attemptId + '/transition-group', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': sitting.csrfToken
          },
          body: JSON.stringify({ targetGroupId: sitting.pendingTargetGroupId })
        })
        .then(function (res) {
          if (!res.ok) throw new Error('Transition failed');
          return res.json();
        })
        .then(function (data) {
          sitting.activeGroupId = data.activeGroupId;
          if (typeof data.remainingSeconds === 'number') {
            sitting.expiresAt = Date.now() + (data.remainingSeconds * 1000);
          }
          sitting.btnConfirmGroupLock.disabled = false;
          sitting.btnConfirmGroupLock.textContent = 'Confirm & Proceed';
          var nextOrderToNav = sitting.pendingTargetDisplayOrder;
          sitting.closeGroupLockModal();
          sitting.goToQuestion(nextOrderToNav);
        })
        .catch(function (err) {
          sitting.btnConfirmGroupLock.disabled = false;
          sitting.btnConfirmGroupLock.textContent = 'Confirm & Proceed';
          alert('Unable to transition question group: ' + (err.message || 'System error'));
        });
      });
    }
  };

  sitting.bindUnitControl = function () {
    if (sitting.btnConfirmUnitLock) {
      sitting.btnConfirmUnitLock.addEventListener('click', function () {
        sitting.btnConfirmUnitLock.disabled = true;
        sitting.btnConfirmUnitLock.textContent = 'Locking Stage 1...';
        sitting.scheduleSync(true);

        fetch('/exams/attempts/' + sitting.attemptId + '/transition-unit', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': sitting.csrfToken
          },
          body: JSON.stringify({ targetUnitNumber: 2 })
        })
        .then(function (res) {
          if (!res.ok) {
            return fetch('/exams/take/' + sitting.attemptId + '/transition/unit', {
              method: 'POST',
              headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': sitting.csrfToken
              },
              body: JSON.stringify({ targetUnitNumber: 2 })
            });
          }
          return res;
        })
        .then(function (res) {
          if (!res.ok) throw new Error('Transition failed');
          return res.json();
        })
        .then(function (data) {
          window.location.href = '/exams/take/' + sitting.attemptId;
        })
        .catch(function (err) {
          console.error('Unit transition error:', err);
          alert('Unable to transition stage. Please try again.');
          sitting.btnConfirmUnitLock.disabled = false;
          sitting.btnConfirmUnitLock.textContent = 'Confirm & Proceed';
        });
      });
    }
  };
})(window.ExamSitting);
