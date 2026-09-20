/**
 * Đổi state hiển thị của trang prototype.
 * Đọc ?state=<name>, gắn lên body[data-state], render thanh chọn state.
 * Không mô phỏng nghiệp vụ — chỉ bật/tắt class .only-<state>.
 */
(function () {
  "use strict";

  var body = document.body;
  var states = (body.dataset.states || "success")
    .split(",")
    .map(function (s) { return s.trim(); })
    .filter(Boolean);

  var requested = new URLSearchParams(location.search).get("state");
  var current = states.indexOf(requested) !== -1 ? requested : states[0];
  body.dataset.state = current;

  if (states.length < 2) return;

  var bar = document.createElement("nav");
  bar.className = "state-switcher";
  bar.setAttribute("aria-label", "Chon state de xem");

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
    }
    bar.appendChild(link);
  });

  body.appendChild(bar);
})();
