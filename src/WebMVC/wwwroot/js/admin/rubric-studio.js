/**
 * #208 — rubric and grader calibration studio.
 *
 * Two jobs, and neither of them computes a verdict:
 *
 *  1. Turn the 4x7 table into a SaveRubricRequest. That is a read of `data-criterion` and
 *     `data-band` off every field, so the coordinate is what travels and not the column's position.
 *  2. Forward run-ladder and lock to the MVC endpoints, and paint what the API said.
 *
 * The verdicts - the plot, the criterion the ladder names, which of the three failures fired - all come
 * back from the API. Nothing here re-derives a threshold: a screen that recomputed 0.65 would eventually
 * disagree with the rule that refuses the lock.
 */
(function () {
  "use strict";

  var root = document.getElementById("rubric-studio");
  if (!root) { return; }

  var config = JSON.parse(document.getElementById("rubric-config").textContent);
  var form = document.getElementById("rubric-form");
  var status = document.getElementById("rubric-save-status");
  var runButton = document.getElementById("rubric-run-ladder");
  var lockButton = document.getElementById("rubric-lock");
  var saveButton = document.getElementById("rubric-save");
  var filledLabel = document.getElementById("rubric-filled");
  var ladderPanel = document.getElementById("rubric-ladder");

  function antiForgeryToken() {
    var input = form && form.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
  }

  function fields(field) {
    return Array.prototype.slice.call(
      root.querySelectorAll('[data-field="' + field + '"][data-criterion][data-band]'));
  }

  /**
   * The grid as posted. Bands come from the element's own attributes, so a criterion whose cells are
   * rendered in a different order still posts the right coordinate - the alternative, using the array
   * index, is how seven cells land on bands 0-6 in the wrong order.
   */
  function gridRequest() {
    var criteria = {};
    var order = [];

    fields("descriptor").concat(fields("gates")).forEach(function (el) {
      var id = el.getAttribute("data-criterion");
      if (!criteria[id]) {
        criteria[id] = { criterionId: Number(id), cells: {}, anchors: {} };
        order.push(id);
      }
      var band = Number(el.getAttribute("data-band"));
      criteria[id].cells[band] = criteria[id].cells[band] || { band: band, descriptor: "", binaryGates: "" };
      if (el.getAttribute("data-field") === "descriptor") {
        criteria[id].cells[band].descriptor = el.value;
      } else {
        criteria[id].cells[band].binaryGates = el.value;
      }
    });

    fields("anchor").forEach(function (el) {
      var id = el.getAttribute("data-criterion");
      if (!criteria[id]) { return; }
      var band = Number(el.getAttribute("data-band"));
      criteria[id].anchors[band] = { band: band, responseText: el.value };
    });

    return {
      certId: config.certId,
      skillId: config.skillId,
      criteria: order.map(function (id) {
        var criterion = criteria[id];
        return {
          criterionId: criterion.criterionId,
          // The server re-reads the criterion's identity from the database; these are carried only so
          // the posted shape is the same one the get returns.
          code: "", name: "", maxScore: 0, weight: 0, displayOrder: 0,
          cells: Object.keys(criterion.cells).map(function (b) { return criterion.cells[b]; }),
          anchors: Object.keys(criterion.anchors).map(function (b) { return criterion.anchors[b]; }),
        };
      }),
    };
  }

  function refreshCounts() {
    var descriptors = fields("descriptor");
    var filled = descriptors.filter(function (el) {
      var gate = root.querySelector('[data-field="gates"][data-criterion="' + el.getAttribute("data-criterion") +
                                     '"][data-band="' + el.getAttribute("data-band") + '"]');
      return el.value.trim().length > 0 && gate && gate.value.trim().length > 0;
    }).length;

    if (filledLabel) { filledLabel.textContent = filled + "/" + descriptors.length; }
    root.querySelectorAll(".rubric-grid__cell").forEach(function (cell) {
      var criterion = cell.getAttribute("data-criterion");
      var band = cell.getAttribute("data-band");
      var descriptor = cell.querySelector('[data-field="descriptor"]');
      var gate = cell.querySelector('[data-field="gates"]');
      var complete = descriptor.value.trim().length > 0 && gate.value.trim().length > 0;
      cell.classList.toggle("is-empty", !complete);
    });

    // The lock is offered only once the grid is whole. This is an affordance, not the rule: the lock's
    // real guard is the service and the schema trigger, and a disabled button that were the only check
    // would be a lie the first time anyone posted the endpoint directly.
    if (lockButton) {
      lockButton.disabled = filled !== descriptors.length || descriptors.length === 0;
    }
    return filled === descriptors.length && descriptors.length > 0;
  }

  /**
   * Seven responses is the ladder's own shape: six band benchmarks and one verbose trap. The studio
   * carries them as one text block each so the author can see all seven at once - the alternative, a
   * seven-step wizard, loses the shape.
   */
  function ladderRequest() {
    var responses = [];
    config.allBands.forEach(function (band) {
      if (band < 1) { return; }
      var el = document.getElementById("ladder-response-" + band);
      responses.push({ band: band, responseText: el ? el.value : "", isVerboseTrap: false });
    });
    var trap = document.getElementById("ladder-response-trap");
    responses.push({ band: 3, responseText: trap ? trap.value : "", isVerboseTrap: true });

    return { certId: config.certId, skillId: config.skillId, responses: responses };
  }

  function post(url, body) {
    return fetch(url, {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": antiForgeryToken(),
      },
      body: JSON.stringify(body),
    });
  }

  function say(message, kind) {
    if (!status) { return; }
    status.textContent = message;
    status.classList.remove("is-refused", "is-accepted");
    if (kind) { status.classList.add(kind); }
  }

  /**
   * Paint a verdict. The sentence is the API's; the plot is the API's points. Three failures get three
   * labels so an author is never told "not calibrated" - see RubricLadderOutcomes.Label.
   */
  function paintLadder(ladder) {
    if (!ladder || !ladderPanel) { return; }

    ladderPanel.setAttribute("data-has-run", ladder.outcome === "not_plotted" ? "false" : "true");

    var verdict = document.getElementById("rubric-ladder-verdict");
    if (verdict) {
      verdict.setAttribute("data-outcome", ladder.outcome);
      verdict.textContent = ladder.label || ladder.outcome;
    }

    var reason = document.getElementById("rubric-ladder-reason");
    if (reason) { reason.textContent = ladder.reason || ""; }

    var responsible = document.getElementById("rubric-ladder-responsible");
    if (responsible) {
      var codes = (ladder.responsibleCriteria || []);
      responsible.textContent = codes.length ? "Rewrite: " + codes.join(", ") : "";
    }

    var bars = document.getElementById("rubric-ladder-bars");
    if (bars && ladder.points) {
      bars.innerHTML = "";
      ladder.points.forEach(function (point) {
        var item = document.createElement("li");
        item.className = "rubric-ladder__bar" + (point.isVerboseTrap ? " rubric-ladder__bar--trap" : "");
        item.setAttribute("data-band", String(point.band));
        item.setAttribute("data-composite", String(point.composite));

        var percent = Math.max(0, Math.min(100, Math.round((point.composite / 6) * 100)));
        item.innerHTML =
          '<span class="rubric-ladder__bar-label font-mono">' + point.band + '</span>' +
          '<span class="rubric-ladder__bar-track"><span class="rubric-ladder__bar-fill" style="width: ' +
            percent + '%"></span></span>' +
          '<span class="rubric-ladder__bar-value font-mono">' + Number(point.composite).toFixed(2) + '</span>';
        bars.appendChild(item);
      });
    }

    if (ladder.accepted) {
      say("The ladder separated every adjacent band. The rubric can be locked.", "is-accepted");
    } else {
      var named = (ladder.responsibleCriteria || []).join(", ");
      say((ladder.label || "The ladder refused this rubric.") +
          (named ? " Criterion " + named + " is responsible." : "") +
          (ladder.ruleCode ? " [" + ladder.ruleCode + "]" : ""), "is-refused");
    }
  }

  if (saveButton && form) {
    saveButton.addEventListener("click", function (event) {
      event.preventDefault();
      say("Saving…");
      post("/admin/studio/rubric/save", gridRequest())
        .then(function (response) {
          return response.json().then(function (body) { return { ok: response.ok, body: body }; });
        })
        .then(function (result) {
          if (result.ok) {
            say("Grid saved: " + result.body.filledCells + " cells across " + result.body.criteria + " criteria.", "is-accepted");
          } else {
            say(result.body.message || "The grid was refused.", "is-refused");
          }
        })
        .catch(function () { say("Could not reach the rubric service. Nothing was saved.", "is-refused"); });
    });
  }

  if (runButton) {
    runButton.addEventListener("click", function () {
      if (!refreshCounts()) {
        say("Fill every cell before running the ladder: a band with no standard is a band the ladder cannot measure.", "is-refused");
        return;
      }
      say("Running the ladder over seven responses…");
      post("/admin/studio/rubric/ladder", ladderRequest())
        .then(function (response) {
          return response.json().then(function (body) { return { ok: response.ok, body: body }; });
        })
        .then(function (result) {
          if (!result.ok) { say(result.body.message || "The ladder was refused.", "is-refused"); return; }
          paintLadder(result.body);
        })
        .catch(function () { say("Could not reach the rubric service. The ladder did not run.", "is-refused"); });
    });
  }

  if (lockButton) {
    lockButton.addEventListener("click", function () {
      say("Locking…");
      post("/admin/studio/rubric/lock", ladderRequest())
        .then(function (response) {
          return response.json().then(function (body) { return { ok: response.ok, body: body }; });
        })
        .then(function (result) {
          if (!result.ok) { say(result.body.message || "The lock was refused.", "is-refused"); return; }
          paintLadder(result.body.ladder);
          if (result.body.locked) {
            root.setAttribute("data-locked", "true");
            document.querySelectorAll("#rubric-grid textarea, #rubric-anchors textarea").forEach(function (el) {
              el.readOnly = true;
            });
            if (saveButton) { saveButton.disabled = true; }
            if (runButton) { runButton.disabled = true; }
            lockButton.disabled = true;
            var lockState = document.getElementById("rubric-lock-state");
            if (lockState) { lockState.textContent = "Locked"; }
          } else {
            // The refusal names which of the three failed; the panel already carries its sentence.
            say("The rubric cannot be locked. Fix the criterion the ladder names, then run it again.", "is-refused");
          }
        })
        .catch(function () { say("Could not reach the rubric service. The rubric was not locked.", "is-refused"); });
    });
  }

  root.addEventListener("input", function (event) {
    if (event.target && event.target.getAttribute && event.target.getAttribute("data-field")) {
      refreshCounts();
    }
  });

  refreshCounts();
})();
