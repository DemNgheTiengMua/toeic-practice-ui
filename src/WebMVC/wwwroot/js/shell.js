/**
 * #242 / SCR-SH-01: the two shell conditions a browser, not a page, knows about.
 *
 *  - Offline (U16): outside a sitting, offline is a banner and nothing else - no answer buffering.
 *    The banner is in the markup on every page; this file only raises `data-offline` on <html>,
 *    which is the single attribute the stylesheet keys the visible state off. A page that forgot to
 *    include this script loses the announcement, not the element.
 *
 *  - The countdown's own wording is decided on the server (the layout), because "3 days to exam" is
 *    a sentence and a locale, and a shell that re-derives it in script would drift from the goal
 *    block on the dashboard. Nothing here touches it.
 *
 * No dependency on any other script; loaded from the layout after icons.js.
 */
(function () {
  "use strict";

  var html = document.documentElement;

  function sync() {
    /* The server can state the condition explicitly (`?offline=1`), which is what makes the banner
       assertable in a rendered page and reviewable on a working laptop. That statement wins: the
       browser does not clear a condition it was told about. */
    if (html.getAttribute("data-offline-forced") === "true") {
      html.setAttribute("data-offline", "true");
      return;
    }

    /* navigator.onLine is false only when the browser is sure there is no network. The banner
       therefore errs towards silence, which is right: a false "you are offline" over a working
       page teaches the learner to ignore the one that is true. */
    var offline = navigator.onLine === false;
    if (offline) html.setAttribute("data-offline", "true");
    else html.removeAttribute("data-offline");
  }

  window.addEventListener("online", sync);
  window.addEventListener("offline", sync);
  sync();
})();
