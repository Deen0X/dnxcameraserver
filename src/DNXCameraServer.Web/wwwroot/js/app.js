/* DNX Camera Server — arranque, sesión, router y utilidades de UI. */
(function () {
  'use strict';

  var App = { state: { user: null, route: null }, api: api, toast: toast, modal: modal, closeModal: closeModal };

  /* ── API ───────────────────────────────────────────────────── */
  async function api(path, opts) {
    opts = opts || {};
    var init = { credentials: 'same-origin', cache: 'no-store' };
    if (opts.method) init.method = opts.method;
    if (opts.body !== undefined) {
      init.headers = { 'Content-Type': 'application/json' };
      init.body = typeof opts.body === 'string' ? opts.body : JSON.stringify(opts.body);
    }

    var res = await fetch(path, init);
    var text = await res.text();
    var data = null;
    if (text) { try { data = JSON.parse(text); } catch (e) { data = text; } }

    if (!res.ok) {
      var message = (data && data.error) || ('HTTP ' + res.status);
      if (data && data.detail) message += ': ' + data.detail;
      var err = new Error(message);
      err.status = res.status;
      err.body = data;
      throw err;
    }
    return data;
  }

  /* ── Toast ─────────────────────────────────────────────────── */
  function toast(message, kind) {
    var root = document.getElementById('toast');
    var el = document.createElement('div');
    el.textContent = message;
    if (kind) el.className = kind;
    root.appendChild(el);
    setTimeout(function () { el.remove(); }, 3600);
  }

  /* ── Modal ─────────────────────────────────────────────────── */
  var currentModal = null;

  function modal(title, html, onMount) {
    closeModal();

    var overlay = document.createElement('div');
    overlay.className = 'modal';
    overlay.innerHTML =
      '<div class="modal-card">' +
      '<div class="card-h"><h2>' + title + '</h2><div class="grow"></div>' +
      '<button class="btn ghost sm icon-btn" data-close title="Cerrar">✕</button></div>' +
      '<div class="modal-body">' + html + '</div>' +
      '</div>';

    document.getElementById('modal-root').appendChild(overlay);
    currentModal = overlay;

    overlay.addEventListener('click', function (ev) {
      if (ev.target === overlay || ev.target.closest('[data-close]')) closeModal();
    });

    var mount = overlay.querySelector('.modal-body');
    if (onMount) onMount(mount);
    return mount;
  }

  function closeModal() {
    if (currentModal) { currentModal.remove(); currentModal = null; }
  }

  document.addEventListener('keydown', function (ev) {
    if (ev.key === 'Escape') closeModal();
  });

  /* ── Login / instalación ───────────────────────────────────── */
  var loginBox = document.getElementById('login');
  var setupBox = document.getElementById('setup');
  var shell = document.getElementById('shell');

  async function boot() {
    try {
      var state = await api('/api/setup/status');
      if (state && state.needsSetup) { showSetup(); return; }
    } catch (e) {
      // Si el estado no está disponible, seguimos con el login normal.
    }

    try {
      App.state.user = await api('/api/auth/me');
      showShell();
    } catch (e) {
      showLogin();
    }
  }

  function hideAll() {
    loginBox.classList.add('is-hidden');
    setupBox.classList.add('is-hidden');
    shell.classList.add('is-hidden');
  }

  function showLogin() {
    hideAll();
    loginBox.classList.remove('is-hidden');
    syncLoginTheme();
    document.getElementById('login-user').focus();
  }

  /* Tema también en la pantalla de login (igual que en la consola). */
  var THEME_ICONS = { auto: '🌓', dark: '🌙', light: '☀️' };
  var THEME_LABELS = { auto: 'Auto', dark: 'Oscuro', light: 'Claro' };
  var THEME_NEXT = { auto: 'dark', dark: 'light', light: 'auto' };

  function syncLoginTheme() {
    if (!window.DNXWebUI) return;
    var theme = DNXWebUI.getTheme();
    var ico = document.getElementById('login-theme-ico');
    var lbl = document.getElementById('login-theme-lbl');
    if (ico) ico.textContent = THEME_ICONS[theme] || THEME_ICONS.auto;
    if (lbl) lbl.textContent = THEME_LABELS[theme] || THEME_LABELS.auto;
  }

  document.getElementById('login-theme').addEventListener('click', function () {
    if (!window.DNXWebUI) return;
    var next = THEME_NEXT[DNXWebUI.getTheme()] || 'auto';
    DNXWebUI.applyTheme(next);
    syncLoginTheme();
  });

  function showSetup() {
    hideAll();
    setupBox.classList.remove('is-hidden');
    document.getElementById('setup-user').focus();
  }

  function showShell() {
    hideAll();
    shell.classList.remove('is-hidden');

    var user = App.state.user;
    document.getElementById('me-name').textContent = user.username;
    document.getElementById('me-role').textContent = user.role;

    // El visor está disponible para todos; la consola solo para Admin.
    document.querySelectorAll('[data-admin]').forEach(function (el) {
      el.classList.toggle('is-hidden', user.role !== 'Admin');
    });

    if (!location.hash) {
      location.hash = user.role === 'Admin' ? '#/panel' : '#/viewer';
    }
    route();
  }

  document.getElementById('login-form').addEventListener('submit', async function (ev) {
    ev.preventDefault();
    var error = document.getElementById('login-error');
    error.classList.add('is-hidden');

    try {
      App.state.user = await api('/api/auth/login', {
        method: 'POST',
        body: {
          username: document.getElementById('login-user').value,
          password: document.getElementById('login-pass').value
        }
      });
      document.getElementById('login-pass').value = '';
      showShell();
    } catch (e) {
      if (e.status === 409 && e.body && e.body.error === 'setup_required') {
        showSetup();
        return;
      }
      error.textContent = e.status === 429
        ? 'Demasiados intentos. Espera un minuto.'
        : 'Usuario o contraseña incorrectos.';
      error.classList.remove('is-hidden');
    }
  });

  document.getElementById('setup-form').addEventListener('submit', async function (ev) {
    ev.preventDefault();
    var error = document.getElementById('setup-error');
    error.classList.add('is-hidden');

    var username = document.getElementById('setup-user').value.trim();
    var pass = document.getElementById('setup-pass').value;
    var pass2 = document.getElementById('setup-pass2').value;

    function fail(message) {
      error.textContent = message;
      error.classList.remove('is-hidden');
    }

    if (pass !== pass2) { fail('Las contraseñas no coinciden.'); return; }
    if (pass.length < 8) { fail('La contraseña debe tener al menos 8 caracteres.'); return; }

    try {
      await api('/api/setup/admin', {
        method: 'POST',
        body: { username: username, password: pass }
      });

      App.state.user = await api('/api/auth/login', {
        method: 'POST',
        body: { username: username, password: pass }
      });

      document.getElementById('setup-pass').value = '';
      document.getElementById('setup-pass2').value = '';
      showShell();
      App.toast('Administrador creado', 'ok');
    } catch (e) {
      fail(e.body && e.body.error === 'setup_completed'
        ? 'La instalación ya se completó. Recarga la página.'
        : 'No se pudo crear el administrador.');
    }
  });

  document.getElementById('logout').addEventListener('click', async function () {
    try { await api('/api/auth/logout', { method: 'POST' }); } catch (e) { }
    App.state.user = null;
    location.hash = '';
    showLogin();
  });

  document.getElementById('chpass').addEventListener('click', function () {
    App.modal('Cambiar contraseña', [
      '<form id="chpass-form">',
      '<label class="field"><span>Contraseña actual</span>' +
      '<input name="current" type="password" autocomplete="current-password" required></label>',
      '<label class="field"><span>Nueva contraseña (mínimo 8)</span>' +
      '<input name="next" type="password" autocomplete="new-password" minlength="8" required></label>',
      '<label class="field"><span>Repite la nueva contraseña</span>' +
      '<input name="confirm" type="password" autocomplete="new-password" minlength="8" required></label>',
      '<div class="row end"><button type="button" class="btn ghost" data-close>Cancelar</button>' +
      '<button class="btn" type="submit">Guardar</button></div></form>'
    ].join(''), function (mount) {
      mount.querySelector('#chpass-form').addEventListener('submit', async function (ev) {
        ev.preventDefault();
        var fd = new FormData(ev.target);
        if (fd.get('next') !== fd.get('confirm')) {
          App.toast('Las contraseñas no coinciden', 'err');
          return;
        }
        try {
          await api('/api/auth/password', {
            method: 'POST',
            body: { currentPassword: fd.get('current'), newPassword: fd.get('next') }
          });
          App.toast('Contraseña actualizada', 'ok');
          App.closeModal();
        } catch (e) {
          App.toast(e.body && e.body.error === 'wrong_current_password'
            ? 'La contraseña actual no es correcta' : 'No se pudo cambiar', 'err');
        }
      });
    });
  });

  /* ── Router ────────────────────────────────────────────────── */
  var titles = {
    panel: ['Panel', 'Estado general del servicio'],
    accounts: ['Cuentas Xiaomi', 'Alta de cuentas y descubrimiento de cámaras'],
    cameras: ['Cámaras', 'Registro, perfiles y liberación'],
    users: ['Usuarios', 'Cuentas de acceso y permisos por cámara'],
    tokens: ['Dispositivos', 'Credenciales RTSP para OBS, VLC o NVR'],
    audit: ['Auditoría', 'Últimos accesos y cambios'],
    settings: ['Ajustes', 'Modo CPU y comportamiento del servicio'],
    viewer: ['Visor', 'Cámaras disponibles para tu cuenta']
  };

  var adminRoutes = ['panel', 'accounts', 'cameras', 'users', 'tokens', 'audit', 'settings'];

  async function route() {
    if (!App.state.user) return;

    var name = (location.hash || '').replace(/^#\//, '') || 'viewer';
    if (adminRoutes.indexOf(name) >= 0 && App.state.user.role !== 'Admin') {
      location.hash = '#/viewer';
      return;
    }

    var meta = titles[name] || titles.viewer;
    document.getElementById('title').textContent = meta[0];
    document.getElementById('subtitle').textContent = meta[1];

    document.querySelectorAll('.nav a').forEach(function (a) {
      a.classList.toggle('active', a.dataset.route === name);
    });

    var root = document.getElementById('view');
    root.innerHTML = '<p class="muted">Cargando…</p>';

    try {
      if (Views[name]) await Views[name](root);
      else root.innerHTML = '<div class="card"><p class="muted">Sección no encontrada.</p></div>';
      App.state.route = name;
    } catch (e) {
      console.error(e);
      root.innerHTML = '<div class="card"><p class="badge err">Error: ' + (e.message || e) + '</p>' +
        '<p class="hint">Comprueba que el servicio y go2rtc estén disponibles.</p></div>';
    }
  }

  window.addEventListener('hashchange', route);
  document.addEventListener('dnx:refresh', function () {
    if (App.state.user && App.state.route) route();
  });

  /* LED de estado = refrescar (comportamiento del pack DNX). */
  api('/healthz').then(function (h) {
    var dot = document.getElementById('live-dot');
    var text = document.getElementById('live-text');
    if (dot) dot.className = 'dot ' + (h.status === 'ok' ? 'ok' : 'warn');
    if (text) text.textContent = h.status === 'ok' ? 'en vivo' : 'degradado';
  }).catch(function () { });

  window.App = App;
  boot();
})();
