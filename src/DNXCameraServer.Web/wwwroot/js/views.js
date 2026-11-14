/* DNX Camera Server — vistas de la consola y del visor. */
(function () {
  'use strict';

  var Views = {};

  function esc(v) {
    return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function fmt(ts) {
    if (!ts) return '—';
    try { return new Date(ts).toLocaleString(); } catch (e) { return esc(ts); }
  }

  function stateBadge(state) {
    var map = {
      Live: 'ok', Idle: 'info', Connecting: 'warn', Retrying: 'warn',
      Released: 'warn', Error: 'err', Disabled: 'off'
    };
    var kind = map[state] || 'info';
    return '<span class="badge ' + kind + '">' + esc(state || '—') + '</span>';
  }

  /* ─────────────────────────── Panel ─────────────────────────── */
  Views.panel = async function (root) {
    var data = await Promise.all([
      App.api('/api/status'),
      App.api('/api/cameras')
    ]);
    var status = data[0], cameras = data[1];

    var online = cameras.filter(function (c) { return c.state === 'Live'; }).length;
    var released = cameras.filter(function (c) { return c.releasedUntil; }).length;

    root.innerHTML =
      '<div class="grid g4">' +
        '<div class="stat"><div class="k">Cámaras</div><div class="v">' + cameras.length + '</div>' +
          '<div class="s">' + online + ' en directo</div></div>' +
        '<div class="stat"><div class="k">go2rtc</div><div class="v">' +
          (status.go2rtc ? '<span class="badge ok">ok</span>' : '<span class="badge err">caído</span>') +
          '</div><div class="s">plano de medios</div></div>' +
        '<div class="stat"><div class="k">Modo CPU</div><div class="v">' + esc(status.cpuMode) + '</div>' +
          '<div class="s">ajuste global</div></div>' +
        '<div class="stat"><div class="k">Disco</div><div class="v">' +
          (status.disk ? status.disk.freeGb + ' GB' : '—') + '</div>' +
          '<div class="s">libres de ' + (status.disk ? status.disk.totalGb : '—') + ' GB</div></div>' +
      '</div>' +
      (released ? '<div class="card" style="margin-top:14px"><div class="card-h"><h2>Cámaras liberadas</h2></div>' +
        '<p class="muted small">' + released + ' cámara(s) cedidas temporalmente a la app Mi Home. ' +
        'Se reintentará la conexión al terminar el plazo.</p></div>' : '') +
      '<div class="card" style="margin-top:14px">' +
        '<div class="card-h"><h2>Cámaras</h2><div class="grow"></div>' +
        '<span class="badge info">' + cameras.length + '</span></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>nombre</th><th>modelo</th><th>ip</th><th>estado</th><th class="col-hide">visto</th>' +
        '</tr></thead><tbody>' +
        (cameras.length ? cameras.map(function (c) {
          return '<tr><td>' + esc(c.name) + '</td><td class="mono">' + esc(c.model) + '</td>' +
            '<td class="mono">' + esc(c.ip) + '</td><td>' + stateBadge(c.state) + '</td>' +
            '<td class="col-hide muted">' + fmt(c.lastSeenAt) + '</td></tr>';
        }).join('') : '<tr><td colspan="5" class="muted">Sin cámaras importadas.</td></tr>') +
        '</tbody></table></div></div>';
  };

  /* ──────────────────────── Cuentas Xiaomi ───────────────────── */
  Views.accounts = async function (root) {
    var accounts = await App.api('/api/xiaomi/accounts');

    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Cuentas Xiaomi</h2><div class="grow"></div>' +
        '<button class="btn ghost" id="acc-token">🔑 Con token</button> ' +
        '<button class="btn" id="acc-add">＋ Cuenta Xiaomi</button></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>userId</th><th>región</th><th class="col-hide">alias</th><th class="col-hide">alta</th><th></th>' +
        '</tr></thead><tbody id="acc-body"></tbody></table></div>' +
        '<p class="hint" style="margin-top:10px">Si Xiaomi rechaza el login por contraseña (error 70016), ' +
        'usa <b>Con token</b>: se salta el flujo de contraseña y usa las cookies de una sesión web.</p>' +
      '</div>';

    function regionSelect(name) {
      return '<select name="' + name + '">' +
        [['cn', 'China (cn)'], ['de', 'Europa (de)'], ['i2', 'Internacional (i2)'],
         ['ru', 'Rusia (ru)'], ['sg', 'Singapur (sg)'], ['us', 'EE.UU. (us)']]
          .map(function (r) { return '<option value="' + r[0] + '">' + r[1] + '</option>'; })
          .join('') + '</select>';
    }

    function render(list) {
      document.getElementById('acc-body').innerHTML = list.length ? list.map(function (a) {
        return '<tr><td class="mono">' + esc(a.xiaomiUserId) + '</td><td>' + esc(a.region) + '</td>' +
          '<td class="col-hide muted">' + esc(a.alias || '—') + '</td>' +
          '<td class="col-hide muted">' + fmt(a.createdAt) + '</td>' +
          '<td><button class="btn danger sm" data-del="' + a.id + '">Eliminar</button></td></tr>';
      }).join('') : '<tr><td colspan="5" class="muted">Sin cuentas.</td></tr>';

      document.querySelectorAll('[data-del]').forEach(function (btn) {
        btn.addEventListener('click', async function () {
          if (!confirm('¿Eliminar la cuenta y sus cámaras?')) return;
          await App.api('/api/xiaomi/accounts/' + btn.dataset.del, { method: 'DELETE' });
          App.toast('Cuenta eliminada', 'ok');
          Views.accounts(root);
        });
      });
    }

    function showError(mount, message, detail) {
      var box = mount.querySelector('.form-error');
      if (!box) return;
      box.innerHTML = esc(message) +
        (detail ? '<div class="hint mono" style="margin-top:6px;opacity:.85">' +
          esc(String(detail).slice(0, 400)) + '</div>' : '');
      box.classList.remove('is-hidden');
    }

    render(accounts);

    /* Alta con contraseña (puede requerir captcha o 2FA). */
    document.getElementById('acc-add').addEventListener('click', function () {
      App.modal('Añadir cuenta Xiaomi', [
        '<form id="xiaomi-form">',
        '<div class="grid-2">',
        '  <label class="field"><span>Usuario Xiaomi</span><input name="username" autocomplete="username" required></label>',
        '  <label class="field"><span>Contraseña</span><input name="password" type="password" autocomplete="current-password" required></label>',
        '</div>',
        '<label class="field"><span>Región</span>' + regionSelect('region') + '</label>',
        '<div id="xiaomi-extra"></div>',
        '<div class="badge err is-hidden form-error"></div>',
        '<div class="row end"><button type="button" class="btn ghost" data-close>Cancelar</button>',
        '<button class="btn" type="submit">Entrar</button></div>',
        '</form>'
      ].join(''), function (mount) {
        var form = mount.querySelector('#xiaomi-form');
        var extra = mount.querySelector('#xiaomi-extra');
        var submit = form.querySelector('button[type=submit]');

        form.addEventListener('submit', async function (ev) {
          ev.preventDefault();
          form.querySelector('.form-error').classList.add('is-hidden');
          submit.disabled = true;
          submit.textContent = 'Conectando…';

          var fd = new FormData(form);
          var payload = {
            username: fd.get('username'), password: fd.get('password'),
            captcha: fd.get('captcha'), verify: fd.get('verify'),
            region: fd.get('region')
          };

          try {
            await App.api('/api/xiaomi/login', { method: 'POST', body: payload });
            App.toast('Cuenta añadida', 'ok');
            App.closeModal();
            Views.accounts(root);
          } catch (err) {
            var detail = err.body || {};
            if (err.status === 401 && detail.captcha) {
              extra.innerHTML =
                '<label class="field"><span>Captcha</span>' +
                '<img class="captcha-img" src="data:image/jpeg;base64,' + detail.captcha + '" alt="captcha">' +
                '<input name="captcha" placeholder="Escribe el código" required></label>';
              showError(mount, 'Xiaomi pide un captcha. Escribe el código de la imagen.');
            } else if (err.status === 401 && (detail.verify_email || detail.verify_phone)) {
              extra.innerHTML =
                '<label class="field"><span>Código enviado a ' +
                esc(detail.verify_email || detail.verify_phone) + '</span>' +
                '<input name="verify" placeholder="Código de verificación" required></label>';
              showError(mount, 'Xiaomi ha enviado un código de verificación.');
            } else if (err.status === 429) {
              showError(mount, 'Demasiados intentos seguidos. Espera un minuto y vuelve a probar.');
            } else {
              showError(mount, detail.message ||
                ('No se pudo añadir la cuenta (' + (err.message || 'error') + ')'), detail.detail);
            }
          } finally {
            submit.disabled = false;
            submit.textContent = 'Entrar';
          }
        });
      });
    });

    /* Alta con passToken (evita el login por contraseña). */
    document.getElementById('acc-token').addEventListener('click', function () {
      App.modal('Añadir cuenta con token', [
        '<form id="token-form">',
        '<div class="grid-2">',
        '  <label class="field"><span>userId</span><input name="userId" inputmode="numeric" ' +
        '     placeholder="1234567890" required></label>',
        '  <label class="field"><span>Región</span>' + regionSelect('region') + '</label>',
        '</div>',
        '<label class="field"><span>passToken</span><input name="token" placeholder="V1:..." required></label>',
        '<label class="field"><span>Alias (opcional)</span><input name="alias" placeholder="Mi cuenta"></label>',
        '<p class="hint">Inicia sesión en <b>account.xiaomi.com</b> desde esta misma red y copia las ' +
        'cookies <span class="mono">userId</span> y <span class="mono">passToken</span> ' +
        '(DevTools → Application → Cookies). Es la vía que funciona cuando Xiaomi bloquea la contraseña.</p>',
        '<div class="badge err is-hidden form-error"></div>',
        '<div class="row end"><button type="button" class="btn ghost" data-close>Cancelar</button>',
        '<button class="btn" type="submit">Guardar token</button></div>',
        '</form>'
      ].join(''), function (mount) {
        var form = mount.querySelector('#token-form');
        var submit = form.querySelector('button[type=submit]');

        form.addEventListener('submit', async function (ev) {
          ev.preventDefault();
          form.querySelector('.form-error').classList.add('is-hidden');
          submit.disabled = true;
          submit.textContent = 'Verificando…';

          var fd = new FormData(form);
          try {
            await App.api('/api/xiaomi/token', {
              method: 'POST',
              body: {
                userId: fd.get('userId'),
                token: fd.get('token'),
                region: fd.get('region'),
                alias: fd.get('alias')
              }
            });
            App.toast('Cuenta añadida con token', 'ok');
            App.closeModal();
            Views.accounts(root);
          } catch (err) {
            var detail = err.body || {};
            showError(mount, detail.message ||
              ('No se pudo validar el token (' + (err.message || 'error') + ')'), detail.detail);
          } finally {
            submit.disabled = false;
            submit.textContent = 'Guardar token';
          }
        });
      });
    });
  };

  /* ─────────────────────────── Cámaras ───────────────────────── */
  Views.cameras = async function (root) {
    var cameras = await App.api('/api/cameras');

    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Cámaras</h2><div class="grow"></div>' +
        '<button class="btn" id="cam-import">＋ Importar</button></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>nombre</th><th>modelo</th><th class="col-hide">ip</th><th>perfil</th><th>estado</th><th></th>' +
        '</tr></thead><tbody>' +
        (cameras.length ? cameras.map(function (c) {
          return '<tr><td><b>' + esc(c.name) + '</b><div class="mono muted">' + esc(c.streamName) + '</div></td>' +
            '<td class="mono">' + esc(c.model) + '</td>' +
            '<td class="col-hide mono">' + esc(c.ip) + '</td>' +
            '<td><select data-profile="' + c.id + '">' +
              ['hd', 'sd', 'auto'].map(function (p) {
                return '<option value="' + p + '"' + (c.profile === p ? ' selected' : '') + '>' + p + '</option>';
              }).join('') + '</select></td>' +
            '<td>' + stateBadge(c.state) + (c.releasedUntil ? '<div class="muted small">hasta ' +
              fmt(c.releasedUntil) + '</div>' : '') + '</td>' +
            '<td class="nowrap">' +
              '<button class="btn ghost sm" data-release="' + c.id + '" title="Ceder a la app Mi Home 10 min">⇄</button> ' +
              '<button class="btn ghost sm" data-reconnect="' + c.id + '" title="Reconectar">⟳</button> ' +
              '<button class="btn danger sm" data-delcam="' + c.id + '" title="Eliminar">🗑️</button>' +
            '</td></tr>';
        }).join('') : '<tr><td colspan="6" class="muted">Sin cámaras. Importa desde una cuenta Xiaomi.</td></tr>') +
        '</tbody></table></div>' +
        '<p class="hint" style="margin-top:10px">Cambiar el perfil reabre la sesión P2P (corte de 2-5 s). ' +
        'El botón ⇄ libera la cámara para usar la app Mi Home.</p>' +
      '</div>';

    document.querySelectorAll('[data-profile]').forEach(function (sel) {
      sel.addEventListener('change', async function () {
        await App.api('/api/cameras/' + sel.dataset.profile + '/profile', {
          method: 'POST', body: { profile: sel.value }
        });
        App.toast('Perfil actualizado', 'ok');
      });
    });

    document.querySelectorAll('[data-release]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        await App.api('/api/cameras/' + btn.dataset.release + '/release', {
          method: 'POST', body: { minutes: 10 }
        });
        App.toast('Cámara liberada 10 minutos', 'ok');
        Views.cameras(root);
      });
    });

    document.querySelectorAll('[data-reconnect]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        await App.api('/api/cameras/' + btn.dataset.reconnect + '/reconnect', { method: 'POST' });
        App.toast('Reconectando', 'ok');
        Views.cameras(root);
      });
    });

    document.querySelectorAll('[data-delcam]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        if (!confirm('¿Eliminar la cámara?')) return;
        await App.api('/api/cameras/' + btn.dataset.delcam, { method: 'DELETE' });
        App.toast('Cámara eliminada', 'ok');
        Views.cameras(root);
      });
    });

    document.getElementById('cam-import').addEventListener('click', async function () {
      var accounts = await App.api('/api/xiaomi/accounts');
      if (!accounts.length) { App.toast('Primero añade una cuenta Xiaomi', 'err'); return; }

      App.modal('Importar cámaras', [
        '<div class="row">',
        '  <label class="field grow"><span>Cuenta</span><select id="imp-account">' +
          accounts.map(function (a) {
            return '<option value="' + a.id + '">' + esc(a.alias || a.xiaomiUserId) + ' (' + esc(a.region) + ')</option>';
          }).join('') + '</select></label>',
        '  <button class="btn" id="imp-load">Cargar dispositivos</button>',
        '</div>',
        '<div id="imp-list" class="stack"><p class="hint">Carga la lista desde Xiaomi.</p></div>'
      ].join(''), function (mount) {
        mount.querySelector('#imp-load').addEventListener('click', async function () {
          var accountId = mount.querySelector('#imp-account').value;
          var list = mount.querySelector('#imp-list');
          list.innerHTML = '<p class="muted small">Cargando…</p>';
          try {
            var data = await App.api('/api/xiaomi/devices?accountId=' + accountId);
            var sources = data.sources || [];
            if (!sources.length) { list.innerHTML = '<p class="muted">La cuenta no devolvió cámaras.</p>'; return; }

            list.innerHTML = '<div class="table-wrap"><table><thead><tr><th></th><th>nombre</th>' +
              '<th>modelo</th><th class="col-hide">ip</th></tr></thead><tbody>' +
              sources.map(function (s, i) {
                var parsed = parseXiaomiUrl(s.url);
                return '<tr><td><input type="checkbox" data-imp="' + i + '" checked></td>' +
                  '<td>' + esc(s.name) + '</td><td class="mono">' + esc(parsed.model) + '</td>' +
                  '<td class="col-hide mono">' + esc(parsed.ip) + '</td>' +
                  '<td class="is-hidden" data-did="' + i + '">' + esc(parsed.did) + '</td></tr>';
              }).join('') + '</tbody></table></div>' +
              '<div class="row end"><button class="btn" id="imp-go">Importar seleccionadas</button></div>';

            list.querySelector('#imp-go').addEventListener('click', async function () {
              var checks = list.querySelectorAll('[data-imp]');
              var ok = 0, fail = 0;
              for (var i = 0; i < checks.length; i++) {
                if (!checks[i].checked) continue;
                var parsed = parseXiaomiUrl(sources[i].url);
                try {
                  await App.api('/api/cameras', {
                    method: 'POST',
                    body: {
                      accountId: accountId, did: parsed.did, model: parsed.model,
                      name: sources[i].name, ip: parsed.ip, channel: 0
                    }
                  });
                  ok++;
                } catch (e) { fail++; }
              }
              App.toast('Importadas: ' + ok + (fail ? ' · fallos: ' + fail : ''), fail ? 'err' : 'ok');
              App.closeModal();
              Views.cameras(root);
            });
          } catch (e) {
            list.innerHTML = '<p class="badge err">No se pudo listar: ' + esc(e.message) + '</p>';
          }
        });
      });
    });

    function parseXiaomiUrl(url) {
      try {
        var u = new URL(url);
        return {
          ip: u.hostname,
          did: u.searchParams.get('did') || '',
          model: u.searchParams.get('model') || ''
        };
      } catch (e) {
        return { ip: '', did: '', model: '' };
      }
    }
  };

  /* ─────────────────────────── Usuarios ──────────────────────── */
  Views.users = async function (root) {
    var users = await App.api('/api/users');

    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Usuarios</h2><div class="grow"></div>' +
        '<button class="btn" id="user-add">＋ Nuevo usuario</button></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>usuario</th><th>rol</th><th>estado</th><th class="col-hide">último acceso</th><th></th>' +
        '</tr></thead><tbody>' +
        users.map(function (u) {
          return '<tr><td><b>' + esc(u.username) + '</b></td><td>' + esc(u.role) + '</td>' +
            '<td>' + (u.isActive ? '<span class="badge ok">activo</span>' : '<span class="badge off">inactivo</span>') + '</td>' +
            '<td class="col-hide muted">' + fmt(u.lastLoginAt) + '</td>' +
            '<td class="nowrap">' +
              '<button class="btn ghost sm" data-grants="' + u.id + '">Permisos</button> ' +
              (u.isActive ? '<button class="btn danger sm" data-userdel="' + u.id + '">Baja</button>' : '') +
            '</td></tr>';
        }).join('') +
        '</tbody></table></div>' +
      '</div>';

    document.getElementById('user-add').addEventListener('click', function () {
      App.modal('Nuevo usuario', [
        '<form id="user-form">',
        '<label class="field"><span>Usuario</span><input name="username" required minlength="3"></label>',
        '<label class="field"><span>Contraseña</span><input name="password" type="password" required minlength="8"></label>',
        '<label class="field"><span>Rol</span><select name="role">' +
        '<option value="Viewer">Viewer</option><option value="Admin">Admin</option></select></label>',
        '<div class="row end"><button type="button" class="btn ghost" data-close>Cancelar</button>' +
        '<button class="btn" type="submit">Crear</button></div></form>'
      ].join(''), function (mount) {
        mount.querySelector('#user-form').addEventListener('submit', async function (ev) {
          ev.preventDefault();
          var fd = new FormData(ev.target);
          try {
            await App.api('/api/users', {
              method: 'POST',
              body: { username: fd.get('username'), password: fd.get('password'), role: fd.get('role') }
            });
            App.toast('Usuario creado', 'ok');
            App.closeModal();
            Views.users(root);
          } catch (e) {
            App.toast(e.body && e.body.error === 'weak_password' ? 'Contraseña demasiado corta' : 'No se pudo crear', 'err');
          }
        });
      });
    });

    document.querySelectorAll('[data-userdel]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        if (!confirm('¿Dar de baja al usuario?')) return;
        try {
          await App.api('/api/users/' + btn.dataset.userdel, { method: 'DELETE' });
          App.toast('Usuario desactivado', 'ok');
          Views.users(root);
        } catch (e) { App.toast('No se pudo dar de baja', 'err'); }
      });
    });

    document.querySelectorAll('[data-grants]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        var userId = btn.dataset.grants;
        var data = await Promise.all([
          App.api('/api/cameras'),
          App.api('/api/users/' + userId + '/grants')
        ]);
        var cameras = data[0], grants = data[1];
        var byCamera = {};
        grants.forEach(function (g) { byCamera[g.cameraId] = g; });

        App.modal('Permisos de cámara', [
          '<div class="grants-grid">',
          cameras.length ? cameras.map(function (c) {
            var g = byCamera[c.id] || { canView: false, canListen: false, canTalk: false };
            return '<div class="grant-row">' +
              '<span>' + esc(c.name) + '</span>' +
              '<label class="small"><input type="checkbox" data-g="' + c.id + '" data-p="view"' + (g.canView ? ' checked' : '') + '> ver</label>' +
              '<label class="small"><input type="checkbox" data-g="' + c.id + '" data-p="listen"' + (g.canListen ? ' checked' : '') + '> oír</label>' +
              '<label class="small"><input type="checkbox" data-g="' + c.id + '" data-p="talk"' + (g.canTalk ? ' checked' : '') + '> hablar</label>' +
              '</div>';
          }).join('') : '<p class="muted">Sin cámaras.</p>',
          '</div>',
          '<div class="row end"><button class="btn" id="grants-save">Guardar</button></div>'
        ].join(''), function (mount) {
          mount.querySelector('#grants-save').addEventListener('click', async function () {
            var items = [];
            cameras.forEach(function (c) {
              function get(p) {
                var el = mount.querySelector('[data-g="' + c.id + '"][data-p="' + p + '"]');
                return el ? el.checked : false;
              }
              var view = get('view'), listen = get('listen'), talk = get('talk');
              if (view || listen || talk)
                items.push({ cameraId: c.id, canView: view, canListen: listen, canTalk: talk });
            });
            await App.api('/api/users/' + userId + '/grants', { method: 'PUT', body: items });
            App.toast('Permisos guardados', 'ok');
            App.closeModal();
          });
        });
      });
    });
  };

  /* ────────────────────── Tokens de dispositivo ─────────────── */
  Views.tokens = async function (root) {
    var data = await Promise.all([App.api('/api/device-tokens'), App.api('/api/cameras')]);
    var tokens = data[0], cameras = data[1];

    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Dispositivos (RTSP)</h2><div class="grow"></div>' +
        '<button class="btn" id="tok-add">＋ Nuevo token</button></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>nombre</th><th>token</th><th>cámara</th><th>estado</th><th class="col-hide">último uso</th><th></th>' +
        '</tr></thead><tbody>' +
        (tokens.length ? tokens.map(function (t) {
          var state = t.revokedAt ? '<span class="badge err">revocado</span>' :
            (t.expiresAt && new Date(t.expiresAt) < new Date() ? '<span class="badge warn">caducado</span>' :
              '<span class="badge ok">activo</span>');
          return '<tr><td>' + esc(t.name) + '</td><td class="mono">' + esc(t.tokenId) + '</td>' +
            '<td>' + esc(t.camera || '—') + '</td><td>' + state + '</td>' +
            '<td class="col-hide muted">' + fmt(t.lastUsedAt) + (t.lastIp ? '<div class="mono">' + esc(t.lastIp) + '</div>' : '') + '</td>' +
            '<td>' + (t.revokedAt ? '' : '<button class="btn danger sm" data-tokdel="' + t.id + '">Revocar</button>') + '</td></tr>';
        }).join('') : '<tr><td colspan="6" class="muted">Sin tokens.</td></tr>') +
        '</tbody></table></div>' +
        '<p class="hint" style="margin-top:10px">Cada token da acceso RTSP a una sola cámara. ' +
        'El secreto se muestra una única vez.</p>' +
      '</div>';

    document.querySelectorAll('[data-tokdel]').forEach(function (btn) {
      btn.addEventListener('click', async function () {
        if (!confirm('¿Revocar el token?')) return;
        await App.api('/api/device-tokens/' + btn.dataset.tokdel, { method: 'DELETE' });
        App.toast('Token revocado', 'ok');
        Views.tokens(root);
      });
    });

    document.getElementById('tok-add').addEventListener('click', function () {
      if (!cameras.length) { App.toast('No hay cámaras', 'err'); return; }
      App.modal('Nuevo token de dispositivo', [
        '<form id="tok-form">',
        '<label class="field"><span>Cámara</span><select name="cameraId">' +
        cameras.map(function (c) { return '<option value="' + c.id + '">' + esc(c.name) + '</option>'; }).join('') +
        '</select></label>',
        '<label class="field"><span>Nombre</span><input name="name" placeholder="OBS salón" required></label>',
        '<label class="field"><span>Caducidad (días, opcional)</span><input name="days" type="number" min="0"></label>',
        '<div class="row end"><button type="button" class="btn ghost" data-close>Cancelar</button>' +
        '<button class="btn" type="submit">Crear</button></div></form>'
      ].join(''), function (mount) {
        mount.querySelector('#tok-form').addEventListener('submit', async function (ev) {
          ev.preventDefault();
          var fd = new FormData(ev.target);
          var days = parseInt(fd.get('days'), 10);
          var created = await App.api('/api/device-tokens', {
            method: 'POST',
            body: {
              cameraId: fd.get('cameraId'),
              name: fd.get('name'),
              expiresInDays: isNaN(days) || days <= 0 ? null : days
            }
          });

          App.closeModal();
          App.modal('Token creado', [
            '<p class="hint">Copia esta URL ahora: no se volverá a mostrar.</p>',
            '<div class="code-box">' + esc(created.rtspUrl) + '</div>',
            '<div class="row end"><button class="btn" id="tok-copy">Copiar</button></div>'
          ].join(''), function (m2) {
            m2.querySelector('#tok-copy').addEventListener('click', function () {
              navigator.clipboard.writeText(created.rtspUrl).then(function () {
                App.toast('Copiado', 'ok');
              });
            });
          });
          Views.tokens(root);
        });
      });
    });
  };

  /* ────────────────────────── Auditoría ──────────────────────── */
  Views.audit = async function (root) {
    var events = await App.api('/api/audit?limit=200');
    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Auditoría</h2><div class="grow"></div>' +
        '<span class="badge info">' + events.length + '</span></div>' +
        '<div class="table-wrap"><table><thead><tr>' +
        '<th>fecha</th><th>acción</th><th>actor</th><th class="col-hide">ip</th><th class="col-hide">detalle</th>' +
        '</tr></thead><tbody>' +
        (events.length ? events.map(function (e) {
          return '<tr><td class="nowrap">' + fmt(e.at) + '</td><td><span class="pill">' + esc(e.action) + '</span></td>' +
            '<td>' + esc(e.actorType) + ': ' + esc(e.actorId || '—') + '</td>' +
            '<td class="col-hide mono">' + esc(e.ip || '—') + '</td>' +
            '<td class="col-hide muted small">' + esc(e.detailJson || '') + '</td></tr>';
        }).join('') : '<tr><td colspan="5" class="muted">Sin eventos.</td></tr>') +
        '</tbody></table></div></div>';
  };

  /* ─────────────────────────── Ajustes ───────────────────────── */
  Views.settings = async function (root) {
    var settings = await App.api('/api/settings');

    root.innerHTML =
      '<div class="card">' +
        '<div class="card-h"><h2>Ajustes del servicio</h2></div>' +
        '<form id="set-form" class="stack">' +
        '<div class="grid-2">' +
        '<label class="field"><span>Modo CPU</span><select name="cpu.mode">' +
        ['Lite', 'Auto', 'Calidad'].map(function (m) {
          return '<option value="' + m + '"' + (settings['cpu.mode'] === m ? ' selected' : '') + '>' + m + '</option>';
        }).join('') + '</select></label>' +
        '<label class="field"><span>Minutos de liberación</span>' +
        '<input name="release.minutes" type="number" min="1" max="240" value="' +
        esc(settings['release.minutes'] || '10') + '"></label>' +
        '<label class="field"><span>Cierre de productor inactivo (s)</span>' +
        '<input name="producer.idle.seconds" type="number" min="10" max="600" value="' +
        esc(settings['producer.idle.seconds'] || '60') + '"></label>' +
        '<label class="field"><span>SSO por cabecera</span>' +
        '<select name="sso.enabled"><option value="false">Desactivado</option>' +
        '<option value="true"' + (settings['sso.enabled'] === 'true' ? ' selected' : '') + '>Activado</option></select></label>' +
        '</div>' +
        '<p class="hint">El modo CPU gobierna transcoding, muestreo de movimiento y grabación ' +
        '(fase 2). El SSO se define por variables de entorno; aquí solo se refleja el estado.</p>' +
        '<div class="row end"><button class="btn" type="submit">Guardar</button></div>' +
        '</form></div>';

    document.getElementById('set-form').addEventListener('submit', async function (ev) {
      ev.preventDefault();
      var fd = new FormData(ev.target);
      var values = {};
      fd.forEach(function (v, k) { values[k] = String(v); });
      await App.api('/api/settings', { method: 'PUT', body: { values: values } });
      App.toast('Ajustes guardados', 'ok');
    });
  };

  /* ──────────────────────────── Visor ────────────────────────── */
  Views.viewer = async function (root) {
    var cameras = await App.api('/api/cameras');

    root.innerHTML =
      '<div class="row" style="margin-bottom:12px">' +
        '<span class="pill">' + cameras.length + ' cámaras disponibles</span>' +
        '<div class="grow"></div>' +
        '<button class="btn ghost sm" id="vw-reload">⟳ Recargar</button>' +
      '</div>' +
      '<div class="cam-grid" id="cam-grid"></div>';

    var grid = document.getElementById('cam-grid');
    if (!cameras.length) {
      grid.innerHTML = '<div class="card"><p class="muted">No tienes cámaras asignadas.</p></div>';
      return;
    }

    var players = {};
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var id = entry.target.dataset.cam;
        if (entry.isIntersecting) {
          if (!players[id]) {
            var holder = entry.target.querySelector('.holder');
            players[id] = new DnxPlayer(holder, id, { muted: true });
            players[id].start();
          }
        } else if (players[id]) {
          players[id].stop();
          delete players[id];
          entry.target.querySelector('.holder').innerHTML = '';
        }
      });
    }, { threshold: 0.15 });

    cameras.forEach(function (c) {
      var tile = document.createElement('div');
      tile.className = 'cam-tile';
      tile.dataset.cam = c.id;
      tile.innerHTML =
        '<div class="holder" style="position:absolute;inset:0"></div>' +
        '<div class="corner">' + stateBadge(c.state) + '</div>' +
        '<div class="overlay"><b>' + esc(c.name) + '</b><div class="grow"></div>' +
        '<span class="state">' + esc(c.model) + '</span></div>';
      tile.addEventListener('click', function () { openStage(c); });
      grid.appendChild(tile);
      observer.observe(tile);
    });

    document.getElementById('vw-reload').addEventListener('click', function () {
      Object.keys(players).forEach(function (k) { players[k].stop(); });
      Views.viewer(root);
    });

    function openStage(camera) {
      var wrap = document.createElement('div');
      wrap.className = 'stage';
      wrap.innerHTML =
        '<div class="stage-head"><b>' + esc(camera.name) + '</b>' +
        '<span class="pill">' + esc(camera.model) + '</span><div class="grow"></div>' +
        '<span id="stage-state" class="muted small"></span>' +
        '<button class="btn ghost sm" id="stage-close">✕</button></div>' +
        '<div class="stage-body"><div class="zoom-wrap" id="zoom-wrap"></div></div>' +
        '<div class="stage-foot">' +
        '<button class="btn sm" id="z-out" title="Alejar">－</button>' +
        '<button class="btn sm" id="z-reset" title="Restablecer">1:1</button>' +
        '<button class="btn sm" id="z-in" title="Acercar">＋</button>' +
        '<button class="btn sm" id="snap" title="Snapshot">📷</button>' +
        '<button class="btn sm" id="mute" title="Silenciar/activar audio">🔇</button>' +
        '<button class="btn sm" id="talk" title="Hablar (requiere HTTPS)" disabled>🎙</button>' +
        '</div>';

      document.body.appendChild(wrap);

      var holder = wrap.querySelector('#zoom-wrap');
      var player = new DnxPlayer(holder, camera.id, { muted: true });
      player.start();

      var zoom = null;
      var timer = setInterval(function () {
        var video = holder.querySelector('video');
        if (video && !zoom) zoom = DnxZoom(holder, video, { scale: 1, x: 0, y: 0 });
      }, 400);

      if (!window.isSecureContext) {
        wrap.querySelector('#talk').title = 'Requiere HTTPS (contexto seguro)';
      }

      function close() {
        clearInterval(timer);
        player.stop();
        wrap.remove();
      }

      wrap.querySelector('#stage-close').addEventListener('click', close);
      document.addEventListener('keydown', function onEsc(ev) {
        if (ev.key === 'Escape') { close(); document.removeEventListener('keydown', onEsc); }
      });

      wrap.querySelector('#z-in').addEventListener('click', function () {
        var video = holder.querySelector('video');
        if (video) video.dispatchEvent(new WheelEvent('wheel', { deltaY: -100, bubbles: true }));
      });
      wrap.querySelector('#z-out').addEventListener('click', function () {
        var video = holder.querySelector('video');
        if (video) video.dispatchEvent(new WheelEvent('wheel', { deltaY: 100, bubbles: true }));
      });
      wrap.querySelector('#z-reset').addEventListener('click', function () {
        if (zoom) zoom.reset();
      });
      wrap.querySelector('#snap').addEventListener('click', function () {
        window.open('/api/cameras/' + camera.id + '/snapshot?w=1920', '_blank');
      });

      var muted = true;
      wrap.querySelector('#mute').addEventListener('click', function () {
        muted = !muted;
        player.setMuted(muted);
        wrap.querySelector('#mute').textContent = muted ? '🔇' : '🔊';
      });
    }
  };

  window.Views = Views;
})();
