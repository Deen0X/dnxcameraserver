# DNX Camera Server — Plan de implementación

> **Base:** `Project_Architecture.md` (v0.1, ADR-001…017) · **Fecha:** 2026-11-13
> **Alcance:** servidor autoalojado LAN para cámaras Xiaomi, con consola de
> administración, visor web, exportación RTSP autenticada y grabación por evento.

---

## 0. Convenciones del plan

- **ID de tarea:** `F{ fase }-{ nn }`. **S** = spike (investigación con timebox).
- **Estado:** `pendiente` / `en curso` / `hecho` / `bloqueado`.
- **DoD (Definition of Done)** de cada tarea:
  1. Compila sin warnings nuevos y pasa `dotnet test`.
  2. Tiene prueba (unitaria o de integración) o justificación escrita de por qué no.
  3. Logs y errores manejados (sin `catch` vacíos ni stack traces al cliente).
  4. Documentado en `README` o en el SSoT si cambia una decisión.
- **Estimaciones:** orientativas, en jornadas de una persona. No son compromiso.

---

## 1. Fases y entregables

| Fase | Objetivo | Entregable demostrable | Estimación |
|---|---|---|---|
| **F0** | Fundaciones y esqueleto operable | Contenedor que arranca con go2rtc y `/healthz` verde | 2–3 d |
| **F1** | MVP usable | Login Xiaomi real, catálogo, visor, RTSP con tokens, RBAC, auditoría | 10–15 d |
| **F2** | Audio y grabación | Talk, grabador con anillo RAM, movimiento, timeline, ventana de liberación | 8–12 d |
| **F3** | Extras y publicación | ONVIF, pulido, multi-arch, documentación de release | 5–8 d |

**Total orientativo:** 25–38 jornadas.

---

## 2. Estructura de la solución

```
DNXCameraServer/
  DnxCameraServer.sln
  Directory.Build.props           # nullable, warnings, analyzers, TFM
  Directory.Packages.props        # versiones centralizadas
  src/
    Dnx.CameraServer.Web/         # ASP.NET Core 10: API, workers, gateway RTSP, wwwroot
    Dnx.CameraServer.Domain/      # entidades, capacidades, contratos, reglas
    Dnx.CameraServer.Data/        # EF Core, migraciones, repositorios
    Dnx.CameraServer.Tests/       # xUnit + WebApplicationFactory
  data/models/*.json              # fichas de capacidades por modelo
  docker/ Dockerfile, compose.yaml, go2rtc.yaml.tpl, entrypoint.sh
  IMPLEMENTATION_PLAN.md
  Project_Architecture.md
```

Capas internas de `Web`: `Api/`, `Auth/`, `Xiaomi/`, `Cameras/`, `Streaming/`,
`Rtsp/`, `Devices/`, `Audit/`, `Health/`, `Workers/`, `Go2Rtc/`, `wwwroot/`.

---

## 3. F0 — Fundaciones (2–3 d)

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F0-01 | Esqueleto de solución y proyectos, TFM `net10.0`, `Directory.Build.props` (nullable, analyzers, `TreatWarningsAsErrors` selectivo) | Solución que compila vacía | — |
| F0-02 | Dockerfile multi-stage + descarga de **go2rtc v1.9.15** con **checksum fijado** por arquitectura | Imagen `amd64`/`arm64` reproducible | F0-01 |
| F0-03 | `compose.yaml`: `network_mode: host`, volúmenes `/data` y `/recordings`, `stop_grace_period: 30s`, healthcheck, `TZ` | `docker compose up` levanta todo | F0-02 |
| F0-04 | Configuración por entorno + validación al arranque (falla rápido si `WEBRTC_CANDIDATE` o `LAN_CIDR` son inválidos) | Options tipadas + validadores | F0-01 |
| F0-05 | Logging estructurado (consola + fichero rotativo), request logging, correlation id | Logs consultables | F0-04 |
| F0-06 | `/healthz` (proceso, go2rtc, disco) y `/readyz` | Endpoint con JSON de estado | F0-05 |
| F0-07 | EF Core + SQLite (WAL) + migración inicial vacía | `camera.db` creado al arrancar | F0-01 |
| F0-08 | **Supervisor de go2rtc**: genera `go2rtc.yaml` desde config, arranca el proceso, captura stdout, reinicia con backoff, apaga ordenadamente | Sidecar gestionado | F0-02 |
| F0-09 | Cliente tipado de la API de go2rtc (`/api/streams`, `/api/xiaomi`, `/api/frame.jpeg`, `/api/ws`) | Cliente con tests de contrato | F0-08 |
| F0-10 | CI: `dotnet build` + `dotnet test` + `docker buildx` multi-arch | Pipeline verde | F0-03 |

