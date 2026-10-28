(function() {
  var modal = document.getElementById('quick-ingest-modal');
  var closeBtn = document.getElementById('quick-ingest-close');
  var cancelBtn = document.getElementById('quick-ingest-cancel');
  var sampleBtn = document.getElementById('qi-sample-btn');
  var parseBtn = document.getElementById('qi-parse-btn');
  var commitBtn = document.getElementById('quick-ingest-commit');
  var rawText = document.getElementById('qi-raw-text');
  var sectionSelect = document.getElementById('qi-section-select');
  var summaryBadge = document.getElementById('qi-summary-badge');
  var previewContainer = document.getElementById('qi-preview-container');
  var previewBody = document.getElementById('qi-preview-body');
  var commitStatus = document.getElementById('qi-commit-status');
  var bpTitle = document.getElementById('qi-bp-title');
  var bpBadges = document.getElementById('qi-bp-badges');
  var bpGuide = document.getElementById('qi-bp-guide');

  var currentExamId = null;
  var currentSections = [];
  var currentParsedResult = null;
  var onCommitCallback = null;

  function openModal(examId, sections, initialSectionId, callback) {
    currentExamId = examId;
    currentSections = sections || [];
    onCommitCallback = callback;

    sectionSelect.innerHTML = '';
    if (currentSections.length > 0) {
      currentSections.forEach(function(s) {
        var opt = document.createElement('option');
        opt.value = s.sectionId || s.SectionId;
        opt.textContent = '[' + (s.code || s.Code || '') + '] ' + (s.name || s.Name || '');
        if (s.sectionId === initialSectionId || s.SectionId === initialSectionId) {
          opt.selected = true;
        }
        sectionSelect.appendChild(opt);
      });
    }

    rawText.value = '';
    currentParsedResult = null;
    previewContainer.style.display = 'none';
    previewBody.innerHTML = '';
    summaryBadge.textContent = '';
    commitStatus.textContent = '';
    commitBtn.disabled = true;

    updateBlueprintPlacard();
    modal.style.display = 'flex';
  }

  function closeModal() {
    modal.style.display = 'none';
  }

  function getSelectedSection() {
    var secId = parseInt(sectionSelect.value, 10);
    if (isNaN(secId)) return null;
    return currentSections.find(function(s) {
      return (s.sectionId || s.SectionId) === secId;
    }) || null;
  }

  function updateBlueprintPlacard() {
    var sec = getSelectedSection();
    if (!sec) {
      bpTitle.textContent = 'Section Blueprint';
      bpBadges.innerHTML = '';
      bpGuide.textContent = 'Select a section to view its blueprint rules and tailored syntax.';
      return;
    }

    var code = sec.code || sec.Code || '';
    var name = sec.name || sec.Name || '';
    var optCount = sec.optionCount || sec.OptionCount || 4;
    var clusterSize = sec.clusterSize || sec.ClusterSize || 1;
    var requirePassage = sec.requirePassage ?? sec.RequirePassage ?? false;
    var requiredKeywordCount = sec.requiredKeywordCount ?? sec.RequiredKeywordCount ?? 0;
    var allowedJson = sec.allowedComponentTypesJson || sec.AllowedComponentTypesJson || '';
    var hideOptionText = sec.hideOptionText ?? sec.HideOptionText ?? false;

    bpTitle.textContent = '[' + code + '] ' + name + ' Blueprint';

    var badgesHtml = '';
    badgesHtml += '<span class="badge" style="background:#14181d; color:#fff;">' + optCount + ' Options (A-' + String.fromCharCode(64 + optCount) + ')</span>';
    if (clusterSize > 1) {
      badgesHtml += '<span class="badge" style="background:#0b4ea2; color:#fff;">Cluster Size: ' + clusterSize + '</span>';
    }
    if (requirePassage) {
      badgesHtml += '<span class="badge" style="background:#f2b705; color:#14181d; font-weight:bold;">Passage Required</span>';
    }
    if (requiredKeywordCount > 0) {
      badgesHtml += '<span class="badge" style="background:#0a6b3c; color:#fff;">' + requiredKeywordCount + ' Keywords Required</span>';
    }
    if (hideOptionText) {
      badgesHtml += '<span class="badge" style="background:#6c757d; color:#fff;">Audio-Only Spoken Keys</span>';
    }
    bpBadges.innerHTML = badgesHtml;

    // Tailored guidance text
    if (requiredKeywordCount > 0) {
      bpGuide.innerHTML = '<strong>Keyword Free-Response Mode:</strong> Paste prompts with <code>KEYWORDS: word1, word2</code> (' + requiredKeywordCount + ' keywords required). Optional: <code>MIN_WORDS</code>, <code>MAX_WORDS</code>.';
    } else if (allowedJson.indexOf('inline_completion') >= 0) {
      bpGuide.innerHTML = '<strong>Passage + Inline Completion Mode:</strong> Start clusters with <code>PASSAGE: ...</code>, then question stems with <code>ANSWER: variant1 | variant2</code> and <code>MAX_WORDS: 2</code>.';
    } else if (allowedJson.indexOf('multi_select') >= 0) {
      bpGuide.innerHTML = '<strong>Multi-Select Mode:</strong> Mark multiple correct choices with <code>*</code> or trailing <code>ANSWER: A, C</code>. Enforces options up to ' + optCount + ' choices.';
    } else if (optCount === 3 || hideOptionText) {
      bpGuide.innerHTML = '<strong>Rapid Audio Answer-Key Mode (3 Options A-C):</strong> Paste rapid answer keys (e.g. <code>1. A</code>, <code>2. C</code>, <code>3. B</code>) with optional <code>TRANSCRIPT: ...</code>. Choice D is disallowed.';
    } else if (requirePassage || clusterSize > 1) {
      bpGuide.innerHTML = '<strong>Clustered Reading Comprehension Mode:</strong> Start each cluster with <code>PASSAGE: ...</code> followed by ' + clusterSize + ' questions with 4 options (A-D) each.';
    } else {
      bpGuide.innerHTML = '<strong>Standard MCQ Mode:</strong> Paste questions with <code>*A. Correct</code> or trailing <code>ANSWER: B</code>. Enforces ' + optCount + ' options (A-' + String.fromCharCode(64 + optCount) + ').';
    }
  }

  sectionSelect.addEventListener('change', function() {
    updateBlueprintPlacard();
    if (rawText.value.trim().length > 0) {
      parseQuestions();
    }
  });

  closeBtn.addEventListener('click', closeModal);
  cancelBtn.addEventListener('click', closeModal);
  modal.addEventListener('click', function(e) {
    if (e.target === modal) closeModal();
  });

  sampleBtn.addEventListener('click', function() {
    var sec = getSelectedSection();
    if (!sec) return;

    var secId = sec.sectionId || sec.SectionId;
    sampleBtn.disabled = true;
    sampleBtn.textContent = 'Loading...';

    fetch('/admin/exams/sections/' + secId + '/quick-ingest/sample')
      .then(function(res) {
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
      })
      .then(function(data) {
        rawText.value = data.template || '';
        sampleBtn.disabled = false;
        sampleBtn.textContent = 'Load Section Sample';
        parseQuestions();
      })
      .catch(function() {
        // Fallback local blueprint template
        rawText.value = generateFallbackSample(sec);
        sampleBtn.disabled = false;
        sampleBtn.textContent = 'Load Section Sample';
        parseQuestions();
      });
  });

  function generateFallbackSample(sec) {
    var optCount = sec.optionCount || sec.OptionCount || 4;
    var requirePassage = sec.requirePassage ?? sec.RequirePassage ?? false;
    var reqKw = sec.requiredKeywordCount ?? sec.RequiredKeywordCount ?? 0;
    var allowed = sec.allowedComponentTypesJson || sec.AllowedComponentTypesJson || '';

    if (reqKw > 0) {
      return 'PROMPT: Write a sentence based on the photograph using the two keywords provided.\nKEYWORDS: photograph, yesterday\nMIN_WORDS: 5\nMAX_WORDS: 30\n\nPROMPT: Write a sentence based on the photograph using the two keywords provided.\nKEYWORDS: airport, luggage\nMIN_WORDS: 5\nMAX_WORDS: 30';
    }
    if (allowed.indexOf('inline_completion') >= 0) {
      return 'PASSAGE: The university botanical garden is open daily from 9:00 AM to 5:00 PM. Admission is free for university students and staff with valid identification.\n\n1. What time does the botanical garden open each day?\nANSWER: 9:00 AM | 9 AM | 9am\nMAX_WORDS: 2\n\n2. What is required for students to receive free admission?\nANSWER: valid identification | valid ID | ID\nMAX_WORDS: 2';
    }
    if (optCount === 3) {
      return '1. A\nTRANSCRIPT: Where is the nearest bus stop?\n2. C\nTRANSCRIPT: When does the flight to Tokyo depart?\n3. B\nTRANSCRIPT: Who is leading the safety orientation?';
    }
    if (requirePassage) {
      return 'PASSAGE: GreenLeaf Logistics has announced an expansion of its refrigerated courier services to meet rising demand from organic food producers. The fleet will add 25 electric delivery vans by the end of Q3.\n\n1. What is the main purpose of the announcement?\n*A. To announce service expansion\nB. To advertise job openings\nC. To explain rate increases\nD. To report financial earnings\nEXPLANATION: The text states GreenLeaf announced an expansion.\n\n2. How many new vehicles will be added?\nA. 10\nB. 15\n*C. 25\nD. 50\nEXPLANATION: The fleet will add 25 vans.';
    }
    return '1. Ms. Tanaka requested that all department managers submit their quarterly budgets by _____ Friday.\n*A. next\nB. nearly\nC. soon\nD. highly\nEXPLANATION: Precedes a day of the week to indicate future timing.\nCATEGORY: grammar_prep\n\nQuestion 2: All visitors must present photo identification upon _____ the research building.\n(A) enter\n*(B) entering\n(C) entered\n(D) entrance\nANSWER: B\nEXPLANATION: Gerund after preposition.\nCATEGORY: grammar_gerund';
  }

  parseBtn.addEventListener('click', parseQuestions);

  var parseDebounceTimer = null;
  rawText.addEventListener('input', function() {
    clearTimeout(parseDebounceTimer);
    parseDebounceTimer = setTimeout(function() {
      if (rawText.value.trim().length > 10) {
        parseQuestions();
      }
    }, 800);
  });

  function getAntiForgeryToken() {
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    return tokenInput ? tokenInput.value : '';
  }

  function parseQuestions() {
    var text = rawText.value;
    if (!text.trim()) {
      summaryBadge.innerHTML = '<span style="color: var(--color-danger, #b31217);">Text is empty.</span>';
      commitBtn.disabled = true;
      previewContainer.style.display = 'none';
      return;
    }

    summaryBadge.innerHTML = '<span>Validating blueprint syntax...</span>';

    var secId = parseInt(sectionSelect.value, 10);
    var body = {
      rawText: text,
      sectionId: isNaN(secId) ? null : secId
    };

    fetch('/admin/exams/' + currentExamId + '/quick-ingest/parse', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getAntiForgeryToken()
      },
      body: JSON.stringify(body)
    })
    .then(function(res) {
      if (!res.ok) throw new Error('HTTP ' + res.status);
      return res.json();
    })
    .then(function(data) {
      currentParsedResult = data;
      renderPreview(data);
    })
    .catch(function(err) {
      summaryBadge.innerHTML = '<span style="color: var(--color-danger, #b31217);">Parsing failed: ' + err.message + '</span>';
      commitBtn.disabled = true;
    });
  }

  function renderPreview(data) {
    previewContainer.style.display = 'block';
    previewBody.innerHTML = '';

    if (data.globalIssues && data.globalIssues.length > 0) {
      data.globalIssues.forEach(function(iss) {
        var tr = document.createElement('tr');
        tr.style.background = '#ffebe6';
        tr.innerHTML = '<td colspan="6" style="padding: 0.5rem; color: #b31217; font-weight: bold;">[Global Error L' + iss.lineNumber + ']: ' + iss.message + '</td>';
        previewBody.appendChild(tr);
      });
    }

    if (data.questions && data.questions.length > 0) {
      data.questions.forEach(function(q) {
        var tr = document.createElement('tr');
        tr.style.borderBottom = '1px solid #ccd2d6';
        if (!q.isValid) {
          tr.style.background = '#fff8f7';
        }

        var typeBadge = '<span class="badge" style="background:#14181d; color:#fff; font-family:var(--font-mono, monospace); font-size:0.75rem;">' + (q.questionType || 'mcq').toUpperCase() + '</span>';

        var contentHtml = '<div style="font-weight: 500;">' + (q.stem || '<em>[Empty stem]</em>') + '</div>';
        if (q.passage) {
          var passageSnippet = q.passage.length > 80 ? q.passage.substring(0, 80) + '...' : q.passage;
          contentHtml = '<div style="font-size:0.75rem; color:#0b4ea2; margin-bottom:0.25rem;"><strong>[Passage]:</strong> ' + passageSnippet + '</div>' + contentHtml;
        }
        if (q.transcript) {
          contentHtml += '<div style="font-size:0.75rem; color:#555; margin-top:0.2rem;"><strong>[Audio]:</strong> ' + q.transcript + '</div>';
        }
        if (q.categoryCode) {
          contentHtml += ' <span class="badge" style="font-size:0.7rem; background:#eef; color:#005;">' + q.categoryCode + '</span>';
        }

        var optHtml = '';
        if (q.questionType === 'free_response') {
          optHtml = '<div style="font-size:0.8rem;">';
          if (q.requiredKeywords) {
            optHtml += '<div><strong style="color:#0a6b3c;">Keywords:</strong> ' + q.requiredKeywords + '</div>';
          }
          if (q.minWords || q.maxWords) {
            optHtml += '<div class="text-muted">Words: ' + (q.minWords || 0) + ' - ' + (q.maxWords || '&infin;') + '</div>';
          }
          optHtml += '</div>';
        } else if (q.questionType === 'inline_completion') {
          optHtml = '<div style="font-size:0.8rem;">';
          if (q.acceptedAnswersJson) {
            try {
              var answers = JSON.parse(q.acceptedAnswersJson);
              optHtml += '<div><strong style="color:#0a6b3c;">Accepted:</strong> ' + answers.join(' | ') + '</div>';
            } catch(e) {
              optHtml += '<div><strong>Accepted:</strong> ' + q.acceptedAnswersJson + '</div>';
            }
          }
          if (q.maxWords) {
            optHtml += '<div class="text-muted">Max Words: ' + q.maxWords + '</div>';
          }
          optHtml += '</div>';
        } else {
          optHtml = '<div style="display: flex; flex-direction: column; gap: 0.2rem;">';
          if (q.options) {
            q.options.forEach(function(o) {
              var badge = o.isCorrect
                ? '<span style="background: var(--color-ink, #14181d); color: #fff; padding: 0.1rem 0.35rem; font-weight: bold; margin-right: 0.35rem;">' + o.label + '</span>'
                : '<span style="border: 1px solid #ccd2d6; padding: 0.1rem 0.35rem; margin-right: 0.35rem;">' + o.label + '</span>';
              optHtml += '<div>' + badge + (o.text || '') + (o.rationale ? ' <em style="color:#666;">(' + o.rationale + ')</em>' : '') + '</div>';
            });
          }
          optHtml += '</div>';
        }

        if (q.explanation) {
          optHtml += '<div style="margin-top: 0.35rem; font-size: 0.78rem; color: #555;"><strong>Expl:</strong> ' + q.explanation + '</div>';
        }

        var statusHtml = q.isValid
          ? '<span style="background: #0a6b3c; color: #fff; padding: 0.2rem 0.5rem; font-weight: bold; font-family:var(--font-condensed, sans-serif); font-size: 0.8rem;">READY</span>'
          : '<div style="color: #b31217; font-size: 0.8rem; font-weight: 600;"><span style="background: #b31217; color: #fff; padding: 0.15rem 0.4rem; font-family:var(--font-condensed, sans-serif); font-size: 0.75rem; display:inline-block; margin-bottom:0.25rem;">MISMATCH</span>';

        if (!q.isValid && q.issues) {
          q.issues.forEach(function(iss) {
            statusHtml += '<div style="margin-top:0.15rem;">&bull; ' + iss.message + '</div>';
          });
          statusHtml += '</div>';
        }

        tr.innerHTML = '<td style="padding: 0.5rem; font-weight: 600; font-family:var(--font-mono, monospace);">' + q.index + '</td>' +
                       '<td style="padding: 0.5rem;">' + typeBadge + '</td>' +
                       '<td style="padding: 0.5rem; font-family: var(--font-mono, monospace); color: #666;">' + q.startLineNumber + '-' + q.endLineNumber + '</td>' +
                       '<td style="padding: 0.5rem;">' + contentHtml + '</td>' +
                       '<td style="padding: 0.5rem;">' + optHtml + '</td>' +
                       '<td style="padding: 0.5rem;">' + statusHtml + '</td>';

        previewBody.appendChild(tr);
      });
    }

    if (data.success) {
      summaryBadge.innerHTML = '<span style="color: var(--color-success, #0a6b3c); font-weight:bold;">&#10004; ' + data.validQuestions + ' question(s) fully conform to section blueprint. Ready to commit.</span>';
      commitBtn.disabled = false;
    } else {
      summaryBadge.innerHTML = '<span style="color: var(--color-danger, #b31217); font-weight:bold;">&#10008; ' + data.errorCount + ' validation mismatch(es). Please review highlighted rows.</span>';
      commitBtn.disabled = true;
    }
  }

  commitBtn.addEventListener('click', function() {
    if (!currentParsedResult || !currentParsedResult.success || !currentParsedResult.questions || currentParsedResult.questions.length === 0) {
      return;
    }

    var secId = parseInt(sectionSelect.value, 10);
    if (isNaN(secId)) {
      alert('Please select a target section.');
      return;
    }

    var selectedMode = document.querySelector('input[name="qi-mode"]:checked')?.value || 'append';

    if (selectedMode === 'replace') {
      var ok = confirm('WARNING: Replace mode will permanently delete all existing questions in this section for this exam. Are you sure you want to proceed?');
      if (!ok) return;
    }

    commitBtn.disabled = true;
    commitStatus.textContent = 'Committing questions to database...';

    var commitPayload = {
      sectionId: secId,
      mode: selectedMode,
      questions: currentParsedResult.questions
    };

    fetch('/admin/exams/' + currentExamId + '/quick-ingest/commit', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'RequestVerificationToken': getAntiForgeryToken()
      },
      body: JSON.stringify(commitPayload)
    })
    .then(function(res) {
      return res.json().then(function(data) {
        if (!res.ok) throw new Error(data.message || ('HTTP ' + res.status));
        return data;
      });
    })
    .then(function(data) {
      commitStatus.textContent = data.message || 'Committed successfully.';
      setTimeout(function() {
        closeModal();
        if (typeof onCommitCallback === 'function') {
          onCommitCallback(data);
        } else {
          window.location.reload();
        }
      }, 700);
    })
    .catch(function(err) {
      commitStatus.textContent = 'Commit failed: ' + err.message;
      commitBtn.disabled = false;
    });
  });

  // Expose global controller
  window.QuickIngestModal = {
    open: openModal,
    close: closeModal
  };
})();
