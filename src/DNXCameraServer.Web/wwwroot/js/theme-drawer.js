/* DNX Web UI — tema cíclico + drawer móvil (vanilla, sin dependencias). */
(function () {
  'use strict';

  /* ── Tema Auto / Oscuro / Claro ─────────────────────────────── */
  var THEME_KEY = 'dnx_theme';
  var ICONS = { auto: '🌓', dark: '🌙', light: '☀️' };
  var LABELS = { auto: 'Auto', dark: 'Oscuro', light: 'Claro' };
  var NEXT = { auto: 'dark', dark: 'light', light: 'auto' };
  var theme = localStorage.getItem(THEME_KEY) || 'auto';

  function applyTheme(t) {
    t = NEXT[t] ? t : 'auto';
    theme = t;
    document.documentElement.setAttribute('data-theme', t);
    localStorage.setItem(THEME_KEY, t);
    var btn = document.getElementById('theme-btn');
    var ico = document.getElementById('theme-ico');
    var lbl = document.getElementById('theme-lbl');
    if (btn) btn.setAttribute('aria-pressed', t === 'auto' ? 'false' : 'mixed');
    if (ico) ico.textContent = ICONS[t];
    if (lbl) lbl.textContent = LABELS[t];
  }

  // Exponer para que la app pueda leer/cambiar el tema.
  window.DNXWebUI = { applyTheme: applyTheme, getTheme: function () { return theme; } };
  applyTheme(theme);

  document.addEventListener('click', function (e) {
    if (e.target.closest && e.target.closest('#theme-btn')) {
      applyTheme(NEXT[theme]);
    }
  });

  /* ── Drawer móvil ───────────────────────────────────────────── */
  function sidebar() { return document.querySelector('.sidebar'); }
  function overlay() { return document.getElementById('drawer-overlay'); }

  function closeDrawer() {
    var sb = sidebar(), ov = overlay();
    if (sb) sb.classList.remove('open');
    if (ov) ov.classList.remove('open');
  }
  function toggleDrawer() {
    var sb = sidebar(), ov = overlay();
    if (!sb) return;
    var open = sb.classList.toggle('open');
    if (ov) ov.classList.toggle('open', open);
  }
  window.DNXWebUI.toggleDrawer = toggleDrawer;
  window.DNXWebUI.closeDrawer = closeDrawer;

  document.addEventListener('click', function (e) {
    if (e.target.closest && e.target.closest('#menu-btn')) { toggleDrawer(); return; }
    if (e.target.closest && e.target.closest('#drawer-overlay')) { closeDrawer(); return; }
    // Clic fuera del sidebar con el drawer abierto → cerrar.
    var sb = sidebar();
    if (sb && sb.classList.contains('open') && !e.target.closest('.sidebar')) {
      closeDrawer();
    }
  });

  // Elegir una sección cierra el drawer.
  document.addEventListener('click', function (e) {
    if (e.target.closest && e.target.closest('.nav a, .drawer-nav a')) closeDrawer();
  });

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') closeDrawer();
  });

  // El LED de estado actúa como "refrescar".
  var live = document.getElementById('live');
  if (live) {
    var doRefresh = function () { document.dispatchEvent(new CustomEvent('dnx:refresh')); };
    live.addEventListener('click', doRefresh);
    live.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); doRefresh(); }
    });
  }
})();