**Aceptación F0:** `docker compose up` arranca app + go2rtc; `/healthz` informa de
ambos; matar go2rtc lo reinicia en <10 s; la imagen arranca en x86 y ARM.

---

## 4. F1 — MVP (10–15 d)

### 4.1 Identidad y acceso

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F1-01 | Autenticación local: hash de contraseña, cookie `HttpOnly`+`SameSite=Strict`, expiración deslizante, bloqueo por intentos, CSRF en formularios | Login/logout/`me` | F0-07 |
| F1-02 | SSO por cabecera **oportunista**: lista de proxies de confianza, `Remote-User`, mapeo a cuenta local, **fallback a login local** | Middleware + tests de ambos caminos | F1-01 |
| F1-03 | RBAC + ACL: roles `Admin`/`Viewer`, permisos `ver`/`oír`/`hablar` por cámara | Filtro de autorización reutilizable | F1-01 |
| F1-04 | Endurecimiento: cabeceras de seguridad, límites de tasa en login y API, CORS cerrado | Checklist aplicado | F1-01 |

### 4.2 Integración Xiaomi

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F1-05 | Gateway de login: `password` → `verify` (2FA) → `captcha`, proxificando `/api/xiaomi`; estados y reintentos en UI | Flujo completo con cuenta real | F0-09 |
| F1-06 | Cuentas: alta, alias, región, borrado (elimina token en go2rtc), estado del token | CRUD + UI | F1-05 |
| F1-07 | Catálogo: listar dispositivos, filtrar cámaras, importar a `Camera` con did/modelo/IP | Importación idempotente | F1-06 |
| F1-08 | **Registro de capacidades**: fichas `data/models/*.json`, `probe` en runtime, override manual; API `/capabilities` | Modelo + merge + tests | F1-07 |

### 4.3 Medios

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F1-09 | Ciclo de vida de stream: crear/actualizar/borrar en go2rtc, nombres `cam_<base32>`, perfil HD/SD/auto | Servicio `StreamManager` | F0-09 |
| F1-10 | Proxy de señalización WebRTC (WHEP) con autorización por cámara; configuración `webrtc.candidates` sin STUN | `/api/cameras/{id}/webrtc/offer` | F1-09 |
| F1-11 | Proxy de HLS y MJPEG (fallback) y snapshot (`/frame.jpeg` con `width`) | Endpoints con ACL | F1-09 |
| F1-12 | **Gateway RTSP**: listener, Digest (HA1) + Basic (hash), ACL por stream, *splice* a `127.0.0.1:18554`, auditoría, rechazo si `Released`, TLS opcional | `rtsp://…@host:8554/<stream>` funcional en OBS/VLC | F1-09 |
| F1-13 | **Device tokens**: emisión (secreto mostrado una vez), `bcrypt` + HA1, revocación, última IP/uso | CRUD + UI | F1-12 |
| F1-14 | Liberación de productor por inactividad (N segundos sin consumidores) | Worker configurable | F1-09 |

### 4.4 UI (DNX Web UI)

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F1-15 | Shell: `index.html`, pack CSS, `theme-drawer.js`, router hash, sesión, `fetch` con CSRF, toasts/modales | Navegación funcional | F0-01 |
| F1-16 | `player.js`: WebRTC (WHEP) → HLS → MJPEG, reconexión, overlay de estado, zoom digital, audio único | Componente reutilizable | F1-10 |
| F1-17 | Visor: login, rejilla configurable con carga perezosa, pantalla completa, snapshot | Vista `/viewer` | F1-16 |
| F1-18 | Consola: panel, cuentas, cámaras (con perfil y capacidades), usuarios/permisos, tokens, auditoría, ajustes | Vista `/admin` | F1-07, F1-13 |

### 4.5 Transversal

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F1-19 | Auditoría: eventos de login, visualización, RTSP, cambios de configuración | Tabla + consulta + UI | F1-03 |
| F1-20 | Tests: unitarios (auth, tokens, ACL, capacidades), integración (gateway RTSP, proxies) y *smoke* e2e | Cobertura de caminos críticos | F1-12 |
| F1-21 | Documentación: despliegue, firewall, ejemplo Caddy/Authelia, guía OBS, solución de problemas | `docs/` + README | F1-18 |

