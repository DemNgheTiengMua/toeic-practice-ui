/**
 * Painted-sign icon set + progressive enhancement.
 *
 * Every icon is drawn on a 24px grid in one stroke (square caps, mitred joins),
 * the same construction as the Menu / Close / chevron marks in sidebar.js. An
 * icon never stands alone: it is added beside a word that already carries the
 * meaning, so removing this script loses decoration, not information.
 *
 * Markup stays plain. This file decorates it by role (nav href, stamp word,
 * button label, badge text, alert variant) so 50 pages share one mapping.
 */
(function () {
  "use strict";

  var ICONS = {
    "home":         ["M3 11L12 3l9 8", "M5 10v11h14V10", "M10 21v-6h4v6"],
    "test":         ["M5 3h14v18H5z", "M9 8h6", "M9 12h6", "M9 16h3"],
    "archive":      ["M3 4h18v5H3z", "M5 9v11h14V9", "M10 13h4"],
    "grid":         ["M4 4h6v6H4z", "M14 4h6v6h-6z", "M4 14h6v6H4z", "M14 14h6v6h-6z"],
    "chart":        ["M4 3v17h17", "M8 16v-5", "M12 16V8", "M16 16v-3"],
    "route":        ["M12 3v18", "M12 5h8l2 3-2 3h-8", "M12 13H5l-3 3 3 3h7"],
    "chat":         ["M3 4h18v12H10l-4 4v-4H3z"],
    "wallet":       ["M3 7h18v13H3z", "M3 7V4h15v3", "M15 12h6v4h-6z"],
    "user":         ["M9 3h6v8H9z", "M4 21v-5h16v5"],
    "receipt":      ["M5 3h14v18l-3.5-2-3.5 2-3.5-2L5 21z", "M9 8h6", "M9 12h6"],
    "flag":         ["M5 21V3", "M5 4h14l-3 4.5 3 4.5H5"],
    "swap":         ["M4 8h14", "M14 4l4 4-4 4", "M20 16H6", "M10 12l-4 4 4 4"],
    "box":          ["M3 7l9-4 9 4v10l-9 4-9-4z", "M3 7l9 4 9-4", "M12 11v10"],
    "play":         ["M7 4l13 8-13 8z"],
    "stop":         ["M6 6h12v12H6z"],
    "mic":          ["M9 3h6v11H9z", "M5 11v3l3 3h8l3-3v-3", "M12 17v4", "M8 21h8"],
    "check":        ["M4 12l5 5L20 6"],
    "x":            ["M5 5l14 14", "M19 5L5 19"],
    "info":         ["M3 3h18v18H3z", "M12 11v6", "M12 7v1"],
    "check-sq":     ["M3 3h18v18H3z", "M7 12l3.5 3.5L17 9"],
    "x-sq":         ["M3 3h18v18H3z", "M9 9l6 6", "M15 9l-6 6"],
    "alert":        ["M12 3l10 18H2z", "M12 10v5", "M12 18v1"],
    "lock":         ["M5 11h14v10H5z", "M8 11V6l2-2h4l2 2v5"],
    "unlock":       ["M5 11h14v10H5z", "M8 11V6l2-2h4l2 2"],
    "arrow-right":  ["M4 12h16", "M14 6l6 6-6 6"],
    "arrow-left":   ["M20 12H4", "M10 6l-6 6 6 6"],
    "chevron-down": ["M6 9l6 6 6-6"],
    "refresh":      ["M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8", "M21 3v5h-5"],
    "spinner":      ["M21 12a9 9 0 1 1-9-9"],
    "clock":        ["M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18z", "M12 7v5l3 2"],
    "eye":          ["M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z", "M12 9a3 3 0 1 1 0 6 3 3 0 0 1 0-6z"],
    "filter":       ["M3 4h18l-7 9v7l-4-2v-5z"],
    "search":       ["M10 3a7 7 0 1 1 0 14 7 7 0 0 1 0-14z", "M15 15l6 6"],
    "upload":       ["M12 16V4", "M6 10l6-6 6 6", "M4 20h16"],
    "plus":         ["M12 4v16", "M4 12h16"],
    "save":         ["M4 4h13l3 3v13H4z", "M8 4v5h8V4", "M8 20v-6h8v6"],
    "logout":       ["M10 4H4v16h6", "M16 8l5 4-5 4", "M21 12H9"],
    "bookmark":     ["M6 3h12v18l-6-4-6 4z"],
    "pencil":       ["M4 20l1-5L16 4l4 4L9 19z", "M14 6l4 4"],
    "inbox":        ["M3 13l3-9h12l3 9v7H3z", "M3 13h5l1 3h6l1-3h5"],
    "mail":         ["M3 5h18v14H3z", "M3 5l9 8 9-8"],
    "calendar":     ["M3 5h18v16H3z", "M3 10h18", "M8 3v4", "M16 3v4"],
    "video":        ["M3 6h12v12H3z", "M15 10l6-3v10l-6-3"],
    "target":       ["M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18z", "M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8z"]
  };

  var NS = "http://www.w3.org/2000/svg";

  function svg(name) {
    var paths = ICONS[name];
    if (!paths) return null;
    var el = document.createElementNS(NS, "svg");
    el.setAttribute("viewBox", "0 0 24 24");
    el.setAttribute("fill", "none");
    el.setAttribute("stroke", "currentColor");
    el.setAttribute("stroke-width", "2.5");
    el.setAttribute("stroke-linecap", "square");
    el.setAttribute("stroke-linejoin", "miter");
    el.setAttribute("aria-hidden", "true");
    el.setAttribute("focusable", "false");
    el.setAttribute("class", "icon icon--" + name);
    paths.forEach(function (d) {
      var p = document.createElementNS(NS, "path");
      p.setAttribute("d", d);
      el.appendChild(p);
    });
    return el;
  }

  function make(name, extra) {
    var el = svg(name);
    if (el && extra) el.setAttribute("class", el.getAttribute("class") + " " + extra);
    return el;
  }
  function has(host) {
    return host.querySelector(":scope > .icon, :scope > [data-icon]");
  }
  function lead(host, name, extra) {
    if (!host || has(host)) return;
    var el = make(name, "icon--lead" + (extra ? " " + extra : ""));
    if (el) host.insertBefore(el, host.firstChild);
  }
  function trail(host, name) {
    if (!host || has(host)) return;
    var el = make(name, "icon--trail");
    if (el) host.appendChild(el);
  }
  function each(sel, fn) {
    Array.prototype.forEach.call(document.querySelectorAll(sel), fn);
  }
  function text(el) { return el.textContent.replace(/\s+/g, " ").trim(); }
  function first(rules, label) {
    for (var i = 0; i < rules.length; i++) {
      if (rules[i][0].test(label)) return rules[i];
    }
    return null;
  }

  /* ---- sidebar: one mark per destination, keyed by the label ---------------- */
  var NAV = {
    "Home": "home", "Dashboard": "grid", "Test list": "test", "Tests": "test", "Exams": "test",
    "Exam history": "archive", "Practice by section": "pencil",
    "Weakness analysis": "chart", "My learning path": "route",
    "Find a mentor": "chat", "Credit wallet": "wallet", "Profile": "user",
    "Students": "user", "Score conversion": "swap", "Credit packages": "box",
    "Orders": "receipt", "Complaints": "flag", "Support reports": "flag", "Prototype catalog": "grid",
    "Saved questions": "bookmark", "Placement test": "target"
  };
  each(".nav-list__item", function (a) { lead(a, NAV[text(a)]); });

  /* ---- state stamps: the word stays, the mark sits beside it ---------------- */
  var STAMP = {
    "ERROR": "alert", "EMPTY": "inbox", "RUNNING": "spinner", "TIME'S UP": "clock",
    "LOCKED": "lock", "DONE": "check", "UPLOADING": "upload", "SENT": "mail",
    "PREPARE": "mic", "PENDING": "clock", "FAILED": "x-sq", "RECORDED": "check",
    "RECORDING": "mic", "NOT SAVED": "alert",
    "WRITING": "pencil", "OFFLINE": "alert", "SAVED": "check", "HINT": "info"
  };
  each(".state-block__icon", function (s) { lead(s, STAMP[text(s)]); });

  /* ---- buttons: keyed by the action the label names ------------------------ */
  var BTN = [
    [/…$/, "spinner", "lead", "icon--spin"],
    [/^(Play|Replay)\b|^Play /, "play", "lead"],
    [/^(Try again|Reload|Retry|Check again|Reconcile|Record again|Practise|Retake|Try paying)/, "refresh", "lead"],
    [/^(Edit|Change photo)/, "pencil", "lead"],
    [/^Sign out|^Exit\b|^Cancel exam/, "logout", "lead"],
    [/^(Unlock)/, "unlock", "lead"],
    [/^(Lock|Locked)\b/, "lock", "lead"],
    [/^Filter/, "filter", "lead"],
    [/^(Create (new|a new)|Generate)/, "plus", "lead"],
    [/^(Save this question)/, "bookmark", "lead"],
    [/^(Show hint|Hide hint)/, "info", "lead"],
    [/^Save/, "save", "lead"],
    [/^(Choose file|Choose from device|Replace file|Retry upload)/, "upload", "lead"],
    [/^(Resend|Send link)/, "mail", "lead"],
    [/^Join meeting/, "video", "lead"],
    [/^(Book|Confirm booking)/, "calendar", "lead"],
    [/^(Buy|Upgrade|Pay|Open credit wallet|View credit wallet)/, "wallet", "lead"],
    [/^Stop recording/, "stop", "lead"],
    [/^(Start recording|Record test clip|Check my microphone|Start speaking)/, "mic", "lead"],
    [/^(Flag for review|Remove flag)/, "flag", "lead"],
    [/^(Review|Details|View|Open order|Open question)/, "eye", "lead"],
    [/^(Submit|Finish|Publish|Check answers|Confirm|Resolve)/, "check", "lead"],
    [/^Reset/, "refresh", "lead"],
    [/^Create account/, "user", "lead"],
    [/^Sign in/, "arrow-right", "trail"],
    [/^Change password/, "lock", "lead"],
    [/^Credit wallet/, "wallet", "lead"],
    [/^Exam history/, "archive", "lead"],
    [/^Weakness analysis/, "chart", "lead"],
    [/^Leave a review/, "pencil", "lead"],
    [/^Find another mentor/, "chat", "lead"],
    [/^Open test list/, "test", "lead"],
    [/^Enter next/, "arrow-right", "trail"],
    [/^(Start|Continue|Take|Practice|Go to|Choose this|Choose another|Set your|Build)/, "arrow-right", "trail"],
    [/^(Back|Go back)/, "arrow-left", "lead"],
    [/^Nguyễn/, "user", "lead"],
    [/^Prototype catalog/, "grid", "lead"]
  ];
  each(".btn", function (b) {
    var label = text(b);
    if (!label || b.querySelector(".icon")) return;
    /* A typed arrow is a glyph pretending to be an icon: swap it for a drawn one. */
    var typedTrail = /\s*→\s*$/.test(label), typedLead = /^\s*←\s*/.test(label);
    /* "Start for free <span aria-hidden>→</span>": the sweep below redraws the span. */
    if (b.querySelector("[aria-hidden='true']")) return;
    if (typedTrail || typedLead) {
      Array.prototype.forEach.call(b.childNodes, function (n) {
        if (n.nodeType === 3) n.nodeValue = n.nodeValue.replace(/\s*→\s*$/, "").replace(/^(\s*)←\s*/, "$1");
      });
      if (typedTrail) trail(b, "arrow-right"); else lead(b, "arrow-left");
      return;
    }
    var rule = first(BTN, label);
    if (!rule) return;
    if (rule[2] === "trail") trail(b, rule[1]); else lead(b, rule[1], rule[3]);
  });

  /* Hand-typed arrows that live outside a button (landing cards, hero link). */
  each("[aria-hidden='true']", function (s) {
    if (s.children.length || !/^[→▼]$/.test(text(s))) return;
    var el = make(text(s) === "▼" ? "chevron-down" : "arrow-right", "icon--trail");
    if (el) { s.textContent = ""; s.appendChild(el); }
  });
  each(".cert-switcher__arrow, .landing-preview__arrow", function (s) {
    if (s.querySelector(".icon")) return;
    var el = make(s.className.indexOf("cert") !== -1 ? "chevron-down" : "arrow-right", "icon--trail");
    if (el) { s.textContent = ""; s.appendChild(el); }
  });

  /* ---- badges: only the ones that state a verdict or a status --------------- */
  var BADGE = [
    [/^(Resolved|Complete|Published|Paid|On sale|Active|Confirmed|Done|Completed|Correct|Your answer · correct|\+\d)/, "check"],
    [/^(Failed|Cancelled|Incorrect|Your answer is incorrect|\d+ incorrect)/, "x"],
    [/^Locked/, "lock"],
    [/^(Pending|In progress)/, "clock"],
    [/^Draft/, "pencil"],
    [/^Reconciling/, "refresh"],
    [/^Flagged/, "flag"],
    [/^PLAYING/, "play"]
  ];
  each(".badge", function (b) {
    if (b.querySelector(".icon")) return;
    var r = first(BADGE, text(b));
    if (r) lead(b, r[1]);
  });

  /* ---- alerts: the variant picks the mark ----------------------------------- */
  var ALERT = { info: "info", success: "check-sq", warning: "alert", danger: "x-sq" };
  each(".alert", function (a) {
    var m = /alert--(info|success|warning|danger)/.exec(a.className);
    if (!m || a.querySelector(":scope > .icon")) return;
    var el = make(ALERT[m[1]], "icon--alert");
    if (!el) return;
    /* The icon floats; the body is its own formatting context so wrapped lines
       stay beside the mark instead of running back under it. */
    var body = document.createElement("div");
    body.className = "alert__body";
    while (a.firstChild) body.appendChild(a.firstChild);
    a.appendChild(el);
    a.appendChild(body);
  });

  /* ---- disclosure rows: the browser's triangle is a glyph, so draw our own ---- */
  each("details > summary", function (sm) {
    if (sm.querySelector(":scope > .icon")) return;
    var el = make("chevron-down", "icon--disclosure");
    if (el) sm.insertBefore(el, sm.firstChild);
  });

  /* ---- clock, save status, stepper ------------------------------------------ */
  each(".timer", function (t) { lead(t, "clock"); });
  each(".save-status", function (s) {
    if (/--saving/.test(s.className)) lead(s, "spinner", "icon--spin");
    else if (/--saved/.test(s.className)) lead(s, "check", "icon--draw");
    else if (/--error/.test(s.className)) lead(s, "alert");
  });
  each(".stepper__item.is-done", function (s) { lead(s, "check"); });

  /* ---- landing tags ---------------------------------------------------------- */
  var TAG = { "FREE": "check", "PERSONALIZED": "target", "TRACKED": "chart" };
  each(".landing-step__tag", function (t) { lead(t, TAG[text(t)]); });
  each(".landing-preview__footer-status", function (t) { lead(t, "clock"); });

  /* ---- pricing: a mark per included line ------------------------------------ */
  each(".price-card__list li", function (li) { lead(li, "check", "icon--bullet"); });

  /* ---- search field and upload zone ----------------------------------------- */
  each("input.field__input[type='search']", function (input) {
    if (input.parentNode.classList.contains("field__control")) return;
    var wrap = document.createElement("span");
    wrap.className = "field__control";
    input.parentNode.insertBefore(wrap, input);
    wrap.appendChild(input);
    var el = make("search", "icon--field");
    if (el) wrap.insertBefore(el, input);
  });
  each(".upload__zone", function (z) {
    if (z.querySelector(":scope > .icon")) return;
    var el = make("upload", "icon--zone");
    if (el) z.insertBefore(el, z.firstChild);
  });
})();
