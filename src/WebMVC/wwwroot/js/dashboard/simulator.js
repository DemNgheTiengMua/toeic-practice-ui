(function () {
        "use strict";
        var root = document.getElementById("dashboard-simulator");
        if (!root) return;

        var sliders = Array.prototype.slice.call(root.querySelectorAll("[data-sim-slider]"));
        var bandCells = Array.prototype.slice.call(root.querySelectorAll("[data-sim-band-cell]"));
        var totalEl = document.getElementById("sim-total-score");
        var bandCodeEl = document.getElementById("sim-matched-band-code");
        var bandNameEl = document.getElementById("sim-matched-band-name");
        var deficitBadgeEl = document.getElementById("sim-deficit-badge");
        var leverageTextEl = document.getElementById("sim-leverage-text");
        var resetBtn = document.getElementById("sim-reset-btn");

        var targetCode = root.getAttribute("data-target-code") || "";
        var targetMin = parseInt(root.getAttribute("data-target-min") || "0", 10);
        var bands = [];
        try {
          bands = JSON.parse(root.getAttribute("data-bands") || "[]");
        } catch (e) {
          bands = [];
        }

        function matchBand(score) {
          if (!bands.length) return null;
          for (var i = 0; i < bands.length; i++) {
            if (score >= bands[i].minTotal && score <= bands[i].maxTotal) {
              return bands[i];
            }
          }
          return score < bands[0].minTotal ? bands[0] : bands[bands.length - 1];
        }

        function recalculate() {
          var simulatedTotal = 0;
          var bestSkill = null;
          var bestHeadroom = -1;

          for (var i = 0; i < sliders.length; i++) {
            var s = sliders[i];
            var skillId = s.getAttribute("data-skill-id");
            var skillName = s.getAttribute("data-skill-name") || "";
            var maxScaled = parseInt(s.getAttribute("data-max-score") || "0", 10);
            var val = parseInt(s.value || "0", 10);
            var headroom = Math.max(0, maxScaled - val);

            simulatedTotal += val;

            var valOut = root.querySelector('[data-sim-skill-value="' + skillId + '"]');
            if (valOut) valOut.textContent = String(val);

            var headroomOut = root.querySelector('[data-sim-skill-headroom="' + skillId + '"]');
            if (headroomOut) headroomOut.textContent = "+" + headroom + " headroom";

            if (headroom > bestHeadroom) {
              bestHeadroom = headroom;
              bestSkill = { name: skillName, maxScaled: maxScaled, headroom: headroom };
            }
          }

          if (totalEl) totalEl.textContent = String(simulatedTotal);

          var matched = matchBand(simulatedTotal);
          if (matched) {
            if (bandCodeEl) bandCodeEl.textContent = matched.code;
            if (bandNameEl) bandNameEl.textContent = matched.name;
          }

          for (var j = 0; j < bandCells.length; j++) {
            var cell = bandCells[j];
            var cCode = cell.getAttribute("data-band-code");
            var cMax = parseInt(cell.getAttribute("data-max-total") || "0", 10);
            var isCurrent = matched && cCode === matched.code;
            var isReached = !isCurrent && simulatedTotal > cMax;

            cell.classList.toggle("is-current", Boolean(isCurrent));
            cell.classList.toggle("is-reached", Boolean(isReached));
            cell.setAttribute("aria-current", isCurrent ? "true" : "false");
          }

          var deficit = Math.max(0, targetMin - simulatedTotal);
          if (deficitBadgeEl) {
            deficitBadgeEl.setAttribute("data-deficit", String(deficit));
            if (deficit > 0) {
              deficitBadgeEl.className = "badge badge--warning font-mono";
              deficitBadgeEl.textContent = "-" + deficit + " pts to " + targetCode;
            } else {
              deficitBadgeEl.className = "badge badge--success font-mono";
              deficitBadgeEl.textContent = "TARGET " + targetCode + " REACHED";
            }
          }

          if (leverageTextEl && bestSkill) {
            leverageTextEl.textContent = "";
            if (bestSkill.headroom > 0) {
              leverageTextEl.appendChild(document.createTextNode("Prioritize "));
              var strong = document.createElement("strong");
              strong.id = "sim-leverage-skill";
              strong.textContent = bestSkill.name;
              leverageTextEl.appendChild(strong);
              leverageTextEl.appendChild(document.createTextNode(" ("));
              var span = document.createElement("span");
              span.id = "sim-leverage-headroom";
              span.className = "font-mono font-bold";
              span.textContent = "+" + bestSkill.headroom + " pts";
              leverageTextEl.appendChild(span);
              leverageTextEl.appendChild(
                document.createTextNode(" remaining headroom to " + bestSkill.maxScaled + ") for the largest potential score gain.")
              );
            } else {
              leverageTextEl.appendChild(document.createTextNode("All skills are set to their maximum scaled ceiling ("));
              var strongMax = document.createElement("strong");
              strongMax.id = "sim-leverage-skill";
              strongMax.textContent = bestSkill.name;
              leverageTextEl.appendChild(strongMax);
              leverageTextEl.appendChild(document.createTextNode(" · "));
              var spanMax = document.createElement("span");
              spanMax.id = "sim-leverage-headroom";
              spanMax.className = "font-mono font-bold";
              spanMax.textContent = "+0 pts";
              leverageTextEl.appendChild(spanMax);
              leverageTextEl.appendChild(document.createTextNode(" headroom)."));
            }
          }
        }

        for (var k = 0; k < sliders.length; k++) {
          sliders[k].addEventListener("input", recalculate);
          sliders[k].addEventListener("change", recalculate);
        }

        if (resetBtn) {
          resetBtn.addEventListener("click", function () {
            for (var i = 0; i < sliders.length; i++) {
              var base = sliders[i].getAttribute("data-base-score");
              if (base !== null) sliders[i].value = base;
            }
            recalculate();
          });
        }
      })();