**Aceptación F1 (verificable):**
1. Alta de cuenta Xiaomi real con 2FA y captcha.
2. Importación de cámaras y visualización en rejilla de 6 sin caídas en 30 min.
3. OBS reproduce por RTSP con token de dispositivo; token revocado → rechazo inmediato.
4. `viewer` no ve cámaras sin permiso (ni por API ni por RTSP).
5. Auditoría registra accesos con usuario, IP y origen (`local`/`sso`).
6. Sin Authelia: aparece login local. Con Authelia: entra directo y conserva permisos.

---

## 5. F2 — Audio y grabación (8–12 d)

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F2-01 | Talk: proxy de *backchannel* WebRTC, *push-to-talk* en UI, bloqueo si `!isSecureContext` con mensaje | Hablar a cámara `miss` | F1-16 |
| F2-02 | Grabador: proceso FFmpeg por cámara sobre RTSP interno, segmentos 2–5 s, `-c copy`, audio AAC | Ficheros reproducibles | F1-09 |
| F2-03 | Anillo en `tmpfs` con cuotas y **tres disparadores** de volcado (evento, pérdida de cámara, SIGTERM) | Sin pérdida en apagado limpio | F2-02 |
| F2-04 | Detección de movimiento: muestreo `frame.jpeg?width=320`, diferencia de frames, umbral y zonas por cámara | Eventos con puntuación | F1-11 |
| F2-05 | Retención: por días/GB, promoción de segmentos con evento, contabilidad de disco | Worker + UI de uso | F2-02 |
| F2-06 | Timeline en visor: reproducción, marcas de evento y huecos por mantenimiento | Vista de grabaciones | F2-05 |
| F2-07 | **Ventana de liberación** (ADR-017): eliminar stream, bloquear reapertura, cuenta atrás, reintentos 30–60 s, "Reconectar ahora", auditoría | Botones en consola | F1-09 |
| F2-08 | Modo CPU (`Lite`/`Auto`/`Calidad`) aplicado a transcode, muestreo y grabación; excepción por cámara | Ajuste global efectivo | F2-02 |

**Aceptación F2:**
1. Movimiento → segmento guardado con 5–15 s previos.
2. Desconectar la cámara → se conservan los últimos segundos del anillo.
3. `docker stop` → volcado completo, sin ficheros corruptos.
4. Liberar 10 min → la app Mi Home conecta; al expirar, reconexión automática.
5. Talk funciona con HTTPS y muestra aviso claro sin él.
6. Ramas ARM: `Lite` no transcodifica y no rompe nada.

---

## 6. F3 — Extras y publicación (5–8 d)

| ID | Tarea | Entregable | Depende |
|---|---|---|---|
| F3-01 | ONVIF: proxy de `/onvif/`, filtrado de perfiles por ACL, reescritura de `GetStreamUri`, credenciales por dispositivo | NVR de terceros ve solo sus cámaras | F1-12 |
| F3-02 | Fallback HLS/MJPEG pulido + matriz de compatibilidad por navegador | Documento + ajustes | F1-11 |
| F3-03 | Framework PTZ (capacidad, API y UI oculta) sin implementación de protocolo; zoom digital avanzado (rueda, doble toque, reset) | Base lista | F1-08 |
| F3-04 | Empaquetado: imágenes multi-arch, etiquetas, notas de release, actualización y copia de seguridad documentadas | Release publicable | F0-10 |
| F3-05 | Endurecimiento final: revisión de dependencias, límites de recursos, prueba de carga | Informe breve | F1-04 |

---

## 7. Orden de ejecución (camino crítico)

```
F0-01 → F0-02 → F0-03 → F0-07 → F0-08 → F0-09
                                     │
F1-01 → F1-02/F1-03                  ▼
            │              F1-05 → F1-07 → F1-08
            │                  │        │
            └──────────────────┴────────┴→ F1-09 → F1-10 → F1-15 → F1-16 → F1-17
                                                    │                    │
                                                    ├→ F1-12 → F1-13 ────┤
                                                    └→ F1-11 ────────────┘
                                                                          ▼
                                                                    F1-18 → F1-19/F1-20/F1-21
```

Regla práctica: **no abrir F2 sin F1 cerrado y probado**, salvo los spikes.

---

