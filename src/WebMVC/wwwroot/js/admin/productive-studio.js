/**
 * #204 / S-07: the productive-task studio.
 *
 * The page holds no threshold. Every window, word bound and placement cap comes back from the API's
 * own seam through /admin/studio/productive/analyse, so the figure the author watches and the rule that
 * refuses the save are one arithmetic. This script's whole job is: pre-create the parts a task may
 * carry, read the form into a draft, post it, and paint the answer.
 *
 * ADR-0002: every call goes to this MVC endpoint, never to the API, and carries the anti-forgery token.
 */
(function () {
  "use strict";

  var configHost = document.getElementById("productive-config");
  if (!configHost) return;

  var config = JSON.parse(configHost.getAttribute("data-configuration") || "{}");
  var skill = configHost.getAttribute("data-skill") || "writing";
  var speaking = skill === "speaking";

  var MAX_PARTS = 2;
  var MAX_PARTS_SPEAKING = 1;

  var levelButtons = Array.prototype.slice.call(document.querySelectorAll(".studio-level"));
  var topicSelect = document.getElementById("studio-topic");
  var domainSelect = document.getElementById("studio-domain");
  var prompt = document.getElementById("studio-prompt");
  var imageField = document.getElementById("studio-image-field");
  var imageNote = document.getElementById("studio-image-note");
  var imageInput = document.getElementById("studio-image");
  var imageAltInput = document.getElementById("studio-image-alt");
  var placementToggle = document.getElementById("studio-placement");
  var capBox = document.getElementById("studio-placement-cap");
  var capValue = document.getElementById("studio-placement-cap-value");
  var capNote = document.getElementById("studio-placement-cap-note");
  var rangeList = document.getElementById("studio-word-range-list");
  var partsHost = document.getElementById("studio-parts");
  var partTemplate = document.getElementById("studio-part-template");
  var partCount = document.getElementById("studio-part-count");
  var addPartButton = document.getElementById("studio-add-part");
  var addPartNote = document.getElementById("studio-add-part-note");
  var saveButton = document.getElementById("studio-save");
  var saveStatus = document.getElementById("studio-save-status");
  var refusalsHost = document.getElementById("studio-refusals");

  var parts = [];
  var selectedLevel = "";
  var analysisSequence = 0;
  var lastAnalysis = null;

  function csrfToken() {
    var input = document.querySelector(
      '#productive-csrf-holder input[name="__RequestVerificationToken"]'
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

  function numberOrNull(value) {
    if (value === null || value === undefined || value === "") return null;
    var parsed = parseInt(value, 10);
    return isNaN(parsed) ? null : parsed;
  }

  /* ---- the bounds one type carries ---------------------------------------- */

  function typeBounds(code) {
    var types = config.types || [];
    for (var i = 0; i < types.length; i++) {
      if (types[i].code === code) return types[i];
    }
    return null;
  }

  function maxParts() { return speaking ? MAX_PARTS_SPEAKING : MAX_PARTS; }

  /* ---- reading the form into the request the server judges ---------------- */

  function partFrom(part) {
    var bounds = typeBounds(part.typeSelect.value) || {};
    var wordBound = bounds.minimumWords === null || bounds.minimumWords === undefined ? null : bounds;
    return {
      itemType: part.typeSelect.value,
      prompt: part.element.querySelector("[data-part-prompt]").value,
      /* A speaking part declares no word range: its length is its recording window. */
      minWords: wordBound ? numberOrNull(part.element.querySelector("[data-part-min]").value) : null,
      maxWords: wordBound ? numberOrNull(part.element.querySelector("[data-part-max]").value) : null,
      prepSeconds: numberOrNull(part.element.querySelector("[data-part-prep]").value),
      responseSeconds: numberOrNull(part.element.querySelector("[data-part-response]").value),
      explanation: part.element.querySelector("[data-part-explanation]").value
    };
  }

  function requestBody() {
    return {
      modality: skill.charAt(0).toUpperCase() + skill.slice(1),
      level: selectedLevel,
      topicCode: topicSelect ? topicSelect.value : "",
      promptCopy: prompt ? prompt.value : null,
      imagePath: imageInput && imageInput.value ? imageInput.value : null,
      imageAlt: imageAltInput && imageAltInput.value ? imageAltInput.value : null,
      domainContext: domainSelect ? domainSelect.value : "general",
      placementEligible: placementToggle ? placementToggle.checked : false,
      subTasks: parts.map(partFrom)
    };
  }

  /* ---- pre-creating and stacking the parts -------------------------------- */

  function buildParts(count) {
    partsHost.innerHTML = "";
    parts = [];

    var previousTypes = parts.map(function (part) { return part.typeSelect.value; });

    for (var index = 0; index < count; index++) {
      var fragment = partTemplate.content.cloneNode(true);
      var element = fragment.querySelector("[data-part]");
      element.querySelector("[data-part-number]").textContent = String(index + 1);

      var typeSelect = element.querySelector("[data-part-type]");
      if (previousTypes[index]) typeSelect.value = previousTypes[index];

      var remove = element.querySelector("[data-part-remove]");
      if (remove) remove.addEventListener("click", actionRemove(index));

      partsHost.appendChild(fragment);

      /* The element is looked up after the append, so it is the one now in the document rather than
         the clone's - an edit to a detached clone would paint nothing. */
      var live = partsHost.querySelectorAll("[data-part]")[index];
      parts.push({ position: index + 1, element: live, typeSelect: typeSelect });
      paintPartHints(parts[index]);
    }

    paintPartCount();
    paintAddPartControl();
  }

  function paintPartCount() {
    partCount.textContent = parts.length + (parts.length === 1 ? " part" : " parts");
    partCount.setAttribute("data-count", String(parts.length));
  }

  function paintAddPartControl() {
    var allowed = maxParts();
    var canAdd = parts.length < allowed;
    addPartButton.hidden = !canAdd;
    addPartButton.disabled = !canAdd;

    addPartNote.textContent = speaking
      ? "A speaking task is one prompt and one recording. One part, always."
      : canAdd
        ? "A writing task may be one part, or the two a two-task sitting is weighted for — Task 2 counts twice Task 1, so each part carries its own range and budget."
        : "Two parts is the most a two-task sitting is weighted for.";
  }

  function actionRemove(index) {
    return function () {
      if (parts.length <= 1) return;
      var types = parts.map(function (part) { return part.typeSelect.value; });
      types.splice(index, 1);
      buildParts(types.length);
      for (var i = 0; i < parts.length; i++) {
        parts[i].typeSelect.value = types[i];
        paintPartHints(parts[i]);
      }
      analyse();
    };
  }

  /**
   * Paints the bounds D0.4 gives the selected type: the word range hint, the two window hints, and
   * whether the field applies at all. A writing part's preparation is planning time the spec does not
   * bound, so the field is disabled and says so rather than sitting empty and looking unanswered.
   */
  function paintPartHints(part) {
    var bounds = typeBounds(part.typeSelect.value);
    if (!bounds) return;

    var min = part.element.querySelector("[data-part-min]");
    var max = part.element.querySelector("[data-part-max]");
    var wordsUnit = part.element.querySelector("[data-part-words-unit]");
    var rangeHint = part.element.querySelector("[data-part-range-hint]");
    var prep = part.element.querySelector("[data-part-prep]");
    var prepHint = part.element.querySelector("[data-part-prep-hint]");
    var response = part.element.querySelector("[data-part-response]");
    var responseHint = part.element.querySelector("[data-part-response-hint]");
    var responseLabel = part.element.querySelector("[data-part-response-label]");

    var hasWords = bounds.minimumWords !== null && bounds.minimumWords !== undefined;
    min.disabled = !hasWords;
    max.disabled = !hasWords;
    wordsUnit.textContent = hasWords ? "words" : "—";

    var scoped = null;
    if (lastAnalysis && lastAnalysis.practiceWordRanges) {
      scoped = lastAnalysis.practiceWordRanges[part.position - 1] || null;
    }

    rangeHint.textContent = hasWords
      ? (scoped
        ? "This band allows " + scoped.floor + "–" + scoped.ceiling + " words for this type."
        : "D0.4 gives this type " + bounds.minimumWords + "–" + bounds.maximumWords + " words by band.")
      : "A spoken answer is bounded by its recording window, not by a word count.";

    var hasPrep = bounds.minimumPrepSeconds !== null && bounds.minimumPrepSeconds !== undefined;
    prep.disabled = !hasPrep;
    prepHint.textContent = hasPrep
      ? "D0.4 allows " + bounds.minimumPrepSeconds + "–" + bounds.maximumPrepSeconds + " s."
      : "No preparation window in D0.4; leave it empty.";

    responseLabel.textContent = hasWords ? "Writing budget" : "Recording";
    responseHint.textContent = "D0.4 allows " + bounds.minimumResponseSeconds
      + "–" + bounds.maximumResponseSeconds + " s.";
  }

  /* ---- the visual stimulus: which types need one -------------------------- */

  function paintImageField() {
    if (!imageField) return;

    var types = parts.map(function (part) { return typeBounds(part.typeSelect.value); });
    var required = types.some(function (bounds) { return bounds && bounds.visual === "required"; });
    var optional = types.some(function (bounds) {
      return bounds && (bounds.visual === "required" || bounds.visual === "optional");
    });

    var state = required ? "required" : optional ? "optional" : "hidden";
    imageField.setAttribute("data-state", state);
    imageNote.textContent = required
      ? "This type is a synthesis of the chart or table it shows. The visual is required."
      : optional
        ? "A visual stimulus may be added; alt text is required when it is."
        : "This type shows no image.";
  }

  /* ---- AC4: the placement cap, painted before the save --------------------- */

  function paintPlacementCap() {
    var placement = placementToggle ? placementToggle.checked : false;

    if (!placement) {
      capBox.setAttribute("data-state", "off");
      capValue.textContent = "Not a placement task";
      capNote.textContent = "Placement tasks are bounded so conditions are equal across candidates.";
      return;
    }

    var cap = config.placementRecordingCap;
    var over = false;

    if (!speaking && lastAnalysis && lastAnalysis.placementWordRanges) {
      capValue.textContent = "Scoped word range";
      var ranges = lastAnalysis.placementWordRanges.map(function (range) {
        return "part " + range.part + ": " + range.floor + "–" + range.ceiling;
      });
      capNote.textContent = ranges.length > 0
        ? "D0.7 scopes a placement writing task to " + ranges.join(", ") + " words."
        : "D0.7 scopes a placement writing task below the practice envelope.";
    } else {
      var responses = parts.map(function (part) {
        return numberOrNull(part.element.querySelector("[data-part-response]").value);
      });
      responses.forEach(function (seconds) {
        if (seconds !== null && seconds > cap) over = true;
      });

      capValue.textContent = "Recording capped at " + cap + " s";
      capNote.textContent = over
        ? "A window above " + cap + " s is refused: placement is bounded for every band (D0.2, D0.7)."
        : "Every band records at most " + cap + " s in placement. Practice is not bounded.";
    }

    capBox.setAttribute("data-state", over ? "over" : "on");
  }

  /* ---- painting the word ranges and the refusals -------------------------- */

  function paintRanges(analysis) {
    rangeList.innerHTML = "";

    var ranges = analysis.placementWordRanges || [];
    if (ranges.length === 0 || !ranges[0] || (ranges[0].floor === 0 && ranges[0].ceiling === 0)) {
      var empty = document.createElement("li");
      empty.className = "studio-gauge studio-gauge--unbounded";
      empty.innerHTML = '<span class="studio-gauge__label">Not bounded by a word count</span>' +
        '<span class="studio-gauge__value font-mono">—</span>' +
        '<span class="studio-gauge__target font-mono"></span>';
      rangeList.appendChild(empty);
      return;
    }

    ranges.forEach(function (range) {
      var item = document.createElement("li");
      item.className = "studio-gauge" + (range.placementScoped ? " studio-gauge--scoped" : "");

      var label = document.createElement("span");
      label.className = "studio-gauge__label";
      label.textContent = "Part " + range.part + (range.placementScoped ? " (placement)" : "");

      var value = document.createElement("span");
      value.className = "studio-gauge__value font-mono";
      value.textContent = range.floor + "–" + range.ceiling + " w";

      var target = document.createElement("span");
      target.className = "studio-gauge__target font-mono";
      target.textContent = "D0.4";

      item.appendChild(label);
      item.appendChild(value);
      item.appendChild(target);
      rangeList.appendChild(item);
    });
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
      ? "Choose a level first."
      : acceptable
        ? "Ready to save as a draft."
        : "Cannot be saved while " + analysis.refusals.length + " rule(s) fail.";

    /* A part whose rule failed is outlined, so the stack says which one to fix. */
    var conflicted = {};
    (analysis.refusals || []).forEach(function (refusal) {
      var match = /^part (\d+):/.exec(refusal.message);
      if (match) conflicted[parseInt(match[1], 10)] = true;
    });

    parts.forEach(function (part) {
      part.element.setAttribute("data-conflict", conflicted[part.position] ? "true" : "false");
      var local = (analysis.refusals || []).filter(function (refusal) {
        return refusal.message.indexOf("part " + part.position + ":") === 0;
      });
      part.element.querySelector("[data-part-refusals]").textContent =
        local.map(function (refusal) { return refusal.message; }).join(" ");
    });
  }

  /* ---- the live call ------------------------------------------------------ */

  function analyse() {
    if (!selectedLevel) return Promise.resolve();

    var sequence = ++analysisSequence;
    return post("/admin/studio/productive/analyse", requestBody())
      .then(function (response) {
        if (!response.ok) return null;
        return response.json();
      })
      .then(function (analysis) {
        /* A slower earlier response must not overwrite a newer one: the screen would show a verdict
           about a draft that is no longer in the form. */
        if (!analysis || sequence !== analysisSequence) return;

        lastAnalysis = analysis;
        paintRanges(analysis);
        paintPlacementCap();
        paintRefusals(analysis);
        parts.forEach(paintPartHints);
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
      analyse();
    });
  });

  addPartButton.addEventListener("click", function () {
    if (parts.length >= maxParts()) return;
    var types = parts.map(function (part) { return part.typeSelect.value; });
    types.push("");
    buildParts(types.length);
    for (var i = 0; i < parts.length; i++) {
      if (types[i]) parts[i].typeSelect.value = types[i];
      paintPartHints(parts[i]);
    }
    paintImageField();
    analyse();
  });

  partsHost.addEventListener("change", function (event) {
    var select = event.target.closest("[data-part-type]");
    if (!select) return;
    var part = parts.filter(function (candidate) {
      return candidate.element === select.closest("[data-part]");
    })[0];
    if (part) paintPartHints(part);
    paintImageField();
    analyse();
  });

  partsHost.addEventListener("input", debounce(analyse, 400));
  prompt.addEventListener("input", debounce(analyse, 400));

  if (placementToggle) {
    placementToggle.addEventListener("change", function () {
      paintPlacementCap();
      analyse();
    });
  }

  if (imageInput) imageInput.addEventListener("input", debounce(analyse, 400));
  if (imageAltInput) imageAltInput.addEventListener("input", debounce(analyse, 400));

  saveButton.addEventListener("click", function () {
    saveButton.disabled = true;
    saveStatus.setAttribute("data-state", "");
    saveStatus.textContent = "Saving…";

    post("/admin/studio/productive", requestBody())
      .then(function (response) {
        return response.json().then(function (body) { return { ok: response.ok, body: body }; });
      })
      .then(function (result) {
        if (!result.ok) {
          saveStatus.setAttribute("data-state", "blocked");
          saveStatus.textContent = result.body.message || "The task was refused.";
          saveButton.disabled = false;
          return;
        }

        saveStatus.setAttribute("data-state", "ready");
        saveStatus.textContent = "Saved task " + result.body.stimulusId + " with " +
          result.body.parts.length + " part(s), all as drafts.";
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

  buildParts(1);
  paintImageField();
  paintPlacementCap();
})();
