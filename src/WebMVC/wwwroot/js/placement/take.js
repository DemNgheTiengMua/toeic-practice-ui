(function () {
  "use strict";

  const dynamic = document.getElementById("placement-take-dynamic");
  const dataIsland = () => document.getElementById("placement-take-data");
  let config = JSON.parse(dataIsland().textContent);
  let remainingSeconds = config.remainingSeconds;
  let isFlagged = config.isFlagged;
  let answeredCount = config.answeredCount;
  let draftDirty = false;
  let draftSaveTimeout = null;
  let draftRequest = null;

  const token = document.querySelector("#anti-forgery-holder input[name='__RequestVerificationToken']")?.value || "";
  const q = selector => document.querySelector(selector);
  const qa = selector => Array.from(document.querySelectorAll(selector));
  const saveStatus = () => q("#save-status");
  const writingEditor = () => q("#writing-editor");
  const currentNavCell = () => q("#nav-cell-" + config.questionId);

  function formatTime(seconds) {
    if (seconds <= 0) return "00:00";
    return String(Math.floor(seconds / 60)).padStart(2, "0") + ":" + String(seconds % 60).padStart(2, "0");
  }

  function updateTimer() {
    const display = q("#timer-display");
    if (display) display.textContent = formatTime(remainingSeconds);
    const box = q("#timer-box");
    if (box) {
      box.classList.toggle("timer--warning", remainingSeconds <= 300 && remainingSeconds > 120);
      box.classList.toggle("timer--critical", remainingSeconds <= 120);
    }
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
  }

  function setStatus(text, className) {
    const element = saveStatus();
    if (element) { element.textContent = text; element.className = className; }
  }

  async function saveDraftNow() {
    const editor = writingEditor();
    if (!editor) return true;
    if (draftSaveTimeout) clearTimeout(draftSaveTimeout);
    const text = editor.value;
    setStatus("Saving draft…", "save-status");
    draftRequest = fetch(`/placement/take/${config.placementId}/draft`, {
      method: "POST",
      headers: { "Content-Type": "application/json", RequestVerificationToken: token },
      body: JSON.stringify({ questionId: config.questionId, responseText: text })
    }).then(async response => {
      if (!response.ok) throw new Error("Save draft failed with status " + response.status);
      draftDirty = false;
      setStatus("Draft saved", "save-status save-status--saved");
      if (text.trim()) {
        currentNavCell()?.classList.add("is-answered");
        currentNavCell()?.classList.remove("is-unanswered");
      }
      return true;
    }).catch(error => {
      console.error(error);
      setStatus("Save error", "save-status save-status--error");
      return false;
    }).finally(() => { draftRequest = null; });
    return draftRequest;
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

  async function navigate(href) {
    const target = new URL(href, window.location.origin);
    const targetQuestion = target.searchParams.get("q");
    if (!targetQuestion || draftDirty && !(await saveDraftNow())) return;
    const response = await fetch(`/placement/take/${config.placementId}/fragment?q=${encodeURIComponent(targetQuestion)}`, {
      headers: { Accept: "text/html" }
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      if (body.redirectUrl) window.location.href = body.redirectUrl;
      setStatus("Save error", "save-status save-status--error");
      return;
    }
    dynamic.innerHTML = await response.text();
    config = JSON.parse(dataIsland().textContent);
    remainingSeconds = config.remainingSeconds;
    isFlagged = config.isFlagged;
    answeredCount = config.answeredCount;
    draftDirty = false;
    updateTimer();
    updateWritingCounts();
    window.history.pushState({}, "", `/placement/take/${config.placementId}?q=${targetQuestion}`);
    document.title = `Placement Test - Question ${targetQuestion} - extra efficient`;
    window.initExamAudio?.();
    restorePlacementAudio();
  }

  document.addEventListener("click", event => {
    const link = event.target.closest("a[href^='/placement/take/']");
    if (link && link.closest("#placement-take-dynamic")) {
      event.preventDefault();
      navigate(link.href);
      return;
    }
    const flag = event.target.closest("#flag-btn");
    if (flag) {
      isFlagged = !isFlagged;
      flag.dataset.flagged = String(isFlagged);
      flag.setAttribute("aria-pressed", String(isFlagged));
      flag.textContent = isFlagged ? "Flagged for review" : "Flag for review";
      flag.classList.toggle("btn--primary", isFlagged);
      sendAnswer(q(".js-option-radio:checked")?.value || null, isFlagged);
      return;
    }
    const saveDraft = event.target.closest("#btn-save-writing-draft");
    if (saveDraft) saveDraftNow();
    const openModal = event.target.closest(".js-open-submit-modal");
    if (openModal) q("#submit-modal")?.style.setProperty("display", "flex");
    if (event.target.closest("#btn-cancel-submit")) q("#submit-modal")?.style.setProperty("display", "none");
  });

  document.addEventListener("input", event => {
    if (!event.target.matches("#writing-editor")) return;
    draftDirty = true;
    updateWritingCounts();
    setStatus("Draft not saved", "save-status");
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
    sendAnswer(event.target.value, isFlagged);
  });

  async function sendAnswer(selectedOptionId, flagged) {
    setStatus("Saving…", "save-status");
    const body = new URLSearchParams();
    if (selectedOptionId != null) body.append("selectedOptionId", selectedOptionId);
    body.append("isFlagged", flagged ? "true" : "false");
    body.append("__RequestVerificationToken", token);
    try {
      const response = await fetch(`/placement/take/${config.placementId}/answers/${config.questionId}`, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded", RequestVerificationToken: token },
        body: body.toString()
      });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error("Save answer failed with status " + response.status);
      setStatus("Saved", "save-status save-status--saved");
      if (typeof data.remainingSeconds === "number") { remainingSeconds = data.remainingSeconds; updateTimer(); }
      const cell = currentNavCell();
      if (data.selectedOptionId != null) { cell?.classList.add("is-answered"); cell?.classList.remove("is-unanswered"); }
      cell?.classList.toggle("is-marked", !!data.isFlagged);
    } catch (error) {
      console.error(error);
      setStatus("Save error", "save-status save-status--error");
    }
  }

  setInterval(async () => {
    remainingSeconds--;
    updateTimer();
    if (remainingSeconds <= 0) {
      if (draftDirty) await saveDraftNow();
      q("#is-auto-submit-input") && (q("#is-auto-submit-input").value = "true");
      q("#submit-form")?.submit();
    }
  }, 1000);

  q("#submit-form")?.addEventListener("submit", async event => {
    if (draftDirty) {
      event.preventDefault();
      if (await saveDraftNow()) event.target.submit();
    } else {
      try { localStorage.removeItem("ee_placement_audio_" + config.placementId); } catch (_) { }
    }
  });

  updateTimer();
  updateWritingCounts();
  restorePlacementAudio();
})();
