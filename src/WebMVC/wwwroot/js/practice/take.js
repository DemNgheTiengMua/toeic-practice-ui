    (function () {
      "use strict";

      var page = document.getElementById("practice-data").dataset;
      var sessionId = Number(page.sessionId);
      var totalQuestions = Number(page.totalQuestions);
      var isSessionFinished = page.finished === "true";

      var dataEl = document.getElementById("practice-data");
      var localQuestions = dataEl ? JSON.parse(dataEl.textContent) : [];

      function escapeHtml(str) {
        if (!str) return "";
        return String(str)
          .replace(/&/g, "&amp;")
          .replace(/</g, "&lt;")
          .replace(/>/g, "&gt;")
          .replace(/"/g, "&quot;")
          .replace(/'/g, "&#039;");
      }

      // In-memory state dictionary keyed by questionId
      var localState = {};
      localQuestions.forEach(function (q) {
        var isAns = q.selectedOptionId != null || q.isCorrect != null;
        var qType = (q.questionType || 'mcq').toLowerCase();
        var selectedIds = [];
        if (q.selectedOptionIds && q.selectedOptionIds.length > 0) {
          selectedIds = q.selectedOptionIds.slice();
        } else if (q.selectedOptionIdsJson) {
          try { selectedIds = JSON.parse(q.selectedOptionIdsJson); } catch (e) { selectedIds = []; }
        }

        localState[q.questionId] = {
          questionId: q.questionId,
          displayOrder: q.displayOrder,
          questionType: qType,
          maxSelections: q.maxSelections,
          allowNumber: q.allowNumber !== false,
          acceptedAnswersJson: q.acceptedAnswersJson,
          requiredKeywords: q.requiredKeywords,
          minWords: q.minWords,
          maxWords: q.maxWords,
          selectedOptionId: q.selectedOptionId,
          selectedOptionIds: selectedIds,
          selectedOptionIdsJson: q.selectedOptionIdsJson || (selectedIds.length > 0 ? JSON.stringify(selectedIds) : null),
          responseText: q.responseText || '',
          isCorrect: q.isCorrect,
          isAnswered: isAns,
          isSkipped: isSessionFinished && !isAns,
          hintUsed: q.hintUsed,
          hint: q.hint,
          isSaved: q.isSaved,
          explanation: q.explanation,
          transcript: q.transcript,
          correctOptionId: q.correctOptionId,
          correctOptionIds: q.correctOptionIds ? q.correctOptionIds.slice() : []
        };
      });

      var initialOrder = Number(page.initialOrder);
      var currentOrder = initialOrder > 0 ? initialOrder : 1;
      var activeFilter = "all";

      var csrfToken = "";
      var tokenInp = document.querySelector('input[name="__RequestVerificationToken"]');
      if (tokenInp) csrfToken = tokenInp.value;

      // DOM Elements
      var qNumberDisplay = document.getElementById("question-number-display");
      var qCategoryDisplay = document.getElementById("question-category-display");
      var statusBadge = document.getElementById("status-badge");
      var hintMetaBadge = document.getElementById("hint-meta-badge");
      var saveToggleBtn = document.getElementById("save-toggle-btn");
      var qStemDisplay = document.getElementById("question-stem-display");
      var optionList = document.getElementById("option-list");
      var componentContainer = document.getElementById("component-container");
      var hintBox = document.getElementById("hint-box");
      var hintText = document.getElementById("hint-text");
      var explanationContainer = document.getElementById("explanation-container");
      var btnPrev = document.getElementById("btn-prev");
      var btnSkip = document.getElementById("btn-skip");
      var showHintBtn = document.getElementById("show-hint-btn");
      var hintUsedMsg = document.getElementById("hint-used-msg");
      var submitBtn = document.getElementById("submit-btn");

      var audioWrapper = document.getElementById("practice-audio-wrapper");

      var audioPlayerHost = document.getElementById("practice-audio-player-host");
      var btnAudioRewind5s = document.getElementById("btn-audio-rewind-5s");
      var btnToggleTranscript = document.getElementById("btn-toggle-transcript");
      var transcriptDrawer = document.getElementById("transcript-drawer");
      var transcriptDrawerText = document.getElementById("transcript-drawer-text");

      var passageCard = document.getElementById("passage-card");
      var passageText = document.getElementById("passage-text");
      var imageContainer = document.getElementById("image-container");
      var questionImage = document.getElementById("question-image");

      var sidebarAccuracyDisplay = document.getElementById("sidebar-accuracy-display");
      var topbarAccuracyVal = document.getElementById("topbar-accuracy-val");
      var progressBar = document.getElementById("progress-bar");
      var progressText = document.getElementById("progress-text");
      var sidebarCorrectCount = document.getElementById("sidebar-correct-count");
      var sidebarIncorrectCount = document.getElementById("sidebar-incorrect-count");
      var sidebarSkippedCount = document.getElementById("sidebar-skipped-count");
      var sidebarUnansweredCount = document.getElementById("sidebar-unanswered-count");

      var filterAllCount = document.getElementById("filter-all-count");
      var filterIncorrectCount = document.getElementById("filter-incorrect-count");
      var filterSavedCount = document.getElementById("filter-saved-count");
      var filterEmptyMsg = document.getElementById("filter-empty-msg");

      var controlsModal = document.getElementById("controls-modal");
      var btnControlsModal = document.getElementById("btn-controls-modal");
      var btnCloseControlsModal = document.getElementById("btn-close-controls-modal");
      var btnModalGotIt = document.getElementById("btn-modal-got-it");

      function showToast(message) {
        var container = document.getElementById("toast-container");
        if (!container) return;
        var toast = document.createElement("div");
        toast.className = "toast";
        toast.textContent = message;
        container.appendChild(toast);
        setTimeout(function () {
          toast.style.transition = "opacity 300ms ease-out";
          toast.style.opacity = "0";
          setTimeout(function () {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
          }, 300);
        }, 2200);
      }

      function showError(message) {
        var errEl = document.getElementById("ajax-error-alert");
        if (errEl) {
          errEl.textContent = message;
          errEl.style.display = "";
          errEl.scrollIntoView({ behavior: "smooth", block: "center" });
        }
      }

      function clearError() {
        var errEl = document.getElementById("ajax-error-alert");
        if (errEl) errEl.style.display = "none";
      }

      function getCurrentQuestion() {
        return localQuestions.find(function (q) { return q.displayOrder === currentOrder; }) || localQuestions[0];
      }

      function updateAccuracyAndStats() {
        var answered = 0;
        var correct = 0;
        var incorrect = 0;
        var skipped = 0;
        var saved = 0;

        localQuestions.forEach(function (q) {
          var s = localState[q.questionId];
          if (s) {
            if (s.isAnswered) answered++;
            if (s.isCorrect === true) correct++;
            if (s.isCorrect === false) incorrect++;
            if (s.isSkipped) skipped++;
            if (s.isSaved) saved++;
          }
        });

        var unanswered = Math.max(0, totalQuestions - answered);
        var accPct = answered > 0 ? Math.round((correct / answered) * 100) : 0;
        var progPct = totalQuestions > 0 ? Math.round((answered / totalQuestions) * 100) : 0;

        if (sidebarAccuracyDisplay) {
          sidebarAccuracyDisplay.textContent = answered > 0 ? (accPct + "% (" + correct + "/" + answered + ")") : "0% (0/0)";
        }
        if (topbarAccuracyVal) {
          topbarAccuracyVal.textContent = answered > 0 ? (accPct + "%") : "--%";
        }
        if (progressBar) {
          progressBar.style.width = progPct + "%";
          var pbParent = progressBar.closest('.progress');
          if (pbParent) pbParent.setAttribute("aria-valuenow", String(answered));
        }
        if (progressText) {
          progressText.textContent = answered + "/" + totalQuestions + " answered";
        }
        if (sidebarCorrectCount) sidebarCorrectCount.textContent = String(correct);
        if (sidebarIncorrectCount) sidebarIncorrectCount.textContent = String(incorrect);
        if (sidebarSkippedCount) sidebarSkippedCount.textContent = String(skipped);
        if (sidebarUnansweredCount) sidebarUnansweredCount.textContent = String(unanswered);

        if (filterAllCount) filterAllCount.textContent = String(totalQuestions);
        if (filterIncorrectCount) filterIncorrectCount.textContent = String(incorrect);
        if (filterSavedCount) filterSavedCount.textContent = String(saved);
      }

      // Practice QoL 5s Rewind
      if (btnAudioRewind5s) {
        btnAudioRewind5s.addEventListener("click", function () {
          if (!audioWrapper) return;
          var audioEl = audioWrapper.querySelector("audio");
          if (audioEl) {
            var currentAudio = audioWrapper.querySelector(".exam-audio");
            var startMs = currentAudio && currentAudio.dataset.startMs ? parseInt(currentAudio.dataset.startMs, 10) : 0;
            var minSec = startMs > 0 ? (startMs / 1000) : 0;
            audioEl.currentTime = Math.max(minSec, audioEl.currentTime - 5);
          }
        });
      }

      // Practice QoL Transcript Toggle
      if (btnToggleTranscript) {
        btnToggleTranscript.addEventListener("click", function () {
          if (!transcriptDrawer) return;
          var isExpanded = transcriptDrawer.style.display !== "none";
          transcriptDrawer.style.display = isExpanded ? "none" : "";
          btnToggleTranscript.setAttribute("aria-expanded", isExpanded ? "false" : "true");
        });
      }

      function renderQuestion(order) {
        clearError();
        var q = localQuestions.find(function (item) { return item.displayOrder === order; });
        if (!q) return;

        currentOrder = order;
        var s = localState[q.questionId] || {};
        var type = (s.questionType || 'mcq').toLowerCase();

        // 1. Meta row
        if (qNumberDisplay) qNumberDisplay.textContent = "Question " + q.displayOrder;
        if (qCategoryDisplay) {
          if (q.categoryName) {
            qCategoryDisplay.textContent = q.categoryName;
            qCategoryDisplay.style.display = "";
          } else {
            qCategoryDisplay.style.display = "none";
          }
        }

        // Status badge
        if (statusBadge) {
          if (s.isCorrect === true) {
            statusBadge.className = "badge badge--success";
            statusBadge.textContent = "Accuracy · Correct";
            statusBadge.style.display = "";
          } else if (s.isCorrect === false) {
            statusBadge.className = "badge badge--danger";
            statusBadge.textContent = "Incorrect";
            statusBadge.style.display = "";
          } else if (s.isSkipped) {
            statusBadge.className = "badge badge--muted";
            statusBadge.textContent = "Skipped";
            statusBadge.style.display = "";
          } else {
            statusBadge.style.display = "none";
          }
        }

        if (hintMetaBadge) {
          hintMetaBadge.style.display = s.hintUsed ? "" : "none";
        }

        // Save button
        if (saveToggleBtn) {
          saveToggleBtn.setAttribute("data-question-id", String(q.questionId));
          saveToggleBtn.setAttribute("data-is-saved", s.isSaved ? "true" : "false");
          saveToggleBtn.setAttribute("aria-pressed", s.isSaved ? "true" : "false");
          saveToggleBtn.textContent = s.isSaved ? "Saved" : "Save question";
          saveToggleBtn.className = "btn btn--ghost btn--sm " + (s.isSaved ? "btn--primary" : "");
          saveToggleBtn.title = s.isSaved ? "Question saved to bank. Click to remove." : "Save this question to your bank.";
        }

        // 2. Passage Card
        if (passageCard && passageText) {
          if (q.passage) {
            passageText.textContent = q.passage;
            passageCard.style.display = "";
          } else {
            passageCard.style.display = "none";
          }
        }

        // 3. Image Container
        if (imageContainer && questionImage) {
          if (q.imagePath) {
            questionImage.src = q.imagePath;
            questionImage.alt = q.imageAlt || "Question illustration";
            imageContainer.style.display = "";
          } else {
            imageContainer.style.display = "none";
          }
        }

        // 4. Audio Continuity & QoL Controls
        if (audioWrapper) {
          if (q.audioPath) {
            var currentAudioEl = audioWrapper.querySelector(".exam-audio");
            var needsReload = true;
            if (currentAudioEl) {
              var prevSrc = currentAudioEl.querySelector(".exam-audio__native")?.getAttribute("src");
              var prevStart = currentAudioEl.dataset.startMs;
              var prevEnd = currentAudioEl.dataset.endMs;
              var sameSrc = prevSrc === q.audioPath;
              var sameStart = (prevStart == null && q.audioStartMs == null) || (String(prevStart) === String(q.audioStartMs));
              var sameEnd = (prevEnd == null && q.audioEndMs == null) || (String(prevEnd) === String(q.audioEndMs));
              if (sameSrc && sameStart && sameEnd) {
                needsReload = false;
              }
            }
            if (needsReload) {
              try {
                var audioTarget = audioPlayerHost || audioWrapper;
                if (window.stopExamAudio) window.stopExamAudio(audioTarget);
                if (window.createExamAudioHtml) {
                  audioTarget.innerHTML = window.createExamAudioHtml({
                    audioPath: q.audioPath,
                    mode: "study",
                    startMs: q.audioStartMs,
                    endMs: q.audioEndMs,
                    playerId: "main-practice-audio"
                  });
                  if (window.initExamAudio) window.initExamAudio();
                }
              } catch (e) {
                console.warn("Audio re-arm error:", e);
              }
            }
            audioWrapper.style.display = "";

            // QoL audio transcript button
            if (btnToggleTranscript && transcriptDrawer && transcriptDrawerText) {
              if (q.transcript) {
                btnToggleTranscript.style.display = "";
                transcriptDrawerText.textContent = q.transcript;
              } else {
                btnToggleTranscript.style.display = "none";
                transcriptDrawer.style.display = "none";
              }
            }
          } else {
            if (window.stopExamAudio) window.stopExamAudio(audioWrapper);
            audioWrapper.style.display = "none";
          }
        }

        // 5. Question Stem
        if (qStemDisplay) qStemDisplay.textContent = q.stem;

        // 6. Interactive 11-Component Inputs
        var isAnswered = s.isAnswered;

        // Clear both hosts before the new question renders so a previous question's controls can
        // never survive as hidden-but-focusable DOM, and a stale radio can't look like a current
        // answer (ticket d08).
        if (optionList) optionList.innerHTML = "";
        if (componentContainer) componentContainer.innerHTML = "";

        if (type === "mcq" || type === "mcq_3") {
          if (optionList) optionList.style.display = "";
          if (componentContainer) componentContainer.style.display = "none";

          if (optionList) {
            q.options.forEach(function (opt) {
              var isSelected = opt.optionId === s.selectedOptionId;
              var isCorrectOpt = s.correctOptionId != null && opt.optionId === s.correctOptionId;

              var optClass = "option";
              if (isAnswered) {
                if (isCorrectOpt) optClass += " option--correct";
                else if (isSelected && !s.isCorrect) optClass += " option--wrong";
              } else if (isSelected) {
                optClass += " is-selected";
              }

              var lbl = document.createElement("label");
              lbl.className = optClass;
              lbl.id = "opt-label-" + opt.optionId;

              var radio = document.createElement("input");
              radio.className = "option__input js-option-radio";
              radio.type = "radio";
              radio.name = "selectedOptionId";
              radio.value = String(opt.optionId);
              if (isSelected) radio.checked = true;
              if (isAnswered) radio.disabled = true;

              radio.addEventListener("change", function () {
                if (!isAnswered) {
                  s.selectedOptionId = opt.optionId;
                  optionList.querySelectorAll(".option").forEach(function (el) {
                    el.classList.remove("is-selected");
                  });
                  lbl.classList.add("is-selected");
                  updateSubmitButtonState();
                }
              });

              var marker = document.createElement("span");
              marker.className = "option__marker";
              marker.textContent = opt.label;

              var txt = document.createElement("span");
              txt.className = "option__text";
              txt.textContent = opt.text;

              lbl.appendChild(radio);
              lbl.appendChild(marker);
              lbl.appendChild(txt);

              if (isAnswered) {
                if (isCorrectOpt && isSelected) {
                  var cBadge = document.createElement("span");
                  cBadge.className = "badge badge--success";
                  cBadge.textContent = "Your answer · correct";
                  lbl.appendChild(cBadge);
                } else if (isCorrectOpt) {
                  var corBadge = document.createElement("span");
                  corBadge.className = "badge badge--success";
                  corBadge.textContent = "Correct answer";
                  lbl.appendChild(corBadge);
                } else if (isSelected) {
                  var wBadge = document.createElement("span");
                  wBadge.className = "badge badge--danger";
                  wBadge.textContent = "Your answer";
                  lbl.appendChild(wBadge);
                }
              }

              optionList.appendChild(lbl);
            });
          }
        } else {
          // Non-MCQ Component Widgets
          if (optionList) optionList.style.display = "none";
          if (componentContainer) {
            componentContainer.style.display = "block";
            renderComponentWidget(q, s);
          }
        }

        // 7. Hint Box
        if (hintBox && hintText) {
          if (s.hintUsed && s.hint) {
            hintText.textContent = s.hint;
            hintBox.style.display = "";
          } else {
            hintText.textContent = "";
            hintBox.style.display = "none";
          }
        }

        // 8. Practice QoL Explanation Accordion
        renderExplanation(s, q);

        // 9. Buttons & Bottom Nav
        if (btnPrev) btnPrev.disabled = currentOrder <= 1;
        if (btnSkip) btnSkip.style.display = s.isAnswered ? "none" : "";
        if (showHintBtn) {
          // Re-enable: revealHint disables while the request is in flight, and a phase change
          // must not leave the control dead for the rest of the session (ticket d06).
          showHintBtn.disabled = false;
          showHintBtn.style.display = (s.hintUsed || s.isAnswered) ? "none" : "";
        }
        if (hintUsedMsg) {
          // Only claim a hint was used when the question actually has hint text to show.
          hintUsedMsg.style.display = (s.hintUsed && s.hint) ? "" : "none";
        }

        updateSubmitButtonState();
        updateNavigatorCells();
        updateAccuracyAndStats();

        // 10. URL History replace (zero reload)
        try {
          var targetUrl = "/practice/" + sessionId + "?q=" + currentOrder;
          if (window.location.pathname + window.location.search !== targetUrl) {
            window.history.replaceState({ order: currentOrder }, "", targetUrl);
          }
        } catch (e) {}
      }

      function renderComponentWidget(q, s) {
        if (!componentContainer) return;
        var type = (s.questionType || 'mcq').toLowerCase();
        var isAnswered = s.isAnswered;

        if (type === "multi_select") {
          var maxSel = s.maxSelections || 2;
          var selectedIds = s.selectedOptionIds || [];

          var html = '<div class="multi-select-widget stack" style="gap: var(--space-2);">';
          html += '<div class="alert alert--info py-1 px-2" style="font-size: 0.85rem; font-weight: 600; border-radius: 0; box-shadow: none;">';
          html += 'CHỌN TỐI ĐA ' + maxSel + ' ĐÁP ÁN (SELECT UP TO ' + maxSel + ' ANSWERS): Đã chọn <span id="ms-count-' + q.questionId + '">' + selectedIds.length + '</span>/' + maxSel;
          html += '</div>';
          html += '<div class="option-list" role="group" aria-label="Multiple choice options for question ' + q.displayOrder + '">';

          var correctSet = new Set(s.correctOptionIds || []);
          if (s.correctOptionId) correctSet.add(s.correctOptionId);

          (q.options || []).forEach(function (opt) {
            var isChecked = selectedIds.indexOf(opt.optionId) !== -1;
            var isCorrectOpt = isAnswered && correctSet.has(opt.optionId);
            var isWrongOpt = isAnswered && isChecked && !isCorrectOpt;

            var optClass = "option option--checkbox";
            if (isAnswered) {
              if (isCorrectOpt) optClass += " option--correct";
              else if (isWrongOpt) optClass += " option--wrong";
            } else if (isChecked) {
              optClass += " is-selected";
            }

            var isDisabled = isAnswered || (!isChecked && selectedIds.length >= maxSel);

            html += '<label class="' + optClass + '" style="' + (isDisabled && !isAnswered ? 'opacity: 0.6; cursor: not-allowed;' : '') + '">';
            html += '<input type="checkbox" class="option__input js-option-checkbox" value="' + opt.optionId + '" ' + (isChecked ? 'checked' : '') + ' ' + (isDisabled ? 'disabled' : '') + '>';
            html += '<span class="option__marker">' + escapeHtml(opt.label) + '</span>';
            html += '<span class="option__text">' + escapeHtml(opt.text) + '</span>';

            if (isAnswered) {
              if (isCorrectOpt && isChecked) {
                html += '<span class="badge badge--success">Your answer · correct</span>';
              } else if (isCorrectOpt) {
                html += '<span class="badge badge--success">Correct answer</span>';
              } else if (isChecked) {
                html += '<span class="badge badge--danger">Your answer</span>';
              }
            }

            html += '</label>';
          });

          html += '</div></div>';
          componentContainer.innerHTML = html;

          if (!isAnswered) {
            var checkboxes = componentContainer.querySelectorAll('.js-option-checkbox');
            checkboxes.forEach(function (cb) {
              cb.addEventListener('change', function () {
                var optId = parseInt(cb.value, 10);
                var idx = selectedIds.indexOf(optId);
                if (cb.checked) {
                  if (idx === -1 && selectedIds.length < maxSel) {
                    selectedIds.push(optId);
                  } else if (idx === -1 && selectedIds.length >= maxSel) {
                    cb.checked = false;
                    return;
                  }
                } else {
                  if (idx !== -1) selectedIds.splice(idx, 1);
                }

                s.selectedOptionIds = selectedIds;
                s.selectedOptionId = selectedIds[0] || null;
                s.selectedOptionIdsJson = JSON.stringify(selectedIds);

                var countEl = document.getElementById('ms-count-' + q.questionId);
                if (countEl) countEl.textContent = selectedIds.length;

                checkboxes.forEach(function (otherCb) {
                  var otherId = parseInt(otherCb.value, 10);
                  var otherChecked = selectedIds.indexOf(otherId) !== -1;
                  var parentLabel = otherCb.closest('.option');
                  if (parentLabel) parentLabel.classList.toggle('is-selected', otherChecked);
                  if (!otherChecked) {
                    otherCb.disabled = selectedIds.length >= maxSel;
                    if (parentLabel) parentLabel.style.opacity = selectedIds.length >= maxSel ? '0.6' : '';
                    if (parentLabel) parentLabel.style.cursor = selectedIds.length >= maxSel ? 'not-allowed' : '';
                  }
                });

                updateSubmitButtonState();
              });
            });
          }
        } else if (type === "inline_completion") {
          var maxWords = s.maxWords;
          var allowNumber = s.allowNumber !== false;
          var curText = s.responseText || '';

          var html = '<div class="inline-completion-widget stack" style="gap: var(--space-2);">';
          html += '<div style="display: flex; gap: 0.5rem; flex-wrap: wrap;">';
          html += '<span class="badge badge--neutral" style="font-family: \'IBM Plex Mono\', monospace; border-radius: 0; box-shadow: none;">' + (maxWords ? 'TỐI ĐA ' + maxWords + ' TỪ' : 'ĐIỀN TỪ') + '</span>';
          html += '<span class="badge badge--neutral" style="font-family: \'IBM Plex Mono\', monospace; border-radius: 0; box-shadow: none;">' + (allowNumber ? 'CHỮ VÀ SỐ' : 'CHỈ CHỮ (NO NUMBERS)') + '</span>';
          html += '</div>';
          html += '<div class="mt-2">';
          html += '<input type="text" class="completion-input js-completion-input" value="' + escapeHtml(curText) + '" placeholder="Nhập câu trả lời..." autocomplete="off" spellcheck="false" ' + (isAnswered ? 'disabled' : '') + '>';
          html += '<div class="text-xs text-muted mt-1">';
          html += 'Số từ: <strong class="js-comp-word-count">' + (curText.trim() ? curText.trim().split(/\s+/).length : 0) + '</strong>' + (maxWords ? ' / ' + maxWords + ' từ tối đa' : '') + '<span class="js-comp-warning ml-2" style="color: #b31217; display: none;"></span>';
          html += '</div></div></div>';
          componentContainer.innerHTML = html;

          var inputEl = componentContainer.querySelector('.js-completion-input');
          var wordCountEl = componentContainer.querySelector('.js-comp-word-count');
          var warningEl = componentContainer.querySelector('.js-comp-warning');

          if (!isAnswered && inputEl) {
            inputEl.addEventListener('input', function () {
              var val = inputEl.value;
              if (!allowNumber && /\d/.test(val)) {
                val = val.replace(/\d/g, '');
                inputEl.value = val;
              }
              s.responseText = val;
              var words = val.trim() ? val.trim().split(/\s+/).length : 0;
              if (wordCountEl) wordCountEl.textContent = words;
              if (maxWords && words > maxWords) {
                if (warningEl) {
                  warningEl.textContent = ' (Vượt quá số từ cho phép)';
                  warningEl.style.display = 'inline';
                }
              } else {
                if (warningEl) warningEl.style.display = 'none';
              }
              updateSubmitButtonState();
            });
          }
        } else if (type === "dropdown") {
          var selectedOptId = s.selectedOptionId;
          var html = '<div class="dropdown-widget stack" style="gap: var(--space-2);">';
          html += '<label class="text-sm font-sign uppercase" style="font-weight:700;">Chọn đáp án phù hợp từ danh sách bên dưới:</label>';
          html += '<select class="completion-input js-dropdown-select" style="max-width: 480px; background: #fff; cursor: pointer;" ' + (isAnswered ? 'disabled' : '') + '>';
          html += '<option value="">-- Chọn đáp án / Select Option --</option>';
          (q.options || []).forEach(function (opt) {
            var isSel = opt.optionId === selectedOptId;
            html += '<option value="' + opt.optionId + '" ' + (isSel ? 'selected' : '') + '>' + escapeHtml(opt.label) + '. ' + escapeHtml(opt.text) + '</option>';
          });
          html += '</select></div>';
          componentContainer.innerHTML = html;

          var selectEl = componentContainer.querySelector('.js-dropdown-select');
          if (!isAnswered && selectEl) {
            selectEl.addEventListener('change', function () {
              var val = parseInt(selectEl.value, 10);
              s.selectedOptionId = !isNaN(val) ? val : null;
              updateSubmitButtonState();
            });
          }
        } else if (type === "drag_drop") {
          var selectedOptId = s.selectedOptionId;
          var placedOption = (q.options || []).find(function (o) { return o.optionId === selectedOptId || o.OptionId === selectedOptId; });

          var html = '<div class="drag-drop-widget stack" style="gap: var(--space-3);">';
          html += '<div>';
          html += '<span class="text-xs uppercase text-muted font-sign" style="font-weight: 700; letter-spacing: 0.05em; display: block; margin-bottom: 0.5rem;">NGÂN HÀNG TỪ / WORD BANK (Bấm hoặc kéo thả):</span>';
          html += '<div class="word-bank-chips" style="display: flex; flex-wrap: wrap; gap: 0.5rem; border: 2px dashed #14181d; padding: 0.75rem; background: #fbfcfd;">';
          (q.options || []).forEach(function (opt) {
            var isPlaced = placedOption && (placedOption.optionId === opt.optionId || placedOption.OptionId === opt.optionId);
            html += '<button type="button" class="chip-item js-drag-chip ' + (isPlaced ? 'is-placed' : '') + '" draggable="' + (!isAnswered ? 'true' : 'false') + '" data-option-id="' + opt.optionId + '" ' + (isAnswered ? 'disabled' : '') + '>';
            html += '<strong style="font-family: \'IBM Plex Mono\', monospace; margin-right: 0.35rem;">' + escapeHtml(opt.label) + '</strong>' + escapeHtml(opt.text);
            html += '</button>';
          });
          html += '</div></div>';

          html += '<div>';
          html += '<span class="text-xs uppercase text-muted font-sign" style="font-weight: 700; letter-spacing: 0.05em; display: block; margin-bottom: 0.5rem;">VỊ TRÍ ĐẶT ĐÁP ÁN / DROP ZONE:</span>';
          html += '<div class="drop-zone js-drop-zone">';
          if (placedOption) {
            html += '<div class="placed-chip-content" style="display: flex; align-items: center; gap: 0.5rem; font-weight: 700;">';
            html += '<span class="badge badge--info" style="border-radius: 0; box-shadow: none;">' + escapeHtml(placedOption.label) + '</span>';
            html += '<span>' + escapeHtml(placedOption.text) + '</span>';
            html += '</div>';
            if (!isAnswered) {
              html += '<button type="button" class="btn btn--ghost btn--sm js-remove-placed" style="color: #b31217; font-weight: 700;">[X] Xóa</button>';
            }
          } else {
            html += '<span class="text-muted text-sm js-drop-placeholder">' + (!isAnswered ? 'Kéo thả từ vào đây hoặc bấm vào từ ở trên' : 'Không có lựa chọn') + '</span>';
          }
          html += '</div></div></div>';
          componentContainer.innerHTML = html;

          if (!isAnswered) {
            var chips = componentContainer.querySelectorAll('.js-drag-chip');
            var dropZone = componentContainer.querySelector('.js-drop-zone');
            var removeBtn = componentContainer.querySelector('.js-remove-placed');

            function placeChip(optId) {
              s.selectedOptionId = optId;
              renderComponentWidget(q, s);
              updateSubmitButtonState();
            }

            function clearChip() {
              s.selectedOptionId = null;
              renderComponentWidget(q, s);
              updateSubmitButtonState();
            }

            chips.forEach(function (chip) {
              chip.addEventListener('click', function () {
                var optId = parseInt(chip.getAttribute('data-option-id'), 10);
                placeChip(optId);
              });
              chip.addEventListener('dragstart', function (e) {
                var optId = chip.getAttribute('data-option-id');
                e.dataTransfer.setData('text/plain', optId);
              });
            });

            if (dropZone) {
              dropZone.addEventListener('dragover', function (e) {
                e.preventDefault();
                dropZone.classList.add('is-hovered');
              });
              dropZone.addEventListener('dragleave', function () {
                dropZone.classList.remove('is-hovered');
              });
              dropZone.addEventListener('drop', function (e) {
                e.preventDefault();
                dropZone.classList.remove('is-hovered');
                var optId = parseInt(e.dataTransfer.getData('text/plain'), 10);
                if (!isNaN(optId)) placeChip(optId);
              });
            }

            if (removeBtn) {
              removeBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                clearChip();
              });
            }
          }
        }
      }

      function renderExplanation(s, q) {
        if (!explanationContainer) return;
        explanationContainer.innerHTML = "";

        if (s.isAnswered && (s.explanation || s.acceptedAnswersJson || s.transcript)) {
          var wrap = document.createElement("div");
          wrap.className = "accordion-explanation";
          wrap.id = "accordion-explanation";

          var headerBtn = document.createElement("button");
          headerBtn.type = "button";
          headerBtn.className = "accordion-explanation__header";
          headerBtn.id = "btn-toggle-explanation";
          headerBtn.setAttribute("aria-expanded", "true");

          var headerTitle = document.createElement("span");
          headerTitle.textContent = "[Xem Lời giải chi tiết / Explanation & Rationale]";
          var chevron = document.createElement("span");
          chevron.id = "exp-chevron";
          chevron.textContent = "[-]";

          headerBtn.appendChild(headerTitle);
          headerBtn.appendChild(chevron);
          wrap.appendChild(headerBtn);

          var bodyDiv = document.createElement("div");
          bodyDiv.className = "accordion-explanation__body";
          bodyDiv.id = "explanation-body";

          if (s.explanation) {
            var expTitle = document.createElement("div");
            expTitle.className = "font-sign uppercase text-xs mb-1";
            expTitle.style.fontWeight = "700";
            expTitle.textContent = "Giải thích chi tiết / Rationale:";
            bodyDiv.appendChild(expTitle);

            var expP = document.createElement("p");
            expP.className = "no-margin";
            expP.style.whiteSpace = "pre-line";
            expP.textContent = s.explanation;
            bodyDiv.appendChild(expP);
          }

          if (s.acceptedAnswersJson) {
            var accDiv = document.createElement("div");
            accDiv.className = "mt-3";
            var accTitle = document.createElement("span");
            accTitle.className = "font-sign uppercase text-xs text-muted";
            accTitle.style.display = "block";
            accTitle.style.marginBottom = "0.25rem";
            accTitle.style.fontWeight = "700";
            accTitle.textContent = "Đáp án chấp nhận / Accepted answers:";
            accDiv.appendChild(accTitle);

            var codeEl = document.createElement("code");
            codeEl.className = "font-mono";
            codeEl.style.padding = "0.25rem 0.5rem";
            codeEl.style.background = "#f4f6f8";
            codeEl.style.border = "1px solid #ccd2d6";
            codeEl.style.display = "inline-block";
            codeEl.textContent = s.acceptedAnswersJson;
            accDiv.appendChild(codeEl);
            bodyDiv.appendChild(accDiv);
          }

          if (s.transcript) {
            var trDiv = document.createElement("div");
            trDiv.className = "explain__transcript mt-3 pt-2";
            trDiv.style.borderTop = "1px dashed #ccd2d6";

            var trStrong = document.createElement("strong");
            trStrong.className = "font-sign uppercase text-xs";
            trStrong.textContent = "Transcript: ";
            trDiv.appendChild(trStrong);

            var trSpan = document.createElement("span");
            trSpan.style.whiteSpace = "pre-line";
            trSpan.textContent = s.transcript;
            trDiv.appendChild(trSpan);

            bodyDiv.appendChild(trDiv);
          }

          wrap.appendChild(bodyDiv);

          headerBtn.addEventListener("click", function () {
            var isOpen = bodyDiv.style.display !== "none";
            bodyDiv.style.display = isOpen ? "none" : "";
            chevron.textContent = isOpen ? "[+]" : "[-]";
            headerBtn.setAttribute("aria-expanded", isOpen ? "false" : "true");
          });

          explanationContainer.appendChild(wrap);
        }
      }

      function updateSubmitButtonState() {
        if (!submitBtn) return;
        var q = getCurrentQuestion();
        var s = localState[q.questionId] || {};
        var type = (s.questionType || 'mcq').toLowerCase();

        if (s.isAnswered) {
          submitBtn.disabled = false;
          if (currentOrder >= totalQuestions) {
            submitBtn.textContent = "View summary";
            submitBtn.className = "btn btn--primary btn--lg";
          } else {
            submitBtn.textContent = "Next question";
            submitBtn.className = "btn btn--primary btn--lg";
          }
        } else {
          submitBtn.textContent = "Check answer";
          submitBtn.className = "btn btn--primary btn--lg";

          var hasInput = false;
          if (type === "mcq" || type === "mcq_3" || type === "dropdown" || type === "drag_drop") {
            hasInput = s.selectedOptionId != null;
          } else if (type === "multi_select") {
            hasInput = s.selectedOptionIds && s.selectedOptionIds.length > 0;
          } else if (type === "inline_completion") {
            hasInput = s.responseText && s.responseText.trim().length > 0;
          }
          submitBtn.disabled = !hasInput;
        }
      }

      function updateNavigatorCells() {
        localQuestions.forEach(function (q) {
          var s = localState[q.questionId] || {};
          var cell = document.getElementById("nav-cell-" + q.questionId);
          if (!cell) return;

          cell.classList.remove("is-current", "is-correct", "is-incorrect", "is-unanswered", "is-skipped", "is-marked");

          if (q.displayOrder === currentOrder) cell.classList.add("is-current");

          if (s.isCorrect === true) cell.classList.add("is-correct");
          else if (s.isCorrect === false) cell.classList.add("is-incorrect");
          else if (s.isSkipped) cell.classList.add("is-skipped");
          else cell.classList.add("is-unanswered");

          if (s.isSaved) cell.classList.add("is-marked");
        });

        applyFilter(activeFilter);
      }

      function applyFilter(filter) {
        activeFilter = filter;
        document.querySelectorAll("#navigator-filters button").forEach(function (btn) {
          btn.classList.toggle("btn--primary", btn.dataset.filter === filter);
          btn.classList.toggle("btn--ghost", btn.dataset.filter !== filter);
        });

        var visibleCount = 0;
        localQuestions.forEach(function (q) {
          var s = localState[q.questionId] || {};
          var cell = document.getElementById("nav-cell-" + q.questionId);
          if (!cell) return;

          var match = true;
          if (filter === "incorrect") match = s.isCorrect === false;
          else if (filter === "saved") match = s.isSaved === true;

          cell.style.display = match ? "" : "none";
          if (match) visibleCount++;
        });

        if (filterEmptyMsg) {
          if (visibleCount === 0) {
            if (filter === "incorrect") filterEmptyMsg.textContent = "No incorrect questions yet — great job!";
            else if (filter === "saved") filterEmptyMsg.textContent = "No questions bookmarked yet. Press S to save.";
            else filterEmptyMsg.textContent = "No questions match this filter.";
            filterEmptyMsg.style.display = "";
          } else {
            filterEmptyMsg.style.display = "none";
          }
        }
      }

      function checkAnswer() {
        var q = getCurrentQuestion();
        var s = localState[q.questionId];
        if (!s || s.isAnswered) return;

        clearError();
        submitBtn.disabled = true;

        var formData = new URLSearchParams();
        formData.append("__RequestVerificationToken", csrfToken);
        if (s.selectedOptionId != null) {
          formData.append("SelectedOptionId", s.selectedOptionId);
        }
        if (s.responseText) {
          formData.append("ResponseText", s.responseText);
        }
        if (s.selectedOptionIds && s.selectedOptionIds.length > 0) {
          formData.append("SelectedOptionIdsJson", JSON.stringify(s.selectedOptionIds));
          s.selectedOptionIds.forEach(function (id) {
            formData.append("SelectedOptionIds", id);
          });
        }

        fetch("/practice/" + sessionId + "/questions/" + q.questionId + "/answer?displayOrder=" + q.displayOrder, {
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "RequestVerificationToken": csrfToken,
            "X-Requested-With": "XMLHttpRequest",
            "Accept": "application/json"
          },
          body: formData.toString()
        })
        .then(function (res) {
          if (!res.ok) {
            return res.json().then(function (errData) {
              var err = new Error(errData.error || ("Server returned HTTP " + res.status));
              err.code = errData.code;
              throw err;
            }).catch(function (fallback) {
              if (fallback.code) throw fallback;
              throw new Error("Server returned HTTP " + res.status);
            });
          }
          return res.json();
        })
        .then(function (data) {
          s.isAnswered = true;
          s.isCorrect = data.isCorrect;
          s.selectedOptionId = data.selectedOptionId;
          s.correctOptionId = data.correctOptionId;
          s.correctOptionIds = data.correctOptionIds || [];
          s.explanation = data.explanation;
          s.transcript = data.transcript;
          s.acceptedAnswersJson = data.acceptedAnswersJson;
          if (data.isSaved) s.isSaved = true;
          if (data.isSessionFinished) isSessionFinished = true;

          renderQuestion(currentOrder);
        })
        .catch(function (err) {
          console.error("Failed to submit answer", err);
          submitBtn.disabled = false;
          showError(err.message || "Your answer could not be verified. Please check connection and try again.");
        });
      }

      function skipQuestion() {
        var q = getCurrentQuestion();
        var s = localState[q.questionId];
        if (!s || s.isAnswered) return;

        clearError();
        if (btnSkip) btnSkip.disabled = true;

        var formData = new URLSearchParams();
        formData.append("__RequestVerificationToken", csrfToken);
        formData.append("Action", "skip");

        fetch("/practice/" + sessionId + "/questions/" + q.questionId + "/answer?displayOrder=" + q.displayOrder, {
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "RequestVerificationToken": csrfToken,
            "X-Requested-With": "XMLHttpRequest",
            "Accept": "application/json"
          },
          body: formData.toString()
        })
        .then(function (res) {
          if (!res.ok) throw new Error("Skip failed");
          return res.json();
        })
        .then(function (data) {
          s.isAnswered = true;
          s.isSkipped = true;
          s.correctOptionId = data.correctOptionId;
          s.correctOptionIds = data.correctOptionIds || [];
          s.explanation = data.explanation;
          s.transcript = data.transcript;
          s.acceptedAnswersJson = data.acceptedAnswersJson;
          if (data.isSessionFinished) isSessionFinished = true;

          if (currentOrder < totalQuestions) {
            renderQuestion(currentOrder + 1);
          } else {
            renderQuestion(currentOrder);
          }
        })
        .catch(function (err) {
          console.error("Skip error:", err);
          if (btnSkip) btnSkip.disabled = false;
          showError("Could not skip question. Please try again.");
        });
      }

      function revealHint() {
        var q = getCurrentQuestion();
        var s = localState[q.questionId];
        if (!s || s.hintUsed || s.isAnswered) return;

        clearError();
        if (showHintBtn) showHintBtn.disabled = true;

        fetch("/practice/" + sessionId + "/questions/" + q.questionId + "/hint", {
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "RequestVerificationToken": csrfToken,
            "X-Requested-With": "XMLHttpRequest",
            "Accept": "application/json"
          },
          body: "__RequestVerificationToken=" + encodeURIComponent(csrfToken)
        })
        .then(function (res) {
          if (!res.ok) throw new Error("Hint fetch failed");
          return res.json();
        })
        .then(function (data) {
          s.hintUsed = true;
          s.hint = data.hint;
          renderQuestion(currentOrder);
        })
        .catch(function (err) {
          console.error("Hint error:", err);
          if (showHintBtn) showHintBtn.disabled = false;
          showError("Could not reveal hint. Please try again.");
        });
      }

      function toggleSaveQuestion() {
        var q = getCurrentQuestion();
        var s = localState[q.questionId];
        if (!s) return;

        var isCurrentlySaved = s.isSaved;
        if (saveToggleBtn) saveToggleBtn.disabled = true;

        var formData = new URLSearchParams();
        formData.append("__RequestVerificationToken", csrfToken);
        formData.append("currentlySaved", isCurrentlySaved ? "true" : "false");

        fetch("/saved-questions/" + q.questionId + "/toggle", {
          method: "POST",
          headers: {
            "Content-Type": "application/x-www-form-urlencoded",
            "RequestVerificationToken": csrfToken,
            "X-Requested-With": "XMLHttpRequest",
            "Accept": "application/json"
          },
          body: formData.toString()
        })
        .then(function (res) {
          if (!res.ok) throw new Error("Toggle saved failed");
          return res.json();
        })
        .then(function (resData) {
          if (saveToggleBtn) saveToggleBtn.disabled = false;
          s.isSaved = resData.isSaved;
          renderQuestion(currentOrder);
          showToast(resData.isSaved ? "Question saved to your bank" : "Question removed from your bank");
        })
        .catch(function (err) {
          console.error("Save toggle error:", err);
          if (saveToggleBtn) saveToggleBtn.disabled = false;
          showError("Could not update bookmark. Please try again.");
        });
      }

      // Event Listeners
      if (btnPrev) {
        btnPrev.addEventListener("click", function () {
          if (currentOrder > 1) renderQuestion(currentOrder - 1);
        });
      }

      if (submitBtn) {
        submitBtn.addEventListener("click", function () {
          var q = getCurrentQuestion();
          var s = localState[q.questionId];
          if (s && s.isAnswered) {
            if (currentOrder < totalQuestions) {
              renderQuestion(currentOrder + 1);
            } else {
              window.location.href = "/practice/" + sessionId + "/summary";
            }
          } else {
            checkAnswer();
          }
        });
      }

      if (btnSkip) btnSkip.addEventListener("click", skipQuestion);
      if (showHintBtn) showHintBtn.addEventListener("click", revealHint);
      if (saveToggleBtn) saveToggleBtn.addEventListener("click", toggleSaveQuestion);

      // Navigator grid delegation
      var navGrid = document.getElementById("navigator-grid");
      if (navGrid) {
        navGrid.addEventListener("click", function (e) {
          var target = e.target.closest(".navigator__cell");
          if (!target || !target.dataset.displayOrder) return;
          var order = parseInt(target.dataset.displayOrder, 10);
          if (order > 0) renderQuestion(order);
        });
      }

      // Filter tabs delegation
      var filtersContainer = document.getElementById("navigator-filters");
      if (filtersContainer) {
        filtersContainer.addEventListener("click", function (e) {
          var target = e.target.closest("button[data-filter]");
          if (!target) return;
          applyFilter(target.dataset.filter);
        });
      }

      // Controls Modal toggles
      function openModal() {
        if (controlsModal) controlsModal.style.display = "flex";
      }
      function closeModal() {
        if (controlsModal) controlsModal.style.display = "none";
      }

      if (btnControlsModal) btnControlsModal.addEventListener("click", openModal);
      if (btnCloseControlsModal) btnCloseControlsModal.addEventListener("click", closeModal);
      if (btnModalGotIt) btnModalGotIt.addEventListener("click", closeModal);
      if (controlsModal) {
        controlsModal.addEventListener("click", function (e) {
          if (e.target === controlsModal) closeModal();
        });
      }

      // Keyboard navigation
      document.addEventListener("keydown", function (e) {
        var isModalOpen = controlsModal && controlsModal.style.display !== "none";
        if (isModalOpen) {
          if (e.key === "Escape" || e.key === "?") {
            e.preventDefault();
            closeModal();
          }
          return;
        }

        var activeTag = document.activeElement ? document.activeElement.tagName.toLowerCase() : "";
        if (activeTag === "input" || activeTag === "textarea" || activeTag === "select") return;
        if ((e.key === "Enter" || e.key === " ") && (activeTag === "button" || activeTag === "a")) return;

        if (e.key === "Escape") {
          closeModal();
          return;
        }

        if (e.key === "?") {
          e.preventDefault();
          openModal();
          return;
        }

        // Option selection: 1 - 4 for MCQ
        if (e.key === "1" || e.key === "2" || e.key === "3" || e.key === "4") {
          var q = getCurrentQuestion();
          var s = localState[q.questionId];
          if (!s || s.isAnswered) return;
          var type = (s.questionType || 'mcq').toLowerCase();
          if (type !== 'mcq' && type !== 'mcq_3') return;

          var index = parseInt(e.key, 10) - 1;
          if (index >= 0 && index < q.options.length) {
            e.preventDefault();
            var opt = q.options[index];
            s.selectedOptionId = opt.optionId;
            var radio = document.querySelector('input.js-option-radio[value="' + opt.optionId + '"]');
            if (radio) {
              radio.checked = true;
              optionList.querySelectorAll(".option").forEach(function (el) { el.classList.remove("is-selected"); });
              radio.closest(".option").classList.add("is-selected");
              updateSubmitButtonState();
            }
          }
          return;
        }

        // Enter or Space: Check Answer if unanswered, Next if answered
        if (e.key === "Enter" || e.key === " ") {
          var q = getCurrentQuestion();
          var s = localState[q.questionId];
          if (!s) return;

          e.preventDefault();
          if (!s.isAnswered) {
            checkAnswer();
          } else {
            if (currentOrder < totalQuestions) {
              renderQuestion(currentOrder + 1);
            } else {
              window.location.href = "/practice/" + sessionId + "/summary";
            }
          }
          return;
        }

        // H: Hint
        if (e.key === "h" || e.key === "H") {
          e.preventDefault();
          revealHint();
          return;
        }

        // S: Save
        if (e.key === "s" || e.key === "S") {
          e.preventDefault();
          toggleSaveQuestion();
          return;
        }

        // K: Skip
        if (e.key === "k" || e.key === "K") {
          e.preventDefault();
          skipQuestion();
          return;
        }

        // N: Next
        if (e.key === "n" || e.key === "N") {
          e.preventDefault();
          if (currentOrder < totalQuestions) renderQuestion(currentOrder + 1);
          return;
        }

        // P: Prev
        if (e.key === "p" || e.key === "P") {
          e.preventDefault();
          if (currentOrder > 1) renderQuestion(currentOrder - 1);
          return;
        }
      });

      // Initial render on page load
      renderQuestion(currentOrder);
    })();
