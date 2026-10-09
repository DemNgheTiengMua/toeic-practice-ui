/**
 * Sidebar behaviour for the signed-in shells (student, admin, payment).
 *
 *  - Desktop: a Menu button on the band hides and restores the whole rail.
 *  - Tablet and phone: the rail becomes a drawer over a scrim. While it is open
 *    the page behind is scroll-locked and inert, Escape closes it, Tab stays in it,
 *    and focus returns to the button that opened it.
 *  - Every nav group folds. Fold state is remembered; a group that holds the
 *    current page is always open on load so "you are here" is never hidden.
 *  - The rail scrolls on its own and does not chain scroll into the page.
 *
 * Progressive enhancement: with no script the rail is the plain stacked list.
 * Loaded from <head> so the remembered layout is applied before first paint.
 */
(function () {
  "use strict";

  var KEY_RAIL = "ee.sidebar";
  var KEY_GROUPS = "ee.navgroups";
  var html = document.documentElement;
  var narrow = window.matchMedia("(max-width: 1024px)");

  function read(key) {
    try { return window.localStorage.getItem(key); } catch (e) { return null; }
  }
  function write(key, value) {
    try { window.localStorage.setItem(key, value); } catch (e) { /* private mode: not remembered */ }
  }

  /* ---- head phase: restore layout before anything paints ------------------ */
  html.classList.add("has-sidebar-js", "sidebar-booting");
  if (read(KEY_RAIL) === "collapsed") html.classList.add("sidebar-collapsed");

  function ready(fn) {
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", fn);
    else fn();
  }

  var SVG_NS = "http://www.w3.org/2000/svg";
  function icon(paths, size) {
    var svg = document.createElementNS(SVG_NS, "svg");
    svg.setAttribute("width", size || 18);
    svg.setAttribute("height", size || 18);
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("fill", "none");
    svg.setAttribute("stroke", "currentColor");
    svg.setAttribute("stroke-width", "2.5");
    svg.setAttribute("stroke-linecap", "square");
    svg.setAttribute("aria-hidden", "true");
    svg.setAttribute("focusable", "false");
    paths.forEach(function (d) {
      var p = document.createElementNS(SVG_NS, "path");
      p.setAttribute("d", d);
      svg.appendChild(p);
    });
    return svg;
  }
  var ICON_MENU = ["M3 6h18", "M3 12h18", "M3 18h18"];
  var ICON_CLOSE = ["M5 5l14 14", "M19 5L5 19"];
  var ICON_CHEVRON = ["M6 9l6 6 6-6"];

  ready(function () {
    var sidebar = document.querySelector(".app-shell--with-sidebar .app-sidebar");
    var topbar = document.querySelector(".app-shell--with-sidebar > .app-topbar");
    var nav = sidebar && sidebar.querySelector(".nav-list");
    if (!sidebar || !topbar || !nav) {
      html.classList.remove("has-sidebar-js", "sidebar-booting", "sidebar-collapsed");
      return;
    }

    sidebar.id = sidebar.id || "app-sidebar";
    var main = document.querySelector(".app-main");
    var drawerOpen = false;
    var lastFocus = null;

    /* ---- topbar height, so the rail and context bar stick just below it ---- */
    function measureTopbar() {
      html.style.setProperty("--topbar-h", topbar.offsetHeight + "px");
    }
    measureTopbar();
    if (window.ResizeObserver) new ResizeObserver(measureTopbar).observe(topbar);
    else window.addEventListener("resize", measureTopbar);

    /* ---- the Menu button on the band ---------------------------------------- */
    var lead = document.createElement("div");
    lead.className = "app-topbar__lead";
    var toggle = document.createElement("button");
    toggle.type = "button";
    toggle.className = "sidebar-toggle";
    toggle.setAttribute("aria-controls", sidebar.id);
    toggle.setAttribute("aria-keyshortcuts", "[");
    toggle.appendChild(icon(ICON_MENU));
    var toggleLabel = document.createElement("span");
    toggleLabel.textContent = "Menu";
    toggle.appendChild(toggleLabel);
    lead.appendChild(toggle);
    var brand = topbar.firstElementChild;
    topbar.insertBefore(lead, brand);
    if (brand) lead.appendChild(brand);

    /* ---- the drawer's own head and scrim (shown at <=1024px only) ----------- */
    var head = document.createElement("div");
    head.className = "app-sidebar__head";
    var headTitle = document.createElement("span");
    headTitle.textContent = "Menu";
    var closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "sidebar-close";
    closeBtn.setAttribute("aria-label", "Close menu");
    closeBtn.appendChild(icon(ICON_CLOSE, 16));
    head.appendChild(headTitle);
    head.appendChild(closeBtn);
    sidebar.insertBefore(head, sidebar.firstChild);

    var scrim = document.createElement("div");
    scrim.className = "sidebar-scrim";
    scrim.setAttribute("aria-hidden", "true");
    document.body.appendChild(scrim);

    /* ---- open / close -------------------------------------------------------- */
    function syncToggle() {
      var expanded = narrow.matches ? drawerOpen : !html.classList.contains("sidebar-collapsed");
      toggle.setAttribute("aria-expanded", String(expanded));
      toggle.title = narrow.matches ? "Open menu" : (expanded ? "Hide menu  [" : "Show menu  [");
    }

    function setInert(on) {
      [main, topbar].forEach(function (el) {
        if (!el) return;
        if (on) el.setAttribute("inert", "");
        else el.removeAttribute("inert");
      });
    }

    function openDrawer() {
      if (drawerOpen) return;
      drawerOpen = true;
      lastFocus = document.activeElement;
      html.classList.add("sidebar-drawer-open");
      /* The toggle sits in the topbar, which goes inert; the close button is the
         way out, so focus moves straight to it. */
      setInert(true);
      syncToggle();
      closeBtn.focus();
    }

    function closeDrawer(returnFocus) {
      if (!drawerOpen) return;
      drawerOpen = false;
      html.classList.remove("sidebar-drawer-open");
      setInert(false);
      syncToggle();
      if (returnFocus !== false) (lastFocus && lastFocus.focus ? lastFocus : toggle).focus();
    }

    function setRailCollapsed(collapsed) {
      html.classList.toggle("sidebar-collapsed", collapsed);
      write(KEY_RAIL, collapsed ? "collapsed" : "open");
      syncToggle();
    }

    toggle.addEventListener("click", function () {
      if (narrow.matches) openDrawer();
      else setRailCollapsed(!html.classList.contains("sidebar-collapsed"));
    });
    closeBtn.addEventListener("click", function () { closeDrawer(); });
    scrim.addEventListener("click", function () { closeDrawer(); });

    /* Following a link closes the drawer so the next page is not opened behind it. */
    nav.addEventListener("click", function (e) {
      if (drawerOpen && e.target.closest && e.target.closest("a[href]")) closeDrawer(false);
    });

    document.addEventListener("keydown", function (e) {
      if (drawerOpen) {
        if (e.key === "Escape") { e.preventDefault(); closeDrawer(); return; }
        if (e.key === "Tab") {
          var items = Array.prototype.filter.call(
            sidebar.querySelectorAll("a[href], button:not([disabled])"),
            function (el) { return el.offsetParent !== null; }
          );
          if (!items.length) return;
          var first = items[0], last = items[items.length - 1];
          if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
          else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
        }
        return;
      }
      /* "[" toggles the rail from anywhere that is not a text field. */
      if (e.key === "[" && !e.ctrlKey && !e.metaKey && !e.altKey) {
        var t = e.target;
        var typing = t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName));
        if (typing) return;
        e.preventDefault();
        toggle.click();
      }
    });

    /* Crossing the breakpoint with the drawer open would strand the scroll lock. */
    function onBreakpoint() {
      if (!narrow.matches && drawerOpen) closeDrawer(false);
      syncToggle();
    }
    if (narrow.addEventListener) narrow.addEventListener("change", onBreakpoint);
    else narrow.addListener(onBreakpoint);

    /* ---- foldable groups ------------------------------------------------------ */
    var saved = {};
    try { saved = JSON.parse(read(KEY_GROUPS) || "{}") || {}; } catch (e) { saved = {}; }

    function persistGroups() { write(KEY_GROUPS, JSON.stringify(saved)); }

    var groups = Array.prototype.slice.call(nav.querySelectorAll(":scope > .nav-list__group"));
    groups.forEach(function (groupEl, i) {
      var label = groupEl.textContent.trim();
      var members = [];
      var node = groupEl.nextElementSibling;
      while (node && !node.classList.contains("nav-list__group")) {
        members.push(node);
        node = node.nextElementSibling;
      }
      if (!members.length) return;

      var section = document.createElement("div");
      section.className = "nav-list__section";
      section.id = sidebar.id + "-group-" + i;
      var inner = document.createElement("div");
      inner.className = "nav-list__section-inner";
      members.forEach(function (m) { inner.appendChild(m); });
      section.appendChild(inner);
      groupEl.parentNode.insertBefore(section, groupEl.nextSibling);

      var hasCurrent = !!inner.querySelector(".is-active, [aria-current]");
      var folded = saved[label] === true && !hasCurrent;

      var btn = document.createElement("button");
      btn.type = "button";
      btn.className = groupEl.className + (hasCurrent ? " has-current" : "");
      btn.setAttribute("aria-controls", section.id);

      var text = document.createElement("span");
      text.textContent = label;
      var count = document.createElement("span");
      count.className = "nav-list__count";
      count.textContent = String(members.filter(function (m) { return m.matches("a"); }).length);
      count.setAttribute("aria-hidden", "true");
      var chev = icon(ICON_CHEVRON, 14);
      chev.setAttribute("class", "nav-list__chevron");
      btn.appendChild(text);
      btn.appendChild(count);
      btn.appendChild(chev);
      groupEl.parentNode.replaceChild(btn, groupEl);

      function setFolded(value, remember) {
        section.classList.toggle("is-folded", value);
        btn.setAttribute("aria-expanded", String(!value));
        if (remember) {
          if (value) saved[label] = true; else delete saved[label];
          persistGroups();
        }
      }
      setFolded(folded, false);
      btn.addEventListener("click", function () {
        setFolded(!section.classList.contains("is-folded"), true);
      });
    });

    /* ---- make sure the current page is in view inside the rail ---------------- */
    var current = nav.querySelector(".is-active");
    if (current) {
      var top = current.offsetTop - sidebar.clientHeight / 2 + current.offsetHeight / 2;
      if (top > 0) sidebar.scrollTop = top;
    }

    syncToggle();
    /* Transitions resume once the restored layout has painted. */
    window.requestAnimationFrame(function () {
      window.requestAnimationFrame(function () { html.classList.remove("sidebar-booting"); });
    });
  });
})();
