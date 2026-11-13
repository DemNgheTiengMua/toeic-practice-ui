(function () {
  "use strict";

  const dynamic = document.getElementById("placement-take-dynamic");
  const dataIsland = () => document.getElementById("placement-take-data");
  let config = JSON.parse(dataIsland().textContent);
  let isFlagged = config.isFlagged;
  let answeredCount = config.answeredCount;
  let draftDirty = false;
  let draftSaveTimeout = null;
  let draftRequest = null;
  let speakingRecorder = null;
  let speakingStream = null;
  let speakingChunks = [];
  let speakingStartedAt = 0;
  let speakingRemaining = 0;
  let speakingTimerTimeout = null;
  let speakingUploadRequest = null;
  // Answer saves still in flight; leaving a locked part waits for them (see leaveLockedPart).
  const pendingAnswers = new Set();
  // #216 / PLC-17: answers written while offline live here, per placement, and are flushed on the
  // next tick that finds a connection. They are the CLIENT queue; the count in the header is the
  // server's count plus whatever is still in this array, said in words.
  const queueKey = "ee_placement_queue_" + config.placementId;
  let localQueue = readQueue();

  const token = document.querySelector("#anti-forgery-holder input[name='__RequestVerificationToken']")?.value || "";
  const q = selector => document.querySelector(selector);
  const qa = selector => Array.from(document.querySelectorAll(selector));
  const saveStatus = () => q("#save-status");
  const writingEditor = () => q("#writing-editor");
  const currentNavCell = () => q("#nav-cell-" + config.questionId);

  function readQueue() {
    try { return JSON.parse(localStorage.getItem(queueKey) || "[]") || []; } catch (_) { return []; }
  }

  function writeQueue() {
    try { localStorage.setItem(queueKey, JSON.stringify(localQueue)); } catch (_) { /* private mode */ }
  }

  function isOnline() {
    return navigator.onLine !== false;
  }

  function showOfflineBanner(visible) {
    const banner = q("#placement-offline-banner");
    if (banner) banner.hidden = !visible;
  }

  // The sync placard says TWO numbers: what the server holds, and what this device still holds.
  // Printing one number while the other lags is what makes an offline save look like a loss.
  function paintSyncStatus(extra) {
    const element = saveStatus();
    if (!element) return;
    const serverCount = answeredCount;
    const queued = localQueue.length;
    if (!isOnline()) {
      element.textContent = "Offline · " + queued + " saved on this device";
      element.className = "save-status save-status--offline";
      return;
    }
    if (queued > 0) {
      element.textContent = "Sending " + queued + "…";
      element.className = "save-status save-status--pending";
      return;
    }
    element.textContent = extra || (serverCount + " of " + config.totalQuestions + " saved");
    element.className = "save-status save-status--saved";
  }

  function updateWritingCounts() {
    const editor = writingEditor();
    if (!editor) return;
    const text = editor.value || "";
    const words = text.trim() ? text.trim().split(/\s+/).length : 0;
    const wordCount = q("#word-count");
    const characterCount = q("#character-count");
    if (wordCount) wordCount.textContent = words;
    if (characterCount) characterCount.textContent = text.length;
  }

  function updateProgress() {
    const text = q("#progress-text");
    const bar = q("#progress-bar");
    const modal = q("#modal-answered-count");
    if (text) text.textContent = `${answeredCount}/${config.totalQuestions} answered`;
    if (modal) modal.textContent = answeredCount;
    if (bar) bar.style.width = Math.round(answeredCount / Math.max(1, config.totalQuestions) * 100) + "%";
    refreshSubmitModalSections();
  }

  // #217 / PLC-18: the confirmation's per-section counts, repainted from the navigator itself. The
  // rows are the server's, one per section, and each cell carries its section id; a section whose
  // every item has been answered hides its row rather than printing a zero beside a real count. The
  // count is taken from `is-answered` on the cells - the same class the save path already sets - so
  // there is one live source for "has this item been answered" and not a second counter to drift.
  function refreshSubmitModalSections() {
    const rows = qa("#submit-modal-section-rows tr");
    if (rows.length === 0) return;
    rows.forEach(row => {
      const sectionId = row.dataset.sectionId;
      const cells = qa(`#placement-take-dynamic [data-section-id="${sectionId}"]`);
      if (cells.length === 0) return;
      const unanswered = cells.filter(cell => !cell.classList.contains("is-answered")).length;
      const count = row.querySelector("[data-unanswered]");
      if (count) count.textContent = String(unanswered);
      row.hidden = unanswered === 0;
    });
    const cost = q("#submit-modal-cost");
    if (cost) cost.hidden = !rows.some(row => !row.hidden);
  }

  function setStatus(text, className) {
    const element = saveStatus();
    if (element) { element.textContent = text; element.className = className; }
  }

  // #216 / PLC-17: the one outbound door. An answer is queued locally first, so a dropped connection
  // loses nothing; the queue is drained here, and a failure puts it back rather than dropping it.
  async function postAnswer(selectedOptionId, flagged) {
    localQueue = localQueue.filter(item => item.questionId !== config.questionId);
    localQueue.push({ questionId: config.questionId, selectedOptionId: selectedOptionId, isFlagged: !!flagged });
    writeQueue();
    return flushQueue();
  }

  async function flushQueue() {
    if (localQueue.length === 0) { paintSyncStatus(); return true; }
    if (!isOnline()) {
      paintSyncStatus();
      showOfflineBanner(true);
      return false;
    }

    const inFlight = localQueue.slice();
    setStatus("Saving…", "save-status save-status--pending");
    try {
      for (const item of inFlight) {
        const body = new URLSearchParams();
        if (item.selectedOptionId != null) body.append("selectedOptionId", item.selectedOptionId);
        body.append("isFlagged", item.isFlagged ? "true" : "false");
        body.append("__RequestVerificationToken", token);
        const response = await fetch(`/placement/take/${config.placementId}/answers/${item.questionId}`, {
          method: "POST",
          headers: { "Content-Type": "application/x-www-form-urlencoded", RequestVerificationToken: token },
          body: body.toString()
        });
        if (!response.ok) throw new Error("Save answer failed with status " + response.status);
        const data = await response.json().catch(() => ({}));
        if (data.selectedOptionId != null) {
          const cell = q("#nav-cell-" + item.questionId);
          cell?.classList.add("is-answered");
          cell?.classList.remove("is-unanswered");
        }
        q("#nav-cell-" + item.questionId)?.classList.toggle("is-marked", !!data.isFlagged);
        localQueue = localQueue.filter(queued => queued !== item);
        writeQueue();
      }
      showOfflineBanner(false);
      paintSyncStatus();
      return true;
    } catch (error) {
      console.error(error);
      // The queue is intact, so nothing is lost. Say so plainly instead of "Save error".
      setStatus(isOnline() ? "Not saved yet · retrying" : "Offline · saved on this device",
        isOnline() ? "save-status save-status--pending" : "save-status save-status--offline");
      showOfflineBanner(true);
      return false;
    }
  }

  async function saveDraftNow() {
    const editor = writingEditor();
    if (!editor) return true;
    if (draftSaveTimeout) clearTimeout(draftSaveTimeout);
    if (!isOnline()) {
      // #216 / PLC-17: a draft is kept on the device, and the page says so. It is flushed by the
      // same retry that flushes an answer.
      try { localStorage.setItem(queueKey + "_draft_" + config.questionId, editor.value); } catch (_) { }
      draftDirty = true;
      setStatus("Offline · draft saved on this device", "save-status save-status--offline");
      showOfflineBanner(true);
      return false;
    }
    const text = editor.value;
    setStatus("Saving draft…", "save-status save-status--pending");
    draftRequest = fetch(`/placement/take/${config.placementId}/draft`, {
      method: "POST",
      headers: { "Content-Type": "application/json", RequestVerificationToken: token },
      body: JSON.stringify({ questionId: config.questionId, responseText: text })
    }).then(async response => {
      if (!response.ok) throw new Error("Save draft failed with status " + response.status);
      draftDirty = false;
      try { localStorage.removeItem(queueKey + "_draft_" + config.questionId); } catch (_) { }
      setStatus("Draft saved", "save-status save-status--saved");
      if (text.trim()) {
        currentNavCell()?.classList.add("is-answered");
        currentNavCell()?.classList.remove("is-unanswered");
      }
      return true;
    }).catch(error => {
      console.error(error);
      setStatus("Not saved yet · retrying", "save-status save-status--pending");
      return false;
    }).finally(() => { draftRequest = null; });
    return draftRequest;
  }

  function formatClock(sec) {
    const safe = Math.max(0, Math.floor(Number(sec) || 0));
    const m = Math.floor(safe / 60);
    const s = safe % 60;
    return (m < 10 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
  }

  function setSpeakingViewState(state) {
    const idle = q("#speaking-state-idle");
    const rec = q("#speaking-state-recording");
    const up = q("#speaking-state-uploading");
    const done = q("#speaking-state-recorded");
    const btnStart = q("#btn-start-speaking-record");
    const btnStop = q("#btn-stop-speaking-record");
    const btnAgain = q("#btn-record-speaking-again");
    if (idle) idle.hidden = state !== "idle";
    if (rec) rec.hidden = state !== "recording";
    if (up) up.hidden = state !== "uploading";
    if (done) done.hidden = state !== "recorded";
    if (btnStart) btnStart.hidden = state !== "idle";
    if (btnStop) btnStop.hidden = state !== "recording";
    if (btnAgain) btnAgain.hidden = state !== "recorded";
  }

  function stopSpeakingStream() {
    if (speakingTimerTimeout) {
      clearTimeout(speakingTimerTimeout);
      speakingTimerTimeout = null;
    }
    if (speakingStream) {
      try { speakingStream.getTracks().forEach(track => track.stop()); } catch (_) { }
      speakingStream = null;
    }
  }

  async function startSpeakingRecording() {
    const banner = q("#permission-blocked-banner");
    const detail = q("#permission-blocked-detail");
    if (banner) banner.hidden = true;
    const capture = (window.EeAudioGate && typeof window.EeAudioGate.acquire === "function")
      ? await window.EeAudioGate.acquire(true)
      : { ok: false, reason: "unsupported" };
    if (!capture.ok) {
      if (banner) banner.hidden = false;
      if (detail) {
        detail.textContent = capture.reason === "unsupported"
          ? "This browser cannot record audio. Use a current version of Chrome, Edge, Firefox or Safari."
          : "The microphone was not allowed before this sitting started. Enable it on the equipment check page and reopen the sitting.";
      }
      setStatus("Microphone blocked", "save-status save-status--error");
      return;
    }

    stopSpeakingStream();
    speakingStream = capture.stream;
    speakingChunks = [];
    const totalSeconds = Number(config.responseSeconds) > 0 ? Number(config.responseSeconds) : 30;
    speakingRemaining = totalSeconds;
    const timerEl = q("#speaking-record-timer");
    const progressEl = q("#speaking-record-progress");
    if (timerEl) timerEl.textContent = formatClock(speakingRemaining);
    if (progressEl) progressEl.style.width = "0%";

    setSpeakingViewState("recording");
    setStatus("Recording…", "save-status save-status--pending");

    const targetQuestionId = config.questionId;
    speakingRecorder = new MediaRecorder(speakingStream);
    speakingRecorder.addEventListener("dataavailable", evt => {
      if (evt.data && evt.data.size > 0) speakingChunks.push(evt.data);
    });
    speakingRecorder.addEventListener("stop", () => {
      const limitMs = totalSeconds * 1000;
      const elapsedMs = Math.min(limitMs, Math.max(1000, Date.now() - speakingStartedAt));
      const blob = new Blob(speakingChunks, { type: speakingChunks[0]?.type || "audio/webm" });
      stopSpeakingStream();
      speakingRecorder = null;
      uploadSpeakingRecording(targetQuestionId, blob, elapsedMs);
    }, { once: true });

    speakingStartedAt = Date.now();
    speakingRecorder.start();

    function tickRecording() {
      speakingRemaining--;
      if (timerEl) timerEl.textContent = formatClock(speakingRemaining);
      if (progressEl) {
        const pct = Math.min(100, Math.round(((totalSeconds - speakingRemaining) / totalSeconds) * 100));
        progressEl.style.width = pct + "%";
      }
      if (speakingRemaining <= 0) {
        stopSpeakingRecording();
      } else {
        speakingTimerTimeout = setTimeout(tickRecording, 1000);
      }
    }
    speakingTimerTimeout = setTimeout(tickRecording, 1000);
  }

  function stopSpeakingRecording() {
    if (speakingTimerTimeout) {
      clearTimeout(speakingTimerTimeout);
      speakingTimerTimeout = null;
    }
    if (speakingRecorder && speakingRecorder.state === "recording") {
      speakingRecorder.stop();
    } else {
      stopSpeakingStream();
    }
  }

  function uploadSpeakingRecording(questionId, blob, durationMs) {
    setSpeakingViewState("uploading");
    setStatus("Saving recording…", "save-status save-status--pending");
    const formData = new FormData();
    formData.append("attemptId", String(config.attemptId));
    formData.append("questionId", String(questionId));
    formData.append("audioFile", blob, "speaking-q" + questionId + ".webm");
    formData.append("durationMs", String(Math.round(durationMs)));
    formData.append("__RequestVerificationToken", token);

    const player = q("#speaking-audio-player");
    const previewUrl = URL.createObjectURL(blob);
    if (player) player.src = previewUrl;

    speakingUploadRequest = fetch("/speaking/upload-audio", {
      method: "POST",
      body: formData
    }).then(async response => {
      if (!response.ok) throw new Error("Speaking audio upload failed with status " + response.status);
      const cell = q("#nav-cell-" + questionId);
      if (cell && !cell.classList.contains("is-answered")) {
        answeredCount++;
        cell.classList.add("is-answered");
        cell.classList.remove("is-unanswered");
        updateProgress();
      }
      const titleEl = q("#speaking-recorded-title");
      if (titleEl) titleEl.textContent = "Recorded (" + Math.round(durationMs / 1000) + "s)";
      setSpeakingViewState("recorded");
      paintSyncStatus("Recording saved");
      return true;
    }).catch(error => {
      console.error(error);
      const titleEl = q("#speaking-recorded-title");
      if (titleEl) titleEl.textContent = "Recording not saved — record again to retry";
      setSpeakingViewState("recorded");
      setStatus("Recording upload failed", "save-status save-status--error");
      return false;
    }).finally(() => {
      speakingUploadRequest = null;
    });
    return speakingUploadRequest;
  }

  function restorePlacementAudio() {
    const player = q('.exam-audio[data-mode="exam"]');
    if (!player) return;
    let stored = {};
    try { stored = JSON.parse(localStorage.getItem("ee_placement_audio_" + config.placementId) || "{}"); } catch (_) { }
    const progress = stored[config.audioKey];
    if (!progress) return;
    if (progress.completed) player.dataset.completed = "true";
    if (progress.currentTime > 0) player.dataset.currentTime = String(progress.currentTime);
  }

  document.addEventListener("examaudiotimeupdate", event => {
    if (!config.audioKey || !event.detail) return;
    try {
      const key = "ee_placement_audio_" + config.placementId;
      const stored = JSON.parse(localStorage.getItem(key) || "{}");
      const current = stored[config.audioKey] || {};
      stored[config.audioKey] = {
        completed: !!(event.detail.completed || current.completed),
        currentTime: Math.max(Number(current.currentTime) || 0, Number(event.detail.currentTime) || 0)
      };
      localStorage.setItem(key, JSON.stringify(stored));
    } catch (_) { }
  });

  document.addEventListener("examaudioended", event => {
    if (!config.audioKey) return;
    try {
      const key = "ee_placement_audio_" + config.placementId;
      const stored = JSON.parse(localStorage.getItem(key) || "{}");
      stored[config.audioKey] = { completed: true, currentTime: Number(stored[config.audioKey]?.currentTime) || 0 };
      localStorage.setItem(key, JSON.stringify(stored));
    } catch (_) { }
  });

  function syncUrl(displayOrder, replace) {
    const target = `/placement/take/${config.placementId}?q=${displayOrder}`;
    if (replace) window.history.replaceState({ q: displayOrder }, "", target);
    else window.history.pushState({ q: displayOrder }, "", target);
    document.title = `Placement Test - Question ${displayOrder} - extra efficient`;
  }

  async function navigate(href, replaceHistory) {
    const target = new URL(href, window.location.origin);
    const targetQuestion = target.searchParams.get("q");
    if (!targetQuestion) {
      // Browser restored a bare sitting URL: mirror the question currently on screen.
      syncUrl(config.displayOrder, true);
      return;
    }
    // A move that does not happen leaves the question on screen. On Back/forward the browser has
    // already changed the address bar, so put it back on that question.
    const stay = () => syncUrl(config.displayOrder, true);
    if (speakingRecorder && speakingRecorder.state === "recording") {
      stopSpeakingRecording();
    }
    if (speakingUploadRequest && !(await speakingUploadRequest)) { stay(); return; }
    if (draftDirty && !(await saveDraftNow())) { stay(); return; }
    // #216 / PLC-17: a move between questions is not a server-dependent boundary, but the queue is
    // flushed first so the next question's header count is real. A failed flush does not block the
    // move - the answers are on the device and the banner says so.
    await flushQueue();
    let response;
    try {
      response = await fetch(`/placement/take/${config.placementId}/fragment?q=${encodeURIComponent(targetQuestion)}`, {
        headers: { Accept: "text/html" }
      });
    } catch (error) {
      console.error(error);
      setStatus("Offline · move queued", "save-status save-status--offline");
      showOfflineBanner(true);
      stay();
      return;
    }
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      if (body.redirectUrl) { window.location.href = body.redirectUrl; return; }
      setStatus("Could not load that question", "save-status save-status--error");
      stay();
      return;
    }
    const fragment = document.createElement("template");
    fragment.innerHTML = await response.text();
    const confirmed = JSON.parse(fragment.content.getElementById("placement-take-data").textContent);
    if (confirmed.displayOrder === config.displayOrder) {
      // A refused rewind (Back into a one-way section) returns the question already on screen.
      // Re-rendering it would tear out the live single-play player and hand back a fresh,
      // playable one, so only the address bar is corrected.
      syncUrl(config.displayOrder, true);
      return;
    }
    stopSpeakingStream();
    dynamic.replaceChildren(fragment.content);
    config = confirmed;
    isFlagged = config.isFlagged;
    answeredCount = config.answeredCount;
    draftDirty = false;
    updateWritingCounts();
    updateProgress();
    paintSyncStatus();
    // The server owns the position: a refused jump comes back as a different question, and the
    // address bar must follow the confirmed one rather than the requested one.
    syncUrl(config.displayOrder, replaceHistory || String(config.displayOrder) !== targetQuestion);
    // exam-audio.js reads data-completed/data-current-time once, when it initialises a player,
    // so the stored progress has to be on the element first.
    restorePlacementAudio();
    window.initExamAudio?.();
  }

  window.addEventListener("popstate", () => {
    // Back/forward can target a question the server locked; re-ask the server and replace the entry.
    navigate(window.location.href, true);
  });

  document.addEventListener("click", event => {
    const link = event.target.closest("a[href^='/placement/take/']");
    if (link && link.closest("#placement-take-dynamic")) {
      event.preventDefault();
      if (link.dataset.confirmLeave) leaveLockedPart(link);
      else navigate(link.href);
      return;
    }
    const flag = event.target.closest("#flag-btn");
    if (flag) {
      isFlagged = !isFlagged;
      flag.dataset.flagged = String(isFlagged);
      flag.setAttribute("aria-pressed", String(isFlagged));
      flag.textContent = isFlagged ? "Flagged for review" : "Flag for review";
      flag.classList.toggle("btn--primary", isFlagged);
      postAnswer(q(".js-option-radio:checked")?.value || null, isFlagged);
      return;
    }
    if (event.target.closest("#btn-start-speaking-record") || event.target.closest("#btn-record-speaking-again")) {
      startSpeakingRecording();
      return;
    }
    if (event.target.closest("#btn-stop-speaking-record")) {
      stopSpeakingRecording();
      return;
    }
    const saveDraft = event.target.closest("#btn-save-writing-draft");
    if (saveDraft) saveDraftNow();
    const openModal = event.target.closest(".js-open-submit-modal");
    if (openModal) { refreshSubmitModalSections(); q("#submit-modal")?.style.setProperty("display", "flex"); }
    if (event.target.closest("#btn-cancel-submit")) q("#submit-modal")?.style.setProperty("display", "none");
  });

  document.addEventListener("input", event => {
    if (!event.target.matches("#writing-editor")) return;
    draftDirty = true;
    updateWritingCounts();
    setStatus("Draft not saved", "save-status save-status--pending");
    if (draftSaveTimeout) clearTimeout(draftSaveTimeout);
    draftSaveTimeout = setTimeout(saveDraftNow, 3000);
  });

  document.addEventListener("change", event => {
    if (!event.target.matches(".js-option-radio")) return;
    qa(".option").forEach(option => option.classList.remove("is-selected"));
    event.target.closest(".option")?.classList.add("is-selected");
    const cell = currentNavCell();
    if (!cell?.classList.contains("is-answered")) answeredCount++;
    updateProgress();
    postAnswer(event.target.value, isFlagged);
  });

  // Capped, so a hung save never traps the candidate on the last question of a locked part.
  function settlePendingAnswers() {
    if (speakingRecorder && speakingRecorder.state === "recording") {
      stopSpeakingRecording();
    }
    const inFlight = Array.from(pendingAnswers);
    if (draftRequest) inFlight.push(draftRequest);
    if (speakingUploadRequest) inFlight.push(speakingUploadRequest);
    if (localQueue.length > 0) inFlight.push(flushQueue());
    if (inFlight.length === 0) return Promise.resolve();
    return Promise.race([
      Promise.allSettled(inFlight),
      new Promise(resolve => setTimeout(resolve, 5000))
    ]);
  }

  // The Next control on the last question of a skill or of a locked question group. Leaving closes
  // that part for good, so confirm first, as the mock exam does. Saves in flight land first, then
  // the writing draft is saved as it stands: an autosave that finished while the candidate kept
  // typing leaves the newest text marked as saved. The server opens the next part when asked for
  // its first question, so the sitting then moves there like any other question, without a reload.
  async function leaveLockedPart(link) {
    if (!window.confirm(link.dataset.confirmLeave)) return;
    await settlePendingAnswers();
    // #216 / PLC-17: a part boundary is server-dependent. If the queue could not be flushed, pause
    // here rather than moving on and pretending the answers landed.
    if (localQueue.length > 0) {
      showOfflineBanner(true);
      setStatus("Waiting for a connection · " + localQueue.length + " on this device", "save-status save-status--offline");
      return;
    }
    if (writingEditor()?.value.trim() && !(await saveDraftNow())) return;
    navigate(link.href);
  }

  // #216 / PLC-17: reconnection drains the queue. The banner stays up until it is empty, so the
  // state is never "offline" over an empty device nor "saved" over a full one.
  window.addEventListener("online", () => {
    showOfflineBanner(false);
    flushQueue();
    restoreLocalDraft();
  });
  window.addEventListener("offline", () => {
    showOfflineBanner(true);
    paintSyncStatus();
  });

  // A draft typed offline is still on the device when the page comes back.
  function restoreLocalDraft() {
    const editor = writingEditor();
    if (!editor || !isOnline()) return;
    let stored = null;
    try { stored = localStorage.getItem(queueKey + "_draft_" + config.questionId); } catch (_) { }
    if (stored == null || stored === editor.value) return;
    editor.value = stored;
    draftDirty = true;
    updateWritingCounts();
    saveDraftNow();
  }

  // #214 / PLC-12: there is no overall countdown, so nothing here ticks and nothing auto-submits on
  // elapsed time. The sitting ends when the learner submits, or when the server's own outer bound
  // (min(last activity + 48 h, started + 72 h)) is reached - enforced server-side, where a closed
  // tab cannot escape it.

  q("#submit-form")?.addEventListener("submit", async event => {
    // #216 / PLC-17: final submission is a server-dependent boundary. It waits for the queue; it
    // does not submit a partial paper and let the missing answers look unanswered.
    if (localQueue.length > 0 || !isOnline()) {
      event.preventDefault();
      showOfflineBanner(true);
      setStatus("Cannot submit yet · " + localQueue.length + " answer(s) still on this device",
        "save-status save-status--offline");
      await flushQueue();
      return;
    }
    if (draftDirty) {
      event.preventDefault();
      if (await saveDraftNow()) event.target.submit();
    } else {
      try { localStorage.removeItem("ee_placement_audio_" + config.placementId); } catch (_) { }
      try { localStorage.removeItem(queueKey); } catch (_) { }
    }
  });

  updateWritingCounts();
  restorePlacementAudio();
  paintSyncStatus();
  if (!isOnline()) showOfflineBanner(true);
  else flushQueue();
})();
