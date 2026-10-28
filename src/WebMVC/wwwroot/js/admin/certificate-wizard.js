function goToStep(step) {
    if (step === 3) {
      syncStep3FromSkills();
    }
    if (step === 4) {
      syncStep4FromSections();
    }
    document.querySelectorAll('.wizard-step').forEach(function(el) { el.style.display = 'none'; });
    var target = document.getElementById('step-' + step);
    if (target) {
      target.style.display = 'block';
    }
    document.getElementById('currentStep').value = step;
    var items = document.querySelectorAll('.stepper__item');
    items.forEach(function(item, idx) {
      item.classList.remove('is-current');
      if (idx + 1 === step) {
        item.classList.add('is-current');
        item.style.background = 'var(--color-ink, #14181d)';
        item.style.color = '#fff';
      } else {
        item.style.background = '#fff';
        item.style.color = 'var(--color-ink, #14181d)';
      }
    });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function selectPreset(presetCode) {
    var url = new URL(window.location.href);
    url.searchParams.set('preset', presetCode);
    url.searchParams.set('step', '1');
    window.location.href = url.toString();
  }

  function syncStep3FromSkills() {
    var skillCards = document.querySelectorAll('#skillsContainer .skill-card');
    var secContainer = document.getElementById('sectionsContainer');
    if (!secContainer) return;

    var existingGroups = secContainer.querySelectorAll('.skill-sections-group');
    var activeSkillIndices = new Set();

    skillCards.forEach(function(card) {
      var sIdx = card.getAttribute('data-skill-idx');
      activeSkillIndices.add(sIdx);
      var nameInp = card.querySelector(`input[name="Skills[${sIdx}].Name"]`);
      var codeInp = card.querySelector(`input[name="Skills[${sIdx}].Code"]`);
      var name = nameInp ? nameInp.value : `Skill ${parseInt(sIdx) + 1}`;
      var code = codeInp ? codeInp.value : `SKILL_${parseInt(sIdx) + 1}`;

      var group = secContainer.querySelector(`.skill-sections-group[data-skill-idx="${sIdx}"]`);
      if (!group) {
        var groupHtml = `
          <div class="skill-sections-group" data-skill-idx="${sIdx}" style="border: 2px solid var(--color-ink, #14181d); padding: 1rem; background: #fff;">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; padding-bottom: 0.5rem; border-bottom: 2px solid var(--color-ink, #14181d);">
              <div style="display: flex; align-items: center; gap: 0.5rem;">
                <span class="badge skill-badge-${sIdx}" style="background: #0b4ea2; color: #fff; border-radius: 0; font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.9rem;">
                  ${name} (${code})
                </span>
                <span class="text-sm text-muted count-badge-${sIdx}">0 section(s) configured</span>
              </div>
              <button type="button" class="btn btn--sm" onclick="addSection(${sIdx})" style="border-radius: 0; border: 1px solid var(--color-ink, #14181d); font-family: var(--font-condensed, sans-serif); text-transform: uppercase;">
                + Add Section
              </button>
            </div>
            <div class="sections-list" data-skill-idx="${sIdx}" style="display: flex; flex-direction: column; gap: 1rem;">
            </div>
          </div>`;
        secContainer.insertAdjacentHTML('beforeend', groupHtml);
        addSection(sIdx);
      } else {
        var badge = group.querySelector(`.skill-badge-${sIdx}`) || group.querySelector('.badge');
        if (badge) badge.textContent = `${name} (${code})`;
      }
    });

    existingGroups.forEach(function(group) {
      var sIdx = group.getAttribute('data-skill-idx');
      if (!activeSkillIndices.has(sIdx)) {
        group.remove();
      }
    });
  }

  function syncStep4FromSections() {
    var ruleSelect = document.getElementById('scoreRule');
    var rule = ruleSelect ? ruleSelect.value : 'sum';
    var step4RuleSelect = document.getElementById('step4ScoreRuleSelect');
    if (step4RuleSelect) {
      step4RuleSelect.value = rule;
      syncStep4ScoreRule(rule);
    }

    var skillCards = document.querySelectorAll('#skillsContainer .skill-card');
    var weightsContainer = document.getElementById('step4WeightsContainer');
    var calcInputs = document.getElementById('liveCalculatorInputs');

    if (!weightsContainer || !calcInputs) return;

    var weightsHtml = '';
    var calcHtml = '';

    skillCards.forEach(function(skillCard, sIdx) {
      var nameInp = skillCard.querySelector(`input[name="Skills[${sIdx}].Name"]`) || skillCard.querySelector(`input[name$=".Name"]`);
      var codeInp = skillCard.querySelector(`input[name="Skills[${sIdx}].Code"]`) || skillCard.querySelector(`input[name$=".Code"]`);
      var skillName = nameInp && nameInp.value ? nameInp.value : `Skill ${sIdx + 1}`;
      var skillCode = codeInp && codeInp.value ? codeInp.value : `SKILL_${sIdx + 1}`;

      var secGroup = document.querySelector(`.skill-sections-group[data-skill-idx="${sIdx}"]`);
      var secCards = secGroup ? secGroup.querySelectorAll('.section-card') : [];

      var sections = [];
      secCards.forEach(function(secCard, secIdx) {
        var secNameInp = secCard.querySelector(`input[name="Skills[${sIdx}].Sections[${secIdx}].Name"]`) || secCard.querySelector(`input[name$=".Name"]`);
        var secCodeInp = secCard.querySelector(`input[name="Skills[${sIdx}].Sections[${secIdx}].Code"]`) || secCard.querySelector(`input[name$=".Code"]`);
        var weightInp = secCard.querySelector(`input[name="Skills[${sIdx}].Sections[${secIdx}].ScoreWeight"]`) || secCard.querySelector(`input[name$=".ScoreWeight"]`);
        var itemsInp = secCard.querySelector(`input[name="Skills[${sIdx}].Sections[${secIdx}].ExpectedQuestionCount"]`) || secCard.querySelector(`input[name$=".ExpectedQuestionCount"]`);

        sections.push({
          secIdx: secIdx,
          name: secNameInp && secNameInp.value ? secNameInp.value : `Section ${secIdx + 1}`,
          code: secCodeInp && secCodeInp.value ? secCodeInp.value : `SEC_${secIdx + 1}`,
          weight: weightInp && weightInp.value ? weightInp.value : '1.0',
          items: itemsInp && itemsInp.value ? parseInt(itemsInp.value, 10) || 10 : 10
        });
      });

      if (sections.length === 0) {
        sections.push({
          secIdx: 0,
          name: 'Section 1',
          code: 'SEC1',
          weight: '1.0',
          items: 10
        });
      }

      weightsHtml += `
        <div class="step4-skill-weight-box" data-skill-idx="${sIdx}" style="margin-bottom: 1.25rem; border: 1px solid var(--color-border, #ccd2d6); padding: 1rem; background: #fafafa;">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem; border-bottom: 2px solid var(--color-ink, #14181d); padding-bottom: 0.5rem;">
            <strong style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 1rem;">
              Skill #${sIdx + 1}: ${skillName} (${skillCode})
            </strong>
            <span class="badge" style="background: var(--color-ink, #14181d); color: #fff; border-radius: 0; font-family: var(--font-mono, monospace); font-size: 0.75rem;">
              ${sections.length} Section${sections.length === 1 ? '' : 's'}
            </span>
          </div>
          <div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 0.75rem;">`;

      sections.forEach(function(sec) {
        weightsHtml += `
            <div style="border: 1px solid var(--color-ink, #14181d); padding: 0.75rem; background: #fff;">
              <div style="display: flex; justify-content: space-between; margin-bottom: 0.5rem;">
                <span style="font-weight: 700; font-family: var(--font-condensed, sans-serif);">${sec.name}</span>
                <code style="font-size: 0.75rem;">${sec.code}</code>
              </div>
              <div style="display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;">
                <label style="font-size: 0.75rem; font-family: var(--font-condensed, sans-serif); text-transform: uppercase;">Score Weight:</label>
                <input type="number" step="0.1" min="0.1" max="10.0"
                       class="field__input step4-weight-input"
                       data-skill-idx="${sIdx}"
                       data-sec-idx="${sec.secIdx}"
                       value="${sec.weight}"
                       onchange="syncStep4WeightToStep3(${sIdx}, ${sec.secIdx}, this.value); updateLiveScorePreview();"
                       style="width: 90px; border: 2px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace); font-weight: 700; text-align: center;">
              </div>
              <div style="font-size: 0.7rem; color: #666; margin-top: 0.25rem;">
                Expected items: <strong>${sec.items}</strong>
              </div>
            </div>`;
      });

      weightsHtml += `
          </div>
        </div>`;

      calcHtml += `
        <div class="calc-skill-row" data-calc-skill-idx="${sIdx}" style="border: 1px solid var(--color-border, #ccd2d6); padding: 0.75rem; margin-bottom: 0.75rem; background: #fafafa;">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
            <strong style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.85rem;">${skillName} (${skillCode})</strong>
            <span class="calc-skill-preview-badge" data-calc-skill-badge="${sIdx}" style="font-family: var(--font-mono, monospace); font-weight: 700; font-size: 0.85rem; color: #0b4ea2;">
              --
            </span>
          </div>`;

      if (sections.length > 1) {
        calcHtml += `<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 0.5rem;">`;
        sections.forEach(function(sec) {
          var defaultSample = Math.max(1, Math.round(sec.items * 0.75));
          calcHtml += `
            <div style="background: #fff; border: 1px solid var(--color-ink, #14181d); padding: 0.5rem;">
              <div style="display: flex; justify-content: space-between; font-size: 0.7rem; margin-bottom: 0.25rem;">
                <span>${sec.code} (w: <span class="calc-sec-weight-label" data-skill-idx="${sIdx}" data-sec-idx="${sec.secIdx}">${sec.weight}</span>)</span>
                <span class="text-muted">max: ${sec.items}</span>
              </div>
              <input type="number" min="0" max="${sec.items}"
                     class="field__input calc-sec-input"
                     data-calc-skill="${sIdx}"
                     data-calc-sec="${sec.secIdx}"
                     value="${defaultSample}"
                     oninput="updateLiveScorePreview()"
                     style="width: 100%; border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace); font-weight: 700; padding: 0.35rem; text-align: center;">
            </div>`;
        });
        calcHtml += `</div>`;
      } else {
        var firstSec = sections[0];
        var defaultSample = Math.max(1, Math.round(firstSec.items * 0.75));
        calcHtml += `
          <div style="display: flex; align-items: center; gap: 0.75rem;">
            <span style="font-size: 0.75rem; font-family: var(--font-condensed, sans-serif); text-transform: uppercase;">Sample Correct:</span>
            <input type="number" min="0" max="${firstSec.items}"
                   class="field__input calc-skill-single-input"
                   data-calc-skill="${sIdx}"
                   data-max-q="${firstSec.items}"
                   value="${defaultSample}"
                   oninput="updateLiveScorePreview()"
                   style="width: 100px; border: 2px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace); font-weight: 700; text-align: center;">
            <span class="text-xs text-muted">/ ${firstSec.items} questions</span>
          </div>`;
      }

      calcHtml += `</div>`;
    });

    weightsContainer.innerHTML = weightsHtml;
    calcInputs.innerHTML = calcHtml;

    updateLiveScorePreview();
  }

  function reindexAllFormFields() {
    var skillCards = document.querySelectorAll('#skillsContainer .skill-card');
    skillCards.forEach(function(skillCard, sIdx) {
      skillCard.setAttribute('data-skill-idx', sIdx);
      var dispInp = skillCard.querySelector(`input[name$=".DisplayOrder"]`);
      if (dispInp) dispInp.value = sIdx + 1;

      skillCard.querySelectorAll('input, select, textarea').forEach(function(inp) {
        if (inp.name) {
          inp.name = inp.name.replace(/Skills\[\d+\]/, `Skills[${sIdx}]`);
        }
      });

      var group = document.querySelector(`.skill-sections-group[data-skill-idx="${sIdx}"]`);
      if (group) {
        var secCards = group.querySelectorAll('.section-card');
        secCards.forEach(function(secCard, secIdx) {
          secCard.setAttribute('data-section-idx', secIdx);
          var secDispInp = secCard.querySelector(`input[name$=".DisplayOrder"]`);
          if (secDispInp) secDispInp.value = secIdx + 1;

          secCard.querySelectorAll('input, select, textarea').forEach(function(inp) {
            if (inp.name) {
              inp.name = inp.name.replace(/Skills\[\d+\]\.Sections\[\d+\]/, `Skills[${sIdx}].Sections[${secIdx}]`);
            }
          });
        });
      }
    });
  }


  function addSkill() {
    var container = document.getElementById('skillsContainer');
    var idx = container.querySelectorAll('.skill-card').length;
    var html = `
      <div class="skill-card" data-skill-idx="${idx}" style="border: 2px solid var(--color-ink, #14181d); padding: 1rem; background: #fafafa;">
        <input type="hidden" name="Skills[${idx}].DisplayOrder" value="${idx + 1}" />
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
          <span class="badge" style="background: var(--color-ink, #14181d); color: #fff; border-radius: 0; font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.85rem; padding: 0.25rem 0.5rem;">
            Skill #${idx + 1}
          </span>
          <button type="button" class="btn btn--sm btn--danger" onclick="removeSkill(${idx})" style="border-radius: 0; padding: 0.2rem 0.5rem;">Remove</button>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 2fr 1.5fr 1fr; gap: 0.75rem;">
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.8rem;">Skill Code</label>
            <input class="field__input" name="Skills[${idx}].Code" value="SKILL_${idx + 1}" required maxlength="20" style="border: 2px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.8rem;">Skill Name</label>
            <input class="field__input" name="Skills[${idx}].Name" value="New Skill ${idx + 1}" required maxlength="120" style="border: 2px solid var(--color-ink, #14181d); border-radius: 0;">
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.8rem;">Modality</label>
            <select class="field__input" name="Skills[${idx}].Modality" style="border: 2px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
              <option value="Listening">Listening</option>
              <option value="Reading">Reading</option>
              <option value="Writing">Writing</option>
              <option value="Speaking">Speaking</option>
            </select>
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.8rem;">Duration (Min)</label>
            <input class="field__input" type="number" name="Skills[${idx}].DurationMinutes" value="30" min="1" max="300" style="border: 2px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
        </div>
      </div>`;
    container.insertAdjacentHTML('beforeend', html);
  }

  function removeSkill(idx) {
    var card = document.querySelector(`.skill-card[data-skill-idx="${idx}"]`);
    if (card) card.remove();
  }

  function removeSection(sIdx, secIdx) {
    var card = document.querySelector(`.skill-sections-group[data-skill-idx="${sIdx}"] .section-card[data-section-idx="${secIdx}"]`);
    if (card) {
      card.remove();
      updateSectionCount(sIdx);
    }
  }

  function updateSectionCount(sIdx) {
    var list = document.querySelector(`.sections-list[data-skill-idx="${sIdx}"]`);
    var group = document.querySelector(`.skill-sections-group[data-skill-idx="${sIdx}"]`);
    if (list && group) {
      var count = list.querySelectorAll('.section-card').length;
      var badge = group.querySelector('.count-badge-' + sIdx) || group.querySelector('.text-muted');
      if (badge) badge.textContent = `${count} section(s) configured`;
    }
  }

  function addSection(sIdx) {
    var list = document.querySelector(`.sections-list[data-skill-idx="${sIdx}"]`);
    if (!list) return;
    var secIdx = list.querySelectorAll('.section-card').length;
    var html = `
      <div class="section-card" data-section-idx="${secIdx}" style="border: 1px solid var(--color-ink, #14181d); padding: 1rem; background: #fafafa;">
        <input type="hidden" name="Skills[${sIdx}].Sections[${secIdx}].DisplayOrder" value="${secIdx + 1}" />
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
          <strong>Section #${secIdx + 1}: New Section</strong>
          <button type="button" class="btn btn--sm btn--danger" onclick="removeSection(${sIdx}, ${secIdx})" style="border-radius: 0; padding: 0.15rem 0.4rem; font-size: 0.75rem;">Remove Section</button>
        </div>
        <!-- Row 1: Codes & Weights -->
        <div style="display: grid; grid-template-columns: 1fr 2fr 1fr 1fr; gap: 0.75rem; margin-bottom: 0.75rem;">
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Section Code</label>
            <input class="field__input" name="Skills[${sIdx}].Sections[${secIdx}].Code" value="SEC_${secIdx + 1}" required maxlength="20" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Section Name</label>
            <input class="field__input" name="Skills[${sIdx}].Sections[${secIdx}].Name" value="New Section ${secIdx + 1}" required maxlength="120" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0;">
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Score Weight</label>
            <input class="field__input" type="number" step="0.1" min="0.1" max="10.0" name="Skills[${sIdx}].Sections[${secIdx}].ScoreWeight" value="1.0" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Expected Items</label>
            <input class="field__input" type="number" min="1" max="200" name="Skills[${sIdx}].Sections[${secIdx}].ExpectedQuestionCount" value="10" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
        </div>
        <!-- Row 2: Navigation Mode & Screen Layout -->
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; margin-bottom: 0.75rem;">
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Navigation Behavior</label>
            <select class="field__input" name="Skills[${sIdx}].Sections[${secIdx}].NavigationMode" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
              <option value="FullSandbox" selected>FULL_SANDBOX (Free back &amp; forth navigation)</option>
              <option value="AudioDriven">AUDIO_DRIVEN (Synced to playback / One-way)</option>
              <option value="UnitLockedSandbox">UNIT_LOCKED_SANDBOX (Two-stage adaptive units)</option>
              <option value="SystemPacedAuto">SYSTEM_PACED_AUTO (Prep ➔ Beep ➔ Record)</option>
              <option value="HybridGroupLocked">HYBRID_GROUP_LOCKED (Shared pool + essay lock)</option>
            </select>
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Screen Layout</label>
            <select class="field__input" name="Skills[${sIdx}].Sections[${secIdx}].LayoutType" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
              <option value="StandardVertical" selected>STANDARD_VERTICAL (Full width single column)</option>
              <option value="SplitHorizontal">SPLIT_HORIZONTAL (Passage left, Questions right)</option>
              <option value="MediaCard">MEDIA_CARD (Image/audio card prominent)</option>
              <option value="Cluster3">CLUSTER3 (Questions grouped in 3s)</option>
            </select>
          </div>
        </div>
        <!-- Row 3: Timing Policy & Review Seconds -->
        <div style="display: grid; grid-template-columns: 2fr 1fr; gap: 0.75rem; margin-bottom: 0.75rem;">
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Section Timing Policy</label>
            <select class="field__input" name="Skills[${sIdx}].Sections[${secIdx}].TimerMode" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
              <option value="SECTION_SHARED" selected>SECTION_SHARED (Shared countdown timer for whole section)</option>
              <option value="AUDIO_PLUS_CHECK">AUDIO_PLUS_CHECK (Audio duration + post-audio review countdown)</option>
              <option value="ITEM_FIXED">ITEM_FIXED (Per-item timed prep and recording)</option>
            </select>
          </div>
          <div class="field">
            <label class="field__label" style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem;">Post-Audio Review (Sec)</label>
            <input class="field__input" type="number" min="0" max="600" name="Skills[${sIdx}].Sections[${secIdx}].ReviewSeconds" placeholder="e.g. 120" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
          </div>
        </div>
        <!-- Row 4: Slot Blueprint Parameters -->
        <div style="background: #eee; padding: 0.75rem; margin-bottom: 0.75rem; border: 1px solid var(--color-border, #ccd2d6);">
          <div style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem; font-weight: 700; margin-bottom: 0.5rem;">
            Authoring Slot Blueprint &amp; Constraints
          </div>
          <div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; margin-bottom: 0.5rem;">
            <div class="field">
              <label class="field__label" style="font-size: 0.7rem;">Default Option Count</label>
              <input class="field__input" type="number" min="2" max="6" name="Skills[${sIdx}].Sections[${secIdx}].OptionCount" value="4" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
            </div>
            <div class="field">
              <label class="field__label" style="font-size: 0.7rem;">Cluster Size</label>
              <input class="field__input" type="number" min="1" max="10" name="Skills[${sIdx}].Sections[${secIdx}].ClusterSize" value="1" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
            </div>
            <div class="field">
              <label class="field__label" style="font-size: 0.7rem;">Adaptive Unit Count</label>
              <input class="field__input" type="number" min="1" max="5" name="Skills[${sIdx}].Sections[${secIdx}].UnitCount" value="1" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
            </div>
            <div class="field">
              <label class="field__label" style="font-size: 0.7rem;">Req Keyword Count</label>
              <input class="field__input" type="number" min="0" max="10" name="Skills[${sIdx}].Sections[${secIdx}].RequiredKeywordCount" value="0" style="border: 1px solid var(--color-ink, #14181d); border-radius: 0; font-family: var(--font-mono, monospace);">
            </div>
          </div>
          <div style="display: flex; gap: 1.5rem; flex-wrap: wrap; margin-top: 0.5rem;">
            <label style="display: flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; cursor: pointer;">
              <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].HideOptionText" value="true">
              <span>Hide Option Text (A-C / A-D placards only)</span>
            </label>
            <label style="display: flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; cursor: pointer;">
              <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].RequirePassage" value="true">
              <span>Require Passage Stimulus</span>
            </label>
            <label style="display: flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; cursor: pointer;">
              <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].RequireAudio" value="true">
              <span>Require Audio File</span>
            </label>
            <label style="display: flex; align-items: center; gap: 0.35rem; font-size: 0.8rem; cursor: pointer;">
              <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].RequireImage" value="true">
              <span>Require Image Prompt</span>
            </label>
          </div>
        </div>
        <!-- Row 5: Allowed Components & Candidate Tools -->
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem;">
          <!-- Allowed Question Components -->
          <div style="background: #fff; border: 1px solid var(--color-ink, #14181d); padding: 0.6rem;">
            <div style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem; font-weight: 700; margin-bottom: 0.35rem;">
              Allowed Question Components
            </div>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.35rem; font-size: 0.75rem;">
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentMcq" value="true" checked>
                <span>Single Choice (4-opt)</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentMcq3" value="true">
                <span>Single Choice (3-opt)</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentMultiSelect" value="true">
                <span>Multi-Select Checkbox</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentInlineCompletion" value="true">
                <span>Inline Text Completion</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentDropdown" value="true">
                <span>Dropdown Matching</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentDragDrop" value="true">
                <span>Drag-and-Drop</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ComponentFreeResponse" value="true">
                <span>Free Response (AI Graded)</span>
              </label>
            </div>
          </div>
          <!-- Candidate Tools Toolbar -->
          <div style="background: #fff; border: 1px solid var(--color-ink, #14181d); padding: 0.6rem;">
            <div style="font-family: var(--font-condensed, sans-serif); text-transform: uppercase; font-size: 0.75rem; font-weight: 700; margin-bottom: 0.35rem;">
              Candidate Sitting Room Tools
            </div>
            <div style="display: flex; flex-direction: column; gap: 0.35rem; font-size: 0.75rem;">
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ToolHighlighter" value="true">
                <span>Text Highlighter &amp; Notes Tool</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ToolWordCount" value="true">
                <span>Live Word Counter</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ToolDisableSpellcheck" value="true">
                <span>Disable Browser Spellcheck</span>
              </label>
              <label style="display: flex; align-items: center; gap: 0.25rem;">
                <input type="checkbox" name="Skills[${sIdx}].Sections[${secIdx}].ToolCutPaste" value="true">
                <span>Block Keyboard Copy/Paste (Require on-screen buttons)</span>
              </label>
            </div>
          </div>
        </div>
      </div>`;
    list.insertAdjacentHTML('beforeend', html);
    updateSectionCount(sIdx);
  }

  function syncStep4ScoreRule(rule) {
    var primarySelect = document.getElementById('scoreRule');
    if (primarySelect) primarySelect.value = rule;
    var step4Select = document.getElementById('step4ScoreRuleSelect');
    if (step4Select) step4Select.value = rule;

    var help = document.getElementById('step4ScoreRuleHelp');
    if (help) {
      if (rule === 'avg_half_step') {
        help.textContent = 'Official rounding: decimal < 0.25 ➔ .0; >= 0.25 & < 0.75 ➔ .5; >= 0.75 ➔ next whole band.';
      } else if (rule === 'sum') {
        help.textContent = 'Sum rule: all skill scaled scores add up directly (e.g. Listening + Reading).';
      } else if (rule === 'avg_whole') {
        help.textContent = 'Whole rounding: average of skill scores rounded to nearest whole integer.';
      } else {
        help.textContent = 'Independent rule: each skill is evaluated and displayed separately.';
      }
    }
    updateLiveScorePreview();
  }

  function syncStep4WeightToStep3(sIdx, secIdx, val) {
    var inputInStep3 = document.querySelector(`input[name="Skills[${sIdx}].Sections[${secIdx}].ScoreWeight"]`);
    if (inputInStep3) inputInStep3.value = val;

    // update weight label in calculator
    var label = document.querySelector(`.calc-sec-weight-label[data-skill-idx="${sIdx}"][data-sec-idx="${secIdx}"]`);
    if (label) label.textContent = val;
  }

  function roundHalfStepIelts(val) {
    if (val > 10.0) {
      var band = val / 10.0;
      var floor = Math.floor(band);
      var rem = band - floor;
      var rounded = rem < 0.25 ? floor : (rem < 0.75 ? floor + 0.5 : floor + 1.0);
      return Math.round(rounded * 10);
    } else {
      var floor = Math.floor(val);
      var rem = val - floor;
      var rounded = rem < 0.25 ? floor : (rem < 0.75 ? floor + 0.5 : floor + 1.0);
      return Math.round(rounded * 10);
    }
  }

  function updateLiveScorePreview() {
    var ruleSelect = document.getElementById('step4ScoreRuleSelect') || document.getElementById('scoreRule');
    var rule = ruleSelect ? ruleSelect.value : 'sum';

    var skillRows = document.querySelectorAll('.calc-skill-row');
    var skillScaledScores = [];
    var summaryHtml = '';

    skillRows.forEach(function(row) {
      var sIdx = row.getAttribute('data-calc-skill-idx');
      var skillNameEl = row.querySelector('strong');
      var skillName = skillNameEl ? skillNameEl.textContent : 'Skill';

      var secInputs = row.querySelectorAll('.calc-sec-input');
      var scaled = 0;
      var rawDisplay = 0;

      if (secInputs.length > 0) {
        var weightedSum = 0;
        var totalWeight = 0;
        secInputs.forEach(function(inp) {
          var secIdx = inp.getAttribute('data-calc-sec');
          var weightInput = document.querySelector(`.step4-weight-input[data-skill-idx="${sIdx}"][data-sec-idx="${secIdx}"]`);
          var weight = weightInput ? parseFloat(weightInput.value) || 1.0 : 1.0;
          var val = parseFloat(inp.value) || 0;
          weightedSum += val * weight;
          totalWeight += weight;
        });

        var weightedRaw = totalWeight > 0 ? (weightedSum / totalWeight) : 0;
        rawDisplay = weightedRaw;

        if (rule === 'avg_half_step') {
          scaled = roundHalfStepIelts(weightedRaw);
        } else if (rule === 'avg_whole') {
          scaled = Math.round(weightedRaw);
        } else {
          // Default proportional
          scaled = Math.round(weightedRaw * 10);
        }
      } else {
        var singleInput = row.querySelector('.calc-skill-single-input');
        var val = singleInput ? parseFloat(singleInput.value) || 0 : 0;
        var maxQ = singleInput ? parseFloat(singleInput.getAttribute('data-max-q')) || 40 : 40;
        rawDisplay = val;

        if (rule === 'avg_half_step') {
          // Proportional to 9.0 (stored as 10-90)
          var bandVal = (val / maxQ) * 9.0;
          scaled = roundHalfStepIelts(bandVal);
        } else if (rule === 'avg_whole') {
          scaled = Math.round((val / maxQ) * 100);
        } else {
          // TOEIC sum: ~495 per skill
          scaled = Math.round((val / maxQ) * 495 / 5) * 5;
        }
      }

      skillScaledScores.push(scaled);

      var badge = row.querySelector('.calc-skill-preview-badge');
      if (badge) {
        if (rule === 'avg_half_step') {
          badge.textContent = 'Scaled: ' + (scaled / 10).toFixed(1);
        } else {
          badge.textContent = 'Scaled: ' + scaled;
        }
      }

      summaryHtml += '<div style="display: flex; justify-content: space-between; padding: 0.2rem 0; border-bottom: 1px dotted #ccc;">'
        + '<span>' + skillName + '</span>'
        + '<strong>' + (rule === 'avg_half_step' ? (scaled / 10).toFixed(1) : scaled) + '</strong>'
        + '</div>';
    });

    var summaryEl = document.getElementById('liveSkillsSummaryTable');
    if (summaryEl) summaryEl.innerHTML = summaryHtml;

    // Aggregate total
    var total = 0;
    if (skillScaledScores.length > 0) {
      if (rule === 'sum') {
        total = skillScaledScores.reduce(function(a, b) { return a + b; }, 0);
      } else if (rule === 'avg_half_step') {
        var avg = skillScaledScores.reduce(function(a, b) { return a + b; }, 0) / skillScaledScores.length;
        total = roundHalfStepIelts(avg);
      } else if (rule === 'avg_whole') {
        var avg = skillScaledScores.reduce(function(a, b) { return a + b; }, 0) / skillScaledScores.length;
        total = Math.round(avg);
      } else {
        total = 0;
      }
    }

    var totalEl = document.getElementById('liveTotalScoreDisplay');
    if (totalEl) {
      if (rule === 'avg_half_step') {
        totalEl.textContent = (total / 10).toFixed(1);
      } else if (rule === 'independent') {
        totalEl.textContent = 'SEPARATE';
      } else {
        totalEl.textContent = total;
      }
    }

    // Band matching
    var bandEl = document.getElementById('liveBandBadgeDisplay');
    if (bandEl) {
      if (rule === 'avg_half_step') {
        var bandNum = (total / 10).toFixed(1);
        if (total >= 85) bandEl.textContent = 'C2 • BAND ' + bandNum;
        else if (total >= 70) bandEl.textContent = 'C1 • BAND ' + bandNum;
        else if (total >= 55) bandEl.textContent = 'B2 • BAND ' + bandNum;
        else if (total >= 40) bandEl.textContent = 'B1 • BAND ' + bandNum;
        else bandEl.textContent = 'A2 • BAND ' + bandNum;
      } else if (rule === 'sum') {
        if (total >= 905) bandEl.textContent = 'GOLD • 905–990';
        else if (total >= 785) bandEl.textContent = 'BLUE • 785–900';
        else if (total >= 605) bandEl.textContent = 'GREEN • 605–780';
        else if (total >= 405) bandEl.textContent = 'BROWN • 405–600';
        else bandEl.textContent = 'ORANGE • 10–400';
      } else if (rule === 'avg_whole') {
        if (total >= 85) bandEl.textContent = 'C1 • ADVANCED';
        else if (total >= 60) bandEl.textContent = 'B2 • VANTAGE';
        else if (total >= 40) bandEl.textContent = 'B1 • THRESHOLD';
        else bandEl.textContent = 'A2 • ELEMENTARY';
      } else {
        bandEl.textContent = 'INDEPENDENT SKILLS';
      }
    }
    var csrfToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    if (csrfToken && skillScaledScores.length > 0) {
      clearTimeout(window.__scorePreviewTimeout);
      window.__scorePreviewTimeout = setTimeout(function() {
        var certIdVal = parseInt(document.getElementById('certId')?.value, 10) || null;
        var codeVal = document.querySelector('input[name="Code"]')?.value || null;
        var skillsPayload = [];
        skillRows.forEach(function(row) {
          var sIdx = row.getAttribute('data-calc-skill-idx');
          var skillCode = document.querySelector('input[name="Skills[' + sIdx + '].Code"]')?.value || ('SKILL_' + sIdx);
          var singleInput = row.querySelector('.calc-skill-single-input');
          var rawVal = singleInput ? parseFloat(singleInput.value) || 0 : 0;
          skillsPayload.push({ skillId: 0, skillCode: skillCode, rawScore: rawVal });
        });
        fetch('/admin/certificates/score-preview', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': csrfToken
          },
          body: JSON.stringify({
            certId: certIdVal,
            certCode: codeVal,
            scoreAggregationRule: rule,
            skills: skillsPayload
          })
        }).catch(function() {});
      }, 300);
    }
  }

  // Form submission: ensure sequential field indexing
  document.getElementById('wizardForm')?.addEventListener('submit', function() {
    reindexAllFormFields();
  });

  // Initialize live preview on load
  document.addEventListener('DOMContentLoaded', function() {
    var curStep = parseInt(document.getElementById('currentStep')?.value || '1', 10);
    if (curStep === 4) {
      syncStep4FromSections();
    } else {
      updateLiveScorePreview();
    }
  });
