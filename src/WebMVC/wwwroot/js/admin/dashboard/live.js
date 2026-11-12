(function () {
  "use strict";

  var region = document.querySelector("[data-dashboard-metrics]");
  if (!region) return;

  var indicator = document.querySelector("[data-dashboard-live-state]");
  var streamUrl = region.dataset.streamUrl;
  var fragmentUrl = region.dataset.fragmentUrl;

  // One fetch at a time. A heartbeat and a change can arrive a tick apart, and two overlapping fragment
  // requests could land out of order - the older one winning and painting a figure the newer already fixed.
  var inFlight = false;
  var queued = false;

  function setState(text, modifier) {
    if (!indicator) return;
    indicator.textContent = text;
    indicator.className = "badge " + modifier;
  }

  async function refresh() {
    if (inFlight) {
      queued = true;
      return;
    }

    inFlight = true;
    try {
      var response = await fetch(fragmentUrl, {
        credentials: "same-origin",
        headers: { "Accept": "text/html" },
        cache: "no-store"
      });
      if (!response.ok) throw new Error("Fragment request failed.");

      // Replace only the region's contents. Assigning innerHTML rather than navigating keeps scroll
      // position, focus and any half-filled form, and replays no reveal animation - the animations in this
      // system run once on a fresh element, and the region element itself is never recreated.
      region.innerHTML = await response.text();
    } catch (error) {
      // The stream may still be up; a single failed fragment read is not a dropped connection, so the
      // indicator is left to the stream's own handlers rather than being painted as dead here.
      queued = true;
    } finally {
      inFlight = false;
      if (queued) {
        queued = false;
        // Deferred rather than recursive: the retry runs after this call has cleared its own flag.
        window.setTimeout(refresh, 0);
      }
    }
  }

  if (!window.EventSource) {
    // No stream: the numbers are still correct as rendered, they simply stop moving. Saying so is the
    // honest state - a badge reading "live" over a page that cannot update is the confusion to avoid.
    setState("Live updates unavailable", "badge--muted");
    return;
  }

  // EventSource reconnects on its own with a backoff, so there is no retry loop here. Close it on unload
  // so a navigating admin does not leave the server streaming to a page that no longer exists.
  var stream = new EventSource(streamUrl);

  stream.addEventListener("open", function () {
    setState("Live", "badge--success");
    // A reconnect may have missed changes; the first event after it is not guaranteed to be a change, so
    // read once on open. Harmless on the initial connect: it renders the same figures the page already has.
    refresh();
  });

  stream.addEventListener("revision", refresh);
  stream.addEventListener("heartbeat", refresh);

  // EventSource fires error on a drop and again on every failed retry. The badge must say "stale" rather
  // than keep claiming live, because old numbers looking current is the failure this ticket exists to stop.
  stream.addEventListener("error", function () {
    setState("Reconnecting…", "badge--warning");
  });

  window.addEventListener("pagehide", function () {
    stream.close();
  });
})();
