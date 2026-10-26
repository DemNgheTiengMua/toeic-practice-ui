(function() {
  const panel = document.getElementById('ai-insight-panel');
  if (!panel) return;

  const scope = panel.dataset.scope;
  const activityId = panel.dataset.activityId;
  let currentStatus = panel.dataset.status;
  let pollCount = 0;
  const maxPolls = 30; // 60 seconds maximum polling bound
  let elapsedTimerId = null;
  let elapsedStartMs = Date.now();

  const claudeThinkingWords = [
    "Accomplishing", "Actioning", "Actualizing", "Architecting", "Baking", "Beaming", "Beboppin'", "Befuddling", "Billowing",
    "Blanching", "Bloviating", "Boogieing", "Boondoggling", "Booping", "Bootstrapping", "Brewing", "Bunning", "Burrowing",
    "Calculating", "Canoodling", "Caramelizing", "Cascading", "Catapulting", "Cerebrating", "Channeling", "Channelling",
    "Choreographing", "Churning", "Clauding", "Coalescing", "Cogitating", "Combobulating", "Composing", "Computing",
    "Concocting", "Considering", "Contemplating", "Cooking", "Crafting", "Creating", "Crunching", "Crystallizing",
    "Cultivating", "Deciphering", "Deliberating", "Determining", "Dilly-dallying", "Discombobulating", "Doing", "Doodling",
    "Drizzling", "Ebbing", "Effecting", "Elucidating", "Embellishing", "Enchanting", "Envisioning", "Evaporating",
    "Fermenting", "Fiddle-faddling", "Finagling", "Flambéing", "Flibbertigibbeting", "Flowing", "Flummoxing", "Fluttering",
    "Forging", "Forming", "Frolicking", "Frosting", "Gallivanting", "Galloping", "Garnishing", "Generating", "Gesticulating",
    "Germinating", "Gitifying", "Grooving", "Gusting", "Harmonizing", "Hashing", "Hatching", "Herding", "Honking",
    "Hullaballooing", "Hyperspacing", "Ideating", "Imagining", "Improvising", "Incubating", "Inferring", "Infusing",
    "Ionizing", "Jitterbugging", "Julienning", "Kneading", "Leavening", "Levitating", "Lollygagging", "Manifesting",
    "Marinating", "Meandering", "Metamorphosing", "Misting", "Moonwalking", "Moseying", "Mulling", "Mustering", "Musing",
    "Nebulizing", "Nesting", "Newspapering", "Noodling", "Nucleating", "Orbiting", "Orchestrating", "Osmosing",
    "Perambulating", "Percolating", "Perusing", "Philosophising", "Photosynthesizing", "Pollinating", "Pondering",
    "Pontificating", "Pouncing", "Precipitating", "Prestidigitating", "Processing", "Proofing", "Propagating", "Puttering",
    "Puzzling", "Quantumizing", "Razzle-dazzling", "Razzmatazzing", "Recombobulating", "Reticulating", "Roosting",
    "Ruminating", "Sautéing", "Scampering", "Schlepping", "Scurrying", "Seasoning", "Shenaniganing", "Shimmying",
    "Simmering", "Skedaddling", "Sketching", "Slithering", "Smooshing", "Sock-hopping", "Spelunking", "Spinning",
    "Sprouting", "Stewing", "Sublimating", "Swirling", "Swooping", "Symbioting", "Synthesizing", "Tempering", "Thinking",
    "Thundering", "Tinkering", "Tomfoolering", "Topsy-turvying", "Transfiguring", "Transmuting", "Twisting", "Undulating",
    "Unfurling", "Unravelling", "Vibing", "Waddling", "Wandering", "Warping", "Whatchamacalliting", "Whirlpooling",
    "Whirring", "Whisking", "Wibbling", "Working", "Wrangling", "Zesting", "Zigzagging"
  ];

  function startKineticTimer() {
    stopKineticTimer();
    elapsedStartMs = Date.now();
    elapsedTimerId = setInterval(function() {
      const elapsedSec = (Date.now() - elapsedStartMs) / 1000;
      const elapsedEl = document.getElementById('ai-insight-elapsed');
      if (elapsedEl) {
        const formattedSec = elapsedSec < 10 ? '0' + elapsedSec.toFixed(1) : elapsedSec.toFixed(1);
        elapsedEl.textContent = `ELAPSED: ${formattedSec}s`;
      }
      const wordIndex = Math.floor(elapsedSec / 1.4) % claudeThinkingWords.length;
      const activeWord = claudeThinkingWords[wordIndex];
      const phaseEl = document.getElementById('ai-insight-phase');
      if (phaseEl) {
        phaseEl.textContent = `CLAUDE AI // ${activeWord.toUpperCase()}…`;
      }
      const titleEl = document.querySelector('#ai-insight-loading .state-block__title');
      if (titleEl) {
        titleEl.textContent = `${activeWord} insights…`;
      }
    }, 100);
  }

  function stopKineticTimer() {
    if (elapsedTimerId !== null) {
      clearInterval(elapsedTimerId);
      elapsedTimerId = null;
    }
  }

  function getEndpoint() {
    if (scope === 'dashboard') {
      return activityId && activityId !== '0'
        ? `/learner/insights/dashboard?certId=${activityId}`
        : '/learner/insights/dashboard';
    }
    if (scope === 'path') {
      return '/learner/insights/path';
    }
    return scope === 'practice_session'
      ? `/practice/insight/${activityId}`
      : `/exams/insight/${activityId}`;
  }

  function getRetryEndpoint() {
    if (scope === 'dashboard') {
      return activityId && activityId !== '0'
        ? `/learner/insights/dashboard/retry?certId=${activityId}`
        : '/learner/insights/dashboard/retry';
    }
    if (scope === 'path') {
      return '/learner/insights/path/retry';
    }
    return scope === 'practice_session'
      ? `/practice/insight/${activityId}/retry`
      : `/exams/insight/${activityId}/retry`;
  }

  function getCsrfToken() {
    const input = document.querySelector('#ai-insight-csrf input[name="__RequestVerificationToken"]');
    return input ? input.value : '';
  }

  function renderDone(data) {
    stopKineticTimer();
    const body = document.getElementById('ai-insight-body');
    const badges = document.getElementById('ai-insight-header-badges');
    if (!body) return;

    panel.dataset.status = 'done';
    if (data.certId && (!panel.dataset.activityId || panel.dataset.activityId === '0')) {
      panel.dataset.activityId = data.certId;
    }

    // Update trend badge while preserving accuracy badge if present
    if (badges && data.trend !== undefined && data.trend !== null) {
      let trendClass = 'badge--warning';
      let trendText = 'Trend: Flat';
      const t = (data.trend || '').toString().toLowerCase();
      if (t === 'improving' || t === '0') {
        trendClass = 'badge--success';
        trendText = 'Trend: Improving';
      } else if (t === 'declining' || t === '2') {
        trendClass = 'badge--danger';
        trendText = 'Trend: Declining';
      }
      const existingTrend = document.getElementById('ai-insight-trend-badge');
      if (existingTrend) {
        existingTrend.className = `badge ${trendClass} text-xs font-mono`;
        existingTrend.textContent = trendText;
      } else {
        const span = document.createElement('span');
        span.id = 'ai-insight-trend-badge';
        span.className = `badge ${trendClass} text-xs font-mono`;
        span.textContent = trendText;
        badges.appendChild(span);
      }
    }

    let recsHtml = '';
    if (data.recommendations && data.recommendations.length > 0) {
      let itemsHtml = data.recommendations.map(r => {
        let badgeClass = 'badge--info';
        let label = 'Tip';
        if (r.severity === 'major' || r.severity === 2) {
          badgeClass = 'badge--danger';
          label = 'Priority';
        } else if (r.severity === 'minor' || r.severity === 1) {
          badgeClass = 'badge--warning';
          label = 'Focus';
        }
        let bandTag = r.targetBandCode ? `<span class="badge badge--muted text-xs">Band ${escapeHtml(r.targetBandCode)}</span>` : '';
        return `
          <div class="row row--between row--baseline p-2" style="background: var(--surface-soft); border: 1px solid var(--rule-soft);">
            <div class="row row--baseline">
              <span class="badge ${badgeClass} text-xs">${label}</span>
              <span class="text-sm">${escapeHtml(r.text)}</span>
            </div>
            ${bandTag}
          </div>
        `;
      }).join('');

      recsHtml = `
        <div class="mt-4 pt-3" style="border-top: 1px solid var(--rule-soft);">
          <div class="text-xs uppercase text-muted mb-2 font-bold font-sign" style="letter-spacing: 0.05em;">Recommended Next Steps:</div>
          <div class="stack" style="gap: var(--space-2);">
            ${itemsHtml}
          </div>
        </div>
      `;
    }

    const narrativeHtml = data.formattedNarrative
      ? data.formattedNarrative
      : formatNarrative(data.narrative || '');

    body.innerHTML = `
      <div id="ai-insight-content">
        <div class="ai-insight-narrative" style="line-height: 1.6; font-size: 15px; margin-bottom: var(--space-4); color: var(--ink);">
          ${narrativeHtml}
        </div>
        ${recsHtml}
        <div class="text-xs text-muted mt-3 font-mono">
          Model: ${escapeHtml(data.model || 'fake-ai-insight-v1')}
        </div>
      </div>
    `;
  }

  function renderFailed(reason) {
    stopKineticTimer();
    const body = document.getElementById('ai-insight-body');
    if (!body) return;
    panel.dataset.status = 'failed';
    body.innerHTML = `
      <div class="state-block p-4" id="ai-insight-failed">
        <div class="state-block__icon state-block__icon--danger" aria-hidden="true">FAILED</div>
        <div class="state-block__title">AI Insight Generation Did Not Complete</div>
        <p class="state-block__desc" id="ai-insight-failure-reason">
          ${escapeHtml(reason || 'Could not generate AI diagnostic insight at this time.')}
        </p>
        <button type="button" class="btn btn--primary mt-2" id="ai-insight-retry-btn" onclick="retryAiInsight()">Retry Analysis</button>
      </div>
    `;
  }

  function renderLoading() {
    const body = document.getElementById('ai-insight-body');
    if (!body) return;
    panel.dataset.status = 'pending';
    body.innerHTML = `
      <div class="state-block p-4" id="ai-insight-loading">
        <div class="row row--between row--middle row--wrap mb-2" style="width: 100%;">
          <span class="badge badge--warning font-mono uppercase font-bold" id="ai-insight-phase">CLAUDE AI // ${claudeThinkingWords[0].toUpperCase()}…</span>
          <span class="font-mono text-xs font-bold" id="ai-insight-elapsed" aria-live="polite">ELAPSED: 00.0s</span>
        </div>
        <div class="state-block__title">${claudeThinkingWords[0]} insights…</div>
        <p class="state-block__desc">
          Evaluating answer patterns, luck vs. competence, and alignment with your target band. This will take just a few seconds.
        </p>
        <div class="ai-kinetic-bar mt-3" role="progressbar" aria-label="AI insight generation in progress">
          <div class="ai-kinetic-bar__fill"></div>
        </div>
      </div>
    `;
    startKineticTimer();
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function formatNarrative(str) {
    const escaped = escapeHtml(str || '');
    return escaped
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)/g, '<em>$1</em>');
  }

  window.retryAiInsight = async function() {
    renderLoading();
    pollCount = 0;
    try {
      const csrfToken = getCsrfToken();
      const resp = await fetch(getRetryEndpoint(), {
        method: 'POST',
        headers: {
          'RequestVerificationToken': csrfToken,
          'Content-Type': 'application/x-www-form-urlencoded'
        },
        body: `__RequestVerificationToken=${encodeURIComponent(csrfToken)}`
      });

      if (resp.ok) {
        setTimeout(pollStatus, 1000);
      } else {
        renderFailed('Retry request could not be processed. Please try again.');
      }
    } catch (err) {
      renderFailed('Network error while requesting retry.');
    }
  };

  async function pollStatus() {
    if (pollCount++ >= maxPolls) {
      renderFailed('AI insight generation is taking longer than expected. Click retry to run analysis again.');
      return;
    }

    try {
      const resp = await fetch(getEndpoint());
      if (resp.ok) {
        const data = await resp.json();
        const s = (data.status || '').toLowerCase();
        if (s === 'done' || s === '1') {
          renderDone(data);
          return;
        }
        if (s === 'failed' || s === '2') {
          renderFailed(data.failureReason);
          return;
        }
      } else if (resp.status === 401 || resp.status === 403 || resp.status === 404) {
        renderFailed('Insight data is unavailable for this session or user.');
        return;
      }
    } catch (err) {
      // Continue polling on transient errors
    }

    setTimeout(pollStatus, 2000);
  }

  if (currentStatus === 'pending' || !currentStatus) {
    startKineticTimer();
    setTimeout(pollStatus, 1500);
  }
})();
