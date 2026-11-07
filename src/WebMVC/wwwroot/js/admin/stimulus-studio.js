/**
 * #203 / S-05 / ADM-07: the stimulus-and-children studio.
 *
 * The page holds no threshold. Every quota, allowed level, gauge, overlap and gap comes back from the
 * API's own seam through /admin/studio/stimulus/analyse, so the number the author watches and the rule
 * that refuses the save are one arithmetic. This script's whole job is: pre-create the slots a level
 * locks, read the form into a bundle, post it, and paint the answer.
 *
 * ADR-0002: every call goes to this MVC endpoint, never to the API, and carries the anti-forgery token.
 */
(function () {
  "use strict";

  var SLOT_COLOURS = 4; /* slots cycle 1-4 by CNT-21's maximum */

  var configHost = document.querySelector(".studio-config");
  if (!configHost) return;

  var config = JSON.parse(configHost.getAttribute("data-configuration") || "{}");
  var skill = configHost.getAttribute("data-skill") || "reading";
  var listening = skill === "listening";

  var levelButtons = Array.prototype.slice.call(document.querySelectorAll(".studio-level"));
  var typeSelect = document.getElementById("studio-type");
  var domainSelect = document.getElementById("studio-domain");
  var passage = document.getElementById("studio-passage");
  var audio = document.getElementById("studio-audio");
  var slotsHost = document.getElementById("studio-slots");
  var slotTemplate = document.getElementById("studio-slot-template");
  var slotCount = document.getElementById("studio-slot-count");
  var gaugeList = document.getElementById("studio-gauge-list");
  var verdict = document.getElementById("studio-verdict");
  var overlapBox = document.getElementById("studio-overlap-indicator");
  var overlapValue = document.getElementById("studio-overlap-value");
  var overlapNote = document.getElementById("studio-overlap-note");
  var gapBox = document.getElementById("studio-gap-indicator");
  var gapValue = document.getElementById("studio-gap-value");
  var gapNote = document.getElementById("studio-gap-note");
  var saveButton = document.getElementById("studio-save");
  var saveStatus = document.getElementById("studio-save-status");
  var refusalsHost = document.getElementById("studio-refusals");
  var preview = document.getElementById("studio-preview");

  var slots = [];
  var selectedLevel = "";
  var analysisSequence = 0;

  function csrfToken() {
    var input = document.querySelector(
      '#studio-csrf-holder input[name="__RequestVerificationToken"]'
    );
    return input ? input.value : "";
  }

  function post(url, body) {
    return fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": csrfToken(),
        "X-Requested-With": "XMLHttpRequest"
      },
      body: JSON.stringify(body),
      credentials: "same-origin"
    });
  }

  /* ---- the quota a level and type lock ------------------------------------ */

  function quotaFor(level, itemType) {
    var quotas = config.quotas || [];
    for (var i = 0; i < quotas.length; i++) {
      if (quotas[i].level === level && quotas[i].itemType === itemType) return quotas[i];
    }
    return null;
  }

  /* ---- reading the form into the request the server judges ---------------- */

  function selectedType() { return typeSelect ? typeSelect.value : ""; }

  function childFrom(slot) {
    var key = slot.element.querySelector("[data-slot-key]:checked");
    return {
      position: slot.position,
      cefrLevel: slot.levelSelect.value,
      topicCode: slot.topicSelect.value,
      stem: slot.element.querySelector("[data-slot-stem]").value,
      optionA: slot.element.querySelectorAll("[data-slot-option]")[0].value,
      optionB: slot.element.querySelectorAll("[data-slot-option]")[1].value,
      optionC: slot.element.querySelectorAll("[data-slot-option]")[2].value,
      optionD: slot.element.querySelectorAll("[data-slot-option]")[3].value,
      keyLabel: key ? key.value : null,
      explanation: slot.element.querySelector("[data-slot-explanation]").value,
      rationaleA: slot.element.querySelectorAll("[data-slot-rationale]")[0].value,
      rationaleB: slot.element.querySelectorAll("[data-slot-rationale]")[1].value,
      rationaleC: slot.element.querySelectorAll("[data-slot-rationale]")[2].value,
      rationaleD: slot.element.querySelectorAll("[data-slot-rationale]")[3].value,
      evidenceStart: numberOrNull(slot.element.querySelector("[data-slot-span-start]").value),
      evidenceEnd: numberOrNull(slot.element.querySelector("[data-slot-span-end]").value),
      evidenceQuote: null,
      placementEligible: false
    };
  }

  function numberOrNull(value) {
    if (value === null || value === undefined || value === "") return null;
    var parsed = parseInt(value, 10);
    return isNaN(parsed) ? null : parsed;
  }

  function bundle() {
    var children = slots.map(childFrom);
    var request = {
      modality: skill.charAt(0).toUpperCase() + skill.slice(1),
      stimulusLevel: selectedLevel,
      itemType: selectedType(),
      children: children,
      domainContext: domainSelect ? domainSelect.value : "general"
    };

    if (listening) {
      request.transcript = passage.value;
      request.audioPath = audio ? audio.value : null;
    } else {
      request.passage = passage.value;
    }

    return request;
  }

  /* ---- pre-creating the slots a level locks ------------------------------- */

  function buildSlots(count, quota) {
    slotsHost.innerHTML = "";
    slots = [];

    if (count <= 0) {
      var placeholder = document.createElement("p");
      placeholder.className = "studio-empty";
      placeholder.id = "studio-slots-empty";
      placeholder.textContent = "Select a stimulus level. The count of children is fixed by the level, " +
        "and that many slots appear here.";
      slotsHost.appendChild(placeholder);
      slotCount.textContent = "0 slots";
      slotCount.setAttribute("data-locked", "0");
      return;
    }

    var allowed = quota && quota.allowedChildLevels ? quota.allowedChildLevels : [];
    var minimumAtLevel = quota ? quota.minimumAtLevel : 0;

    for (var index = 0; index < count; index++) {
      var fragment = slotTemplate.content.cloneNode(true);
      var element = fragment.querySelector(".studio-slot");
      var colour = (index % SLOT_COLOURS) + 1;
      element.setAttribute("data-colour", String(colour));
      element.querySelector("[data-slot-number]").textContent = String(index + 1);

      var levelSelect = element.querySelector("[data-slot-level]");
      allowed.forEach(function (level) {
        var option = document.createElement("option");
        option.value = level;
        option.textContent = level;
        /* The default is the stimulus's own level, which is what the two-thirds rule wants most of. */
        if (level === selectedLevel) option.selected = true;
        levelSelect.appendChild(option);
      });

      var unitLabel = element.querySelector("[data-slot-span-unit]");
      if (unitLabel) unitLabel.textContent = listening ? "sec" : "char";

      var bind = element.querySelector("[data-slot-bind]");
      if (bind) actionBind(bind, index);

      slotsHost.appendChild(fragment);

      /* The element is looked up after the append, so it is the one now in the document rather than the
         clone's - an edit to a detached clone would paint nothing. */
      slots.push({
        position: index + 1,
        element: slotsHost.querySelector('.studio-slot[data-colour="' + colour + '"]:last-of-type'),
        levelSelect: levelSelect,
        topicSelect: element.querySelector("[data-slot-topic]")
      });
    }

    slotCount.textContent = count + (count === 1 ? " slot" : " slots");
    slotCount.setAttribute("data-locked", String(count));
    if (minimumAtLevel > 0) {
      slotCount.title = "At least " + minimumAtLevel + " of " + count + " must sit at " + selectedLevel +
        " (D0.3's two-thirds rule).";
    }
  }

  /* ---- binding a child to the text the author selected -------------------- */

  function actionBind(button, index) {
    button.addEventListener("click", function () {
      var start = passage.selectionStart;
      var end = passage.selectionEnd;
      if (start === null || end === null || end <= start) {
        saveStatus.textContent = "Select some of the passage first; a span is bound by pointing, not by typing.";
        saveStatus.setAttribute("data-state", "blocked");
        return;
      }

      var slot = slots[index];
      if (!slot) return;

      /* Character offsets for reading. For listening the author picks sentences and the seconds are
         typed, because a character offset in a transcript is not a timestamp in the recording. */
      slot.element.querySelector("[data-slot-span-start]").value = String(start);
      slot.element.querySelector("[data-slot-span-end]").value = String(end);

      paintPassage();
      analyse();
    });
  }

  /**
   * Paints every bound span in its child's colour. The marks are rebuilt from scratch on each change so
   * a moved span cannot leave a stale mark behind - a highlight that lies about where the evidence is
   * is the defect this screen exists to prevent.
   */
  function paintPassage() {
    if (!preview) return;

    var text = passage.value;
    var bounds = [];
    slots.forEach(function (slot) {
      var start = numberOrNull(slot.element.querySelector("[data-slot-span-start]").value);
      var end = numberOrNull(slot.element.querySelector("[data-slot-span-end]").value);
      if (start === null || end === null || end <= start || end > text.length) return;
      bounds.push({ start: start, end: end, colour: slot.element.getAttribute("data-colour") });
    });

    if (bounds.length === 0 || listening) {
      preview.innerHTML = "";
      var none = document.createElement("p");
      none.className = "studio-preview__empty";
      none.textContent = listening
        ? "A listening child is bound by seconds; the timestamps above are its span."
        : "No child is bound to a span yet.";
      preview.appendChild(none);
      return;
    }

    /* Highest start first: an inserted mark shifts every later offset, so the tail is painted before the
       head and each bound span lands on the characters the author selected. */
    bounds.sort(function (a, b) { return b.start - a.start; });

    var html = escapeHtml(text);
    bounds.forEach(function (bound) {
      var head = html.slice(0, bound.start);
      var body = html.slice(bound.start, bound.end);
      var tail = html.slice(bound.end);
      html = head + '<mark class="studio-mark" data-colour="' + bound.colour + '">' + body + "</mark>" + tail;
    });

    preview.innerHTML = html;
  }

  function escapeHtml(value) {
    return value
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");
  }

  /* ---- painting the analysis --------------------------------------------- */

  function paintGauges(gauges) {
    gaugeList.innerHTML = "";
    if (!gauges || gauges.length === 0) {
      var empty = document.createElement("li");
      empty.className = "studio-gauge studio-gauge--unbounded";
      empty.innerHTML = '<span class="studio-gauge__label">Not profiled</span>' +
        '<span class="studio-gauge__value font-mono">—</span>' +
        '<span class="studio-gauge__target font-mono"></span>';
      gaugeList.appendChild(empty);
      return;
    }

    gauges.forEach(function (gauge) {
      var item = document.createElement("li");
      var state = gauge.inside === null ? "unbounded" : (gauge.inside ? "inside" : "outside");
      item.className = "studio-gauge studio-gauge--" + state;

      var label = document.createElement("span");
      label.className = "studio-gauge__label";
      label.textContent = gauge.label;

      var value = document.createElement("span");
      value.className = "studio-gauge__value font-mono";
      value.textContent = gauge.value;

      var target = document.createElement("span");
      target.className = "studio-gauge__target font-mono";
      target.textContent = gauge.target;

      item.appendChild(label);
      item.appendChild(value);
      item.appendChild(target);
      gaugeList.appendChild(item);
    });
  }

  function paintOverlap(overlaps, maximum) {
    if (!overlaps || overlaps.length === 0) {
      overlapBox.setAttribute("data-state", "clear");
      overlapValue.textContent = "0.00";
      overlapNote.textContent = "No two children cite overlapping text.";
      return;
    }

    var over = false;
    overlaps.forEach(function (pair) {
      if (pair.overCeiling) over = true;
    });

    overlapValue.textContent = Number(maximum).toFixed(2);
    overlapBox.setAttribute("data-state", over ? "over" : "clear");
    overlapNote.textContent = over
      ? "Over 0.15: two children measure the same sentence, which counts one proficiency twice."
      : "Below the 0.15 ceiling.";
  }

  function paintGaps(gaps) {
    if (!gapBox || listening === false) return;

    if (!gaps || gaps.length === 0) {
      gapBox.setAttribute("data-state", "clear");
      gapValue.textContent = "—";
      gapNote.textContent = "Adjacent detail items need 8.0 s.";
      return;
    }

    var shortest = gaps[0];
    gaps.forEach(function (gap) { if (gap.seconds < shortest.seconds) shortest = gap; });

    gapValue.textContent = Number(shortest.seconds).toFixed(1) + " s";
    gapBox.setAttribute("data-state", shortest.belowFloor ? "over" : "clear");
    gapNote.textContent = shortest.belowFloor
      ? "Under 8.0 s between child " + shortest.afterPosition + " and " + shortest.beforePosition + "."
      : "Every adjacent pair leaves at least 8.0 s.";
  }

  function paintRefusals(analysis) {
    refusalsHost.innerHTML = "";

    (analysis.refusals || []).forEach(function (refusal) {
      var block = document.createElement("div");
      block.className = "studio-refusal";
      block.innerHTML = '<span class="studio-refusal__gate"></span>' +
        '<span class="studio-refusal__message"></span>';
      block.querySelector(".studio-refusal__gate").textContent = "Refused by " + refusal.gate;
      block.querySelector(".studio-refusal__message").textContent = refusal.message;
      refusalsHost.appendChild(block);
    });

    (analysis.unrunChecks || []).forEach(function (check) {
      var block = document.createElement("div");
      block.className = "studio-notice";
      block.innerHTML = '<span class="studio-notice__gate"></span>' +
        '<span class="studio-notice__message"></span>';
      block.querySelector(".studio-notice__gate").textContent = check.gate + " — not run";
      block.querySelector(".studio-notice__message").textContent = check.message;
      refusalsHost.appendChild(block);
    });

    var acceptable = analysis.acceptable;
    saveButton.disabled = !acceptable || !selectedLevel;
    saveStatus.setAttribute("data-state", acceptable ? "ready" : "blocked");
    saveStatus.textContent = !selectedLevel
      ? "Choose a stimulus level first."
      : acceptable
        ? "Ready to save as a draft."
        : "Cannot be saved while " + analysis.refusals.length + " rule(s) fail.";

    /* A slot whose span is in an overlapping pair is outlined, so the stack says which child to move. */
    var conflicted = {};
    (analysis.overlaps || []).forEach(function (pair) {
      if (pair.overCeiling) { conflicted[pair.firstPosition] = true; conflicted[pair.secondPosition] = true; }
    });

    slots.forEach(function (slot) {
      slot.element.setAttribute("data-conflict", conflicted[slot.position] ? "true" : "false");
      var local = (analysis.refusals || []).filter(function (refusal) {
        return refusal.message.indexOf("child " + slot.position + ":") === 0;
      });
      var host = slot.element.querySelector("[data-slot-refusals]");
      host.textContent = local.map(function (refusal) { return refusal.message; }).join(" ");
    });
  }

  /* ---- the live call ------------------------------------------------------ */

  function analyse() {
    if (!selectedLevel) return Promise.resolve();

    var sequence = ++analysisSequence;
    return post("/admin/studio/stimulus/analyse", bundle())
      .then(function (response) {
        if (!response.ok) return null;
        return response.json();
      })
      .then(function (analysis) {
        /* A slower earlier response must not overwrite a newer one: the screen would show a verdict
           about text that is no longer in the box. */
        if (!analysis || sequence !== analysisSequence) return;

        paintGauges(analysis.gauges);
        verdict.textContent = analysis.verdict;
        /* D19 and ADM-08: the verdict states a fact. When the band the text fits is not the band
           selected, the control says so in gold; when they agree, plain. The server's sentence carries
           the two codes, so the state is whether it names a different band - never a client-side
           comparison of thresholds. */
        verdict.setAttribute(
          "data-state",
          analysis.selectedBand && analysis.fittingBand && analysis.selectedBand !== analysis.fittingBand
            ? "mismatch"
            : "");
        paintGaps(analysis.gaps);
        paintRefusals(analysis);
        paintPassage();
      })
      .catch(function () {
        saveStatus.setAttribute("data-state", "blocked");
        saveStatus.textContent = "Could not reach the studio; the last verdict is stale.";
      });
  }

  /* ---- wiring ------------------------------------------------------------- */

  levelButtons.forEach(function (button) {
    button.addEventListener("click", function () {
      levelButtons.forEach(function (peer) {
        peer.classList.remove("is-selected");
        peer.setAttribute("aria-checked", "false");
      });
      button.classList.add("is-selected");
      button.setAttribute("aria-checked", "true");
      selectedLevel = button.getAttribute("data-level");
      document.getElementById("studio-level").value = selectedLevel;

      var quota = quotaFor(selectedLevel, selectedType());
      buildSlots(quota ? quota.locked : 0, quota);
      analyse();
    });
  });

  if (typeSelect) {
    typeSelect.addEventListener("change", function () {
      if (!selectedLevel) return;
      var quota = quotaFor(selectedLevel, selectedType());
      buildSlots(quota ? quota.locked : 0, quota);
      analyse();
    });
  }

  passage.addEventListener("input", debounce(analyse, 400));

  slotsHost.addEventListener("input", debounce(analyse, 400));
  slotsHost.addEventListener("change", debounce(analyse, 200));

  saveButton.addEventListener("click", function () {
    saveButton.disabled = true;
    saveStatus.setAttribute("data-state", "");
    saveStatus.textContent = "Saving…";

    post("/admin/studio/stimulus", bundle())
      .then(function (response) {
        return response.json().then(function (body) { return { ok: response.ok, body: body }; });
      })
      .then(function (result) {
        if (!result.ok) {
          saveStatus.setAttribute("data-state", "blocked");
          saveStatus.textContent = result.body.message || "The bundle was refused.";
          saveButton.disabled = false;
          return;
        }

        saveStatus.setAttribute("data-state", "ready");
        saveStatus.textContent = "Saved stimulus " + result.body.stimulusId + " with " +
          result.body.children.length + " children, all as drafts.";
      })
      .catch(function () {
        saveStatus.setAttribute("data-state", "blocked");
        saveStatus.textContent = "Could not save; nothing was written.";
        saveButton.disabled = false;
      });
  });

  function debounce(fn, wait) {
    var handle = null;
    return function () {
      if (handle) clearTimeout(handle);
      handle = setTimeout(fn, wait);
    };
  }
})();
