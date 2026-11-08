/**
 * #221 / D18: microphone permission has three states, and they are read from the browser,
 * never inferred from a failed getUserMedia.
 *
 * `granted`, `denied` and "never asked" have different remedies, so collapsing any two of them
 * sends the learner to a control that will not help. Two traps make the collapse tempting:
 *
 *   1. A rejected getUserMedia throws `NotAllowedError` both when the learner DENIED the prompt
 *      and when they DISMISSED it without answering. Chromium leaves the permission at `prompt`
 *      in the second case, so the error name cannot tell the two apart. Only the Permissions
 *      API can, which is why `ask()` re-queries after every rejection instead of trusting the
 *      error.
 *   2. `navigator.permissions.query({name:"microphone"})` is unsupported in Firefox before 128
 *      and in some Safari builds, where it throws a TypeError. An absent API is NOT "denied" -
 *      it is "this browser will not say without being asked", so the last answer this origin
 *      recorded is used, and "never asked" is the default.
 *
 * The durable record is a localStorage key on this origin. Not a cookie and not the server:
 * the permission belongs to one browser profile on one origin, and the server cannot observe
 * it. A server-side record would be a lie the first time the learner uses another machine.
 * It survives a page reload; it cannot survive the browser's own permission being revoked,
 * which is why every query re-reads the browser and overwrites the cache.
 */
(function () {
  "use strict";

  var STORAGE_KEY = "ee_microphone_preference_permission";
  var GRANTED = "granted";
  var DENIED = "denied";
  var NEVER_ASKED = "never-asked";

  function readCached() {
    try {
      var record = JSON.parse(window.localStorage.getItem(STORAGE_KEY) || "null");
      if (!record || typeof record.state !== "string") return null;
      return record.state === GRANTED || record.state === DENIED || record.state === NEVER_ASKED
        ? record.state
        : null;
    } catch (error) {
      // Private mode or a corrupted value: the browser is still the source of truth.
      return null;
    }
  }

  function writeCached(state) {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify({
        state: state,
        at: new Date().toISOString()
      }));
    } catch (error) {
      // No storage is not a failure: the state is still correct for this page load.
    }
  }

  /**
   * Reads the permission without ever prompting. `measurable` is false when the browser refuses
   * to expose the microphone permission name at all, which is a fact about the browser and not
   * about the learner's hardware.
   */
  async function read() {
    var permissions = navigator.permissions;
    if (!permissions || typeof permissions.query !== "function") {
      return { state: readCached() || NEVER_ASKED, measurable: false };
    }

    try {
      var status = await permissions.query({ name: "microphone" });
      var state = status.state === "granted"
        ? GRANTED
        : (status.state === "denied" ? DENIED : NEVER_ASKED);
      writeCached(state);
      return { state: state, measurable: true };
    } catch (error) {
      // TypeError for an unsupported permission name. Do not read this as `denied`.
      return { state: readCached() || NEVER_ASKED, measurable: false };
    }
  }

  /** The label §5 requires: the remedy has to name the browser, and the copy says so. */
  function browser() {
    var agent = navigator.userAgent || "";
    if (/Edg\//.test(agent)) return "edge";
    if (/OPR\//.test(agent)) return "opera";
    if (/Firefox\//.test(agent)) return "firefox";
    if (/Chrome\//.test(agent)) return "chrome";
    if (/Safari\//.test(agent)) return "safari";
    return "unknown";
  }

  /**
   * Asks the browser for the microphone and returns the state it ends in. This is the ONLY
   * function in the site that may prompt, and it is only reachable from a settings or
   * pre-sitting screen (see `apply`, which refuses when the surface is a sitting).
   */
  async function ask(constraints) {
    if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
      return { state: readCached() || NEVER_ASKED, measurable: false, stream: null, error: "unsupported" };
    }

    try {
      var stream = await navigator.mediaDevices.getUserMedia({ audio: constraints || true });
      writeCached(GRANTED);
      return { state: GRANTED, measurable: true, stream: stream, error: null };
    } catch (error) {
      // `NotAllowedError` covers both a refusal and a dismissal, so ask the browser which.
      var after = await read();
      return {
        state: after.state === GRANTED ? DENIED : after.state,
        measurable: after.measurable,
        stream: null,
        error: error && error.name ? error.name : "unknown"
      };
    }
  }

  /**
   * Takes a microphone WITHOUT ever prompting. This is the only entry point a sitting surface may
   * use. `ask` above can raise a browser dialog and is reserved for settings and pre-sitting
   * screens; calling it from a take page would put a system dialog over a running clock, which is
   * exactly the interruption D18 forbids.
   *
   * A granted permission makes getUserMedia silent, so the call below cannot prompt. When the
   * permission is denied or has never been asked, this returns a blocked result and never calls
   * getUserMedia at all - so there is nothing to prompt with.
   */
  async function acquire(constraints) {
    var current = await read();
    if (current.state !== GRANTED) {
      return { ok: false, state: current.state, measurable: current.measurable, stream: null, reason: "permission" };
    }

    if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
      return { ok: false, state: current.state, measurable: false, stream: null, reason: "unsupported" };
    }

    try {
      var stream = await navigator.mediaDevices.getUserMedia({ audio: constraints || true });
      return { ok: true, state: GRANTED, measurable: true, stream: stream, reason: null };
    } catch (error) {
      // Permission was revoked between the query and the call: re-read so the screen can say so.
      var after = await read();
      return {
        ok: false,
        state: after.state,
        measurable: after.measurable,
        stream: null,
        reason: error && error.name ? error.name : "unknown"
      };
    }
  }

  function setBlock(component, visibleState) {
    component.querySelectorAll("[data-permission-state]").forEach(function (block) {
      block.hidden = block.getAttribute("data-permission-state") !== visibleState;
    });
    component.setAttribute("data-permission-current", visibleState);
  }

  function setBrowser(component) {
    var name = browser();
    component.querySelectorAll("[data-denied-browser]").forEach(function (block) {
      block.hidden = block.getAttribute("data-denied-browser") !== name;
    });
    return name;
  }

  /**
   * Renders a state onto a component. `canPrompt` is false on a sitting surface, where the
   * browser must never raise a dialog over a running clock: the state is still shown, and the
   * remedy names the screen where it can be fixed.
   */
  function apply(component, state, canPrompt) {
    if (!component) return;
    setBrowser(component);
    var shown = state === GRANTED || state === DENIED ? state : NEVER_ASKED;
    setBlock(component, shown);
    component.setAttribute("data-permission-promptable", canPrompt ? "true" : "false");
  }

  window.EeAudioGate = {
    GRANTED: GRANTED,
    DENIED: DENIED,
    NEVER_ASKED: NEVER_ASKED,
    read: read,
    ask: ask,
    acquire: acquire,
    apply: apply,
    browser: browser,
    readCached: readCached,
    storageKey: STORAGE_KEY
  };
})();
