// mobile menu
(function () {
  var toggle = document.getElementById('navToggle');
  var links = document.getElementById('navLinks');
  if (toggle && links) {
    toggle.addEventListener('click', function () { links.classList.toggle('open'); });
    links.addEventListener('click', function (e) { if (e.target.tagName === 'A') links.classList.remove('open'); });
  }
})();

// active nav state per page
(function () {
  var page = document.body.getAttribute('data-page');
  if (!page) return;
  var nodes = document.querySelectorAll('.nav-links a[data-page="' + page + '"]');
  nodes.forEach(function (a) { a.classList.add('active'); });
})();

// reveal on scroll — degrade gracefully
try {
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } });
    }, { threshold: 0.12 });
    document.querySelectorAll('.reveal').forEach(function (el) { io.observe(el); });
  } else {
    document.querySelectorAll('.reveal').forEach(function (el) { el.classList.add('in'); });
  }
} catch (e) {
  document.querySelectorAll('.reveal').forEach(function (el) { el.classList.add('in'); });
}

// 深色 / 浅色 界面对比滑块（指针拖动 + 键盘可操作，符合 WCAG：role=slider / aria-valuenow / 方向键）
(function () {
  var el = document.querySelector('.cmp[data-cmp]');
  if (!el) return;
  var handle = el.querySelector('.cmp__handle');
  var dark = el.querySelector('.cmp__dark');
  var min = 0, max = 100, val = 50, dragging = false;

  function setVal(v, doFocus) {
    v = Math.max(min, Math.min(max, v));
    val = Math.round(v);
    dark.style.clipPath = 'inset(0 ' + (100 - val) + '% 0 0)';
    handle.style.left = val + '%';
    el.setAttribute('aria-valuenow', String(val));
    if (doFocus) el.focus();
  }
  function fromClientX(x) {
    var r = el.getBoundingClientRect();
    if (!r.width) return;
    setVal((x - r.left) / r.width * 100);
  }
  el.addEventListener('pointerdown', function (e) {
    dragging = true;
    try { el.setPointerCapture(e.pointerId); } catch (_) {}
    fromClientX(e.clientX);
  });
  el.addEventListener('pointermove', function (e) {
    if (!dragging) return;
    fromClientX(e.clientX);
  });
  function endDrag(e) {
    dragging = false;
    try { el.releasePointerCapture(e.pointerId); } catch (_) {}
  }
  el.addEventListener('pointerup', endDrag);
  el.addEventListener('pointercancel', endDrag);
  el.addEventListener('keydown', function (e) {
    var big = e.shiftKey ? 10 : 2;
    switch (e.key) {
      case 'ArrowLeft': case 'ArrowDown': setVal(val - big); e.preventDefault(); break;
      case 'ArrowRight': case 'ArrowUp': setVal(val + big); e.preventDefault(); break;
      case 'Home': setVal(min); e.preventDefault(); break;
      case 'End': setVal(max); e.preventDefault(); break;
      case 'PageUp': setVal(val + 10); e.preventDefault(); break;
      case 'PageDown': setVal(val - 10); e.preventDefault(); break;
    }
  });
  setVal(50);
})();

// download version tabs (click + keyboard accessible)
(function () {
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.dl-tab'));
  if (!tabs.length) return;
  function activateTab(target) {
    var ver = target.getAttribute('data-ver');
    tabs.forEach(function (t) {
      t.classList.remove('active');
      t.setAttribute('aria-selected', 'false');
      t.setAttribute('tabindex', '-1');
    });
    document.querySelectorAll('.dl-panel').forEach(function (p) { p.classList.remove('active'); });
    document.querySelectorAll('.chlog-panel').forEach(function (p) { p.classList.remove('active'); });
    target.classList.add('active');
    target.setAttribute('aria-selected', 'true');
    target.setAttribute('tabindex', '0');
    target.focus();
    var panel = document.querySelector('.dl-panel[data-panel="' + ver + '"]');
    if (panel) panel.classList.add('active');
    var chlog = document.querySelector('.chlog-panel[data-panel="' + ver + '"]');
    if (chlog) chlog.classList.add('active');
  }
  tabs.forEach(function (tab) {
    tab.addEventListener('click', function () { activateTab(tab); });
    tab.addEventListener('keydown', function (e) {
      var idx = tabs.indexOf(tab);
      if (e.key === 'ArrowRight') { e.preventDefault(); activateTab(tabs[(idx + 1) % tabs.length]); }
      else if (e.key === 'ArrowLeft') { e.preventDefault(); activateTab(tabs[(idx - 1 + tabs.length) % tabs.length]); }
    });
  });
})();