## 8. Spikes (paralelos, timebox 1–2 d cada uno)

| ID | Pregunta | Criterio de salida |
|---|---|---|
| S1 | ¿Cuántas sesiones P2P admite cada modelo real? ¿Convive con Mi Home? | Tabla de resultados por modelo; ajusta ADR-017 |
| S2 | ¿Funciona CS2/TUTK con `network_mode: host` en el host objetivo (incluido broadcast)? | Conecta desde Docker en LAN real |
| S3 | ¿WebRTC sin STUN funciona en LAN con `candidates` y proxy delante? | WHEP reproducible en Chrome/Edge/Safari |
| S4 | Coste real de transcode H265→H264 en x86 y ARM | Números de CPU por cámara; ajusta modo `Auto` |
| S5 | Interoperabilidad Digest/Basic con OBS, VLC y ffmpeg | Matriz de clientes soportados |

Los spikes **no bloquean** F0/F1 salvo S2 (afecta al despliegue) y S5 (afecta a F1-12).

---

## 9. Datos y migraciones (orden)

1. `AppUser`, `Setting` (F1-01)
2. `XiaomiAccount`, `Camera` (F1-06, F1-07)
3. `CameraGrant` (F1-03)
4. `DeviceToken` (F1-13)
5. `AuditEvent` (F1-19)
6. `Recording`, `MotionEvent` (F2-05)

Convención: una migración por tarea, revisada antes de aplicar; sin cambios
manuales al esquema en producción.

---

## 10. Configuración (matriz de entorno)

| Variable | Obligatoria | Ejemplo | Uso |
|---|---|---|---|
| `TZ` | sí | `Europe/Madrid` | Horas en logs y grabaciones |
| `LAN_CIDR` | sí | `192.168.1.0/24` | Firewall lógico y validaciones |
| `WEBRTC_CANDIDATE` | sí | `192.168.1.10` | Candidato ICE (sin STUN) |
| `ASPNETCORE_URLS` | sí | `http://0.0.0.0:8080` | Escucha de la app |
| `SSO_HEADER` | no | `Remote-User` | Activa SSO por cabecera |
| `SSO_TRUSTED_PROXIES` | no | `192.168.1.20` | IPs del proxy |
| `GO2RTC_API` | sí | `http://127.0.0.1:1984` | API interna |
| `GO2RTC_RTSP` | sí | `127.0.0.1:18554` | RTSP interno |
| `DATA_DIR` / `REC_DIR` | sí | `/data` `/recordings` | Persistencia |
| `CPU_MODE` | no | `Auto` | `Lite`/`Auto`/`Calidad` |
| `RING_SECONDS` | no | `15` | Anillo de prebuffer |

Secretos: token Xiaomi **solo** en `go2rtc.yaml` (0600). Nunca en variables de
entorno ni en logs.

---

## 11. Testing

| Nivel | Herramienta | Qué cubre |
|---|---|---|
| Unitario | xUnit | Hash/validación de credenciales, HA1, ACL, merge de capacidades, retención |
| Integración | `WebApplicationFactory` + contenedor go2rtc de prueba | Proxies, auth, emisión/revocación de tokens |
| RTSP | Cliente de prueba / ffmpeg en CI | Digest/Basic, rechazo por ACL, `Released` |
| E2E manual | Navegador + OBS | Rejilla, fullscreen, talk, reconexión |
| Carga | Script simple | 20 consumidores sobre una cámara; CPU y memoria |

Comandos: `dotnet build`, `dotnet test`, `docker compose up --build`.

---

## 12. Operación

- **Arranque:** app → go2rtc (supervisado) → workers. Healthcheck del contenedor.
- **Parada:** SIGTERM → volcado de anillos (30 s) → cierre de go2rtc.
- **Copia de seguridad:** `/data/camera.db` + `/data/go2rtc.yaml`; grabaciones según política.
- **Actualización:** sustituir imagen (go2rtc va dentro, versión fijada); migraciones automáticas al arrancar.
- **Diagnóstico:** `/healthz`, logs, panel de estado en consola, `api/streams` de go2rtc solo desde localhost.

---

## 13. Riesgos → mitigación concreta

