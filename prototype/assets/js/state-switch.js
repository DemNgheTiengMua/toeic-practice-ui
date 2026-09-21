/**
 * Switches the prototype page's visible state.
 * Reads ?state=<name>, sets body[data-state], and renders the state switcher.
 * No business logic is simulated — this only toggles the .only-<state> class.
 */
(function () {
  "use strict";

  var body = document.body;
  var states = (body.dataset.states || "success")
    .split(",")
    .map(function (s) { return s.trim(); })
    .filter(Boolean);

  var requested = new URLSearchParams(location.search).get("state");

  /* An unrecognised ?state= value is a bug, not a default. Silently falling back
     made a stale or mistyped deep link render a plausible-looking wrong state —
     the reviewer sees a real screen and has no reason to doubt it. Say so
     instead, in the console and on the page. */
  var unknown = requested && states.indexOf(requested) === -1;
  if (unknown) {
    console.error(
      "[state-switch] unknown state %o — this page declares %o",
      requested, states
    );
  }

  var current = states.indexOf(requested) !== -1 ? requested : states[0];
  body.dataset.state = current;

  if (states.length < 2) return;

  var bar = document.createElement("nav");
  bar.className = "state-switcher";
  bar.setAttribute("aria-label", "Choose a state to view");

  var label = document.createElement("span");
  label.className = "state-switcher__label";
  label.textContent = "State:";
  bar.appendChild(label);

  states.forEach(function (name) {
    var link = document.createElement("a");
    var url = new URL(location.href);
    url.searchParams.set("state", name);
    link.href = url.pathname + url.search;
    link.textContent = name;
    link.className = "state-switcher__item";
    if (name === current) {
      link.setAttribute("aria-current", "true");
      if (unknown) {
        link.className += " state-switcher__fallback";
        link.title = "fell back from " + requested;
      }
    }
    bar.appendChild(link);
  });

  if (unknown) {
    bar.className += " state-switcher--unknown";
    var warn = document.createElement("span");
    warn.className = "state-switcher__warn";
    warn.textContent = "no state “" + requested +
      "” — showing the first declared state";
    bar.appendChild(warn);
  }

  body.appendChild(bar);
})();
