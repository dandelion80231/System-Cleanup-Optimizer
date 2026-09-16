// 标记 JS 可用（用于 reveal 无闪烁安全降级：CSS 中 html.js .reveal 才隐藏）
document.documentElement.classList.add('js');

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

// 滚动入场：交错 stagger，尊重 reduced-motion  // ponytail: 去掉 em-dash
(function () {
  var els = Array.prototype.slice.call(document.querySelectorAll('.reveal'));
  if (!els.length) return;
  var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // 按父容器分组，组内依次延迟，形成交错效果
  var groups = {};
  els.forEach(function (el) {
    var key = el.parentElement ? el.parentElement : document.body;
    (groups[key] = groups[key] || []).push(el);
  });
  Object.keys(groups).forEach(function (k) {
    groups[k].forEach(function (el, i) { el.style.setProperty('--rd', (i * 70) + 'ms'); });
  });

  if (reduce || !('IntersectionObserver' in window)) {
    els.forEach(function (el) { el.classList.add('in'); });
    return;
  }
  try {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } });
    }, { threshold: 0.12 });
    els.forEach(function (el) { io.observe(el); });
    // 兜底：极少数未在首屏触发的情况，1.5s 后强制显示
    window.addEventListener('load', function () {
      setTimeout(function () { els.forEach(function (el) { if (!el.classList.contains('in')) el.classList.add('in'); }); }, 1500);
    });
  } catch (e) {
    els.forEach(function (el) { el.classList.add('in'); });
  }
})();

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

// 更新日志筛选与跳转
(function () {
  var scrollContainer = document.querySelector('.timeline-scroll');
  var searchInput = document.getElementById('chlogSearch');
  var versionSelect = document.getElementById('chlogVersion');
  var countDisplay = document.getElementById('chlogCount');
  var items = Array.prototype.slice.call(document.querySelectorAll('.tl-item'));

  if (!items.length) return;

  // 提取版本信息
  var versions = [];
  items.forEach(function (item) {
    var verEl = item.querySelector('.ver');
    var dateEl = item.querySelector('.date');
    if (verEl && dateEl) {
      versions.push({ ver: verEl.textContent.trim(), date: dateEl.textContent.trim() });
    }
  });

  // 去重并填充下拉框
  var uniqueVersions = [];
  var seen = new Set();
  versions.forEach(function (v) {
    if (!seen.has(v.ver)) {
      seen.add(v.ver);
      uniqueVersions.push(v);
    }
  });

  uniqueVersions.forEach(function (v) {
    var opt = document.createElement('option');
    opt.value = v.ver;
    opt.textContent = v.ver + ' (' + v.date + ')';
    versionSelect.appendChild(opt);
  });

  // 筛选函数
  function filterItems() {
    var query = searchInput.value.toLowerCase().trim();
    var selectedVer = versionSelect.value;
    var visible = 0;

    items.forEach(function (item) {
      var text = item.textContent.toLowerCase();
      var ver = item.querySelector('.ver');
      var verText = ver ? ver.textContent.trim() : '';

      var matchVer = !selectedVer || verText === selectedVer;
      var matchQuery = !query || text.indexOf(query) !== -1;

      if (matchVer && matchQuery) {
        item.classList.remove('hidden');
        visible++;
      } else {
        item.classList.add('hidden');
      }
    });

    if (countDisplay) {
      countDisplay.textContent = visible + ' / ' + items.length + ' 条';
    }
  }

  if (searchInput) searchInput.addEventListener('input', filterItems);
  if (versionSelect) versionSelect.addEventListener('change', filterItems);

  // 点击版本跳转到对应条目
  versionSelect.addEventListener('change', function () {
    if (!this.value) return;
    var target = items.find(function (item) {
      var v = item.querySelector('.ver');
      return v && v.textContent.trim() === this.value;
    }.bind(this));
    if (target) {
      target.scrollIntoView({ behavior: 'smooth', block: 'center' });
      target.classList.add('highlighted');
      setTimeout(function () { target.classList.remove('highlighted'); }, 2000);
    }
  });

  // 初始化计数
  filterItems();
})();