| Riesgo | Tarea que lo mitiga |
|---|---|
| Compatibilidad por modelo | F1-08 (capacidades), UX de "no soportada", S1 |
| TUTK inestable | F1-14 (liberación por inactividad), backoff del supervisor |
| Cloud Xiaomi cambia | F1-05 encapsulado en un gateway aislado; logs de error claros |
| Internet requerido en handshake | Mensajes de estado y reintentos; documentado |
| Una sesión por cámara | S1 + F2-07 (liberación manual) |
| Docker Desktop no soportado | F0-03 (host network) + documentación explícita |
| go2rtc mantenido por una persona | Versión fijada + checksum + cliente API desacoplado |
| Audio sin TLS | F1-16 (detección) + F2-01 (aviso) |
| Disco lleno por grabación | F2-05 (retención y contabilidad) |

---

## 14. Quality gates por fase

- **F0:** compila en limpio; imagen arranca en `amd64` y `arm64`; `/healthz` OK.
- **F1:** checklist de aceptación de §4.5 al 100 %; sin endpoints sin autorizar;
  auditoría operativa; 30 min de rejilla sin fugas de memoria.
- **F2:** los tres disparadores de volcado probados; retención respeta cuotas;
  talk bloqueado correctamente sin HTTPS.
- **F3:** NVR de terceros conecta con ACL; release reproducible.

---

## 15. Fuera de alcance (recordatorio)

- Exposición a Internet, relay cloud, multi-tenant.
- Ingeniería inversa de PTZ/zoom/IR/presets.
- Grabación en nube, detección por IA, analítica avanzada.
- Aplicaciones nativas (móvil/escritorio).

---

## 16. Primer paso concreto

`F0-01` + `F0-02`: crear la solución, los cuatro proyectos, el
`Directory.Build.props`, el `Dockerfile` multi-stage con go2rtc fijado y el
`compose.yaml` con host network. Con eso el proyecto deja de ser documentación y
pasa a ser ejecutable.

---

## 17. Estado de ejecución (2026-11-13)

### Completado
- **F0-01** Solución `DNXCameraServer.slnx` + Domain/Data/Web/Tests.
- **F0-02/03** Dockerfile multi-stage **Alpine** con go2rtc 1.9.15 fijado por
  checksum y `compose.yaml` con `network_mode: host`, volúmenes y healthcheck.
- **F0-04/05/06** Configuración por entorno validada, logging estructurado,
  `/healthz` y `/readyz`. Claves de DataProtection persistidas en `/data`.
- **F0-07** EF Core + SQLite (WAL) + migración `Initial`.
- **F0-08/09** Supervisor de go2rtc (config generada, arranque, watchdog, parada
  ordenada) y cliente tipado de su API.
- **F1-01/02/03** Login local (cookie, hash, bloqueo), SSO por cabecera con
  fallback, RBAC + ACL por cámara.
- **F1-05/06/07/08** Gateway Xiaomi (contraseña/2FA/captcha), cuentas, catálogo,
  importación y registro de capacidades con 11 fichas de modelo.
- **F1-09/10/11** Ciclo de vida de streams, proxy WHEP, HLS, MJPEG y snapshots.
- **F1-12/13** Gateway RTSP con Digest/Basic, ACL, auditoría y rechazo si la
  cámara está liberada; tokens de dispositivo revocables.
- **F1-15/16/17/18** UI DNX Web UI: login, panel, cuentas, cámaras, usuarios y
  permisos, tokens, auditoría, ajustes, visor con rejilla, pantalla completa,
  zoom digital y audio.
- **F1-19** Auditoría en base de datos con consulta desde consola.
- **F1-20** 14 pruebas unitarias (Digest, contraseñas, capacidades, ACL).
- **F1-21** `README`, `docs/DEPLOY.md`, `docs/OBS.md`, `docs/DEV.md`.
- **F0-10** CI en `.github/workflows/ci.yml` (build + test + multi-arch).
- **F1-04** Endurecimiento: límite de 10 intentos/min por IP en login y cabeceras
  de seguridad (CSP, nosniff, X-Frame-Options, Referrer-Policy).
- **F1-14** Sincronización de estado cada 15 s (`StatusSyncWorker`); go2rtc ya
  detiene el productor cuando no quedan consumidores.
- **Despliegue real** en un host Linux de la LAN,
  imagen `dnxcameraserver:0.1.0`, servicio healthy, go2rtc activo.

### Pendiente inmediato
- **S1–S5** Spikes con cámaras reales.

### Fase 2 y 3
- Talk, grabador con anillo RAM, detección de movimiento, timeline, ventana de
  liberación completa (hoy: botón con reintento manual), ONVIF y framework PTZ.
