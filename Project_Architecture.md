# DNX Camera Server — Arquitectura (SSoT)

> **Estado:** v0.1 — sesión de arquitectura 2026-11-13
> **Fuente de verdad** de decisiones técnicas. Cualquier cambio de arquitectura se
> refleja aquí y se anota como ADR.
> **Plan de ejecución:** `IMPLEMENTATION_PLAN.md` (fases F0–F3, tareas y criterios
> de aceptación).
> **Base tecnológica de medios:** `source_base/go2rtc-master` (v1.9.15, MIT).

---

## 1. Objetivo y alcance

Servidor web autoalojado que conecta cuentas de **Xiaomi Mi Home**, publica sus
cámaras en la **LAN** y ofrece:

1. **Consola de administración** (rol `admin`): cuentas Xiaomi, cámaras, usuarios,
   permisos y credenciales de dispositivo.
2. **Visualizador** (rol `viewer`): rejilla multicámara, pantalla completa, audio,
   snapshot y, si el modelo lo soporta, control bidireccional.
3. **Exportación como cámara IP** en LAN: RTSP autenticado por dispositivo
   (OBS, VLC, NVR, etc.).

### Dentro del alcance
- Autenticación Xiaomi (usuario/contraseña, verificación 2FA, captcha, varias
  cuentas y regiones).
- Catálogo de cámaras con **capacidades reales por modelo** (solo las que existan).
- Streaming LAN: WebRTC (visor web), RTSP (dispositivos), HLS/MJPEG (compatibilidad),
  snapshot JPEG.
- Usuarios `viewer` con permisos por cámara.
- Auditoría de accesos y visualizaciones.

### Fuera del alcance (explícito)
- **Exposición a Internet.** Nada se publica fuera de la LAN. El acceso remoto es
  responsabilidad del usuario (VPN propia).
- **Ingeniería inversa** de protocolos Xiaomi para descubrir funciones no
  implementadas (PTZ, zoom óptico, IR, presets…).
- Multi-tenant / SaaS.
- Grabación en fase 1 (queda diseñada, ver §9).
- ONVIF en fase 1 (queda diseñado, ver §10).

### Límite funcional conocido y aceptado
go2rtc **no implementa PTZ ni zoom** en ninguna fuente. Hoy las capacidades
reales son: códecs, audio, audio bidireccional (`miss`), calidad HD/SD,
multilente y snapshot. El producto **no anunciará** lo que no puede ejecutar.
El zoom digital se resuelve **en el cliente** (ver ADR-006).

---

## 2. Decisiones (ADR)

### ADR-001 — go2rtc como plano de medios, sin fork
**Decisión:** usar el binario de go2rtc (versión fijada) como sidecar; no se
modifica su código en v1.
**Motivo:** resuelve login cloud, P2P (TUTK/CS2), códecs, fan-out, reconexión,
RTSP/WebRTC/HLS/ONVIF y audio bidireccional. Reimplementarlo serían ~5k líneas de
cripto y P2P ya probadas (y violaría la regla de no reinventar funcionalidad).
**Consecuencia:** dependemos de su API y de su modelo de auth (un único
usuario/contraseña RTSP) → se compensa con ADR-004.

### ADR-002 — ASP.NET Core 10 (API + workers) y UI estática vanilla (DNX Web UI)
**Decisión:** una aplicación ASP.NET Core 10 que sirve:
- **API REST** (auth, catálogo, tokens, proxy de señalización, auditoría).
- **Workers** (`IHostedService`): grabador, detección, health, watchdog.
- **Gateway RTSP**.
- **UI estática** en `wwwroot/`: HTML + CSS + JS sin framework y **sin build**,
  sobre el sistema de diseño **DNX Web UI** (skill `dnx-web-ui`, assets ya en el
  repo), con router por hash y módulos JS por sección.
**Motivo:** el pack de diseño es explícitamente *no-framework, no-build*; Blazor
obligaría a duplicar estado entre circuito y DOM y a mezclar dos modelos de
interacción. El visor (WebRTC, rejilla, fullscreen) es JS de todas formas, y el
panel de administración es CRUD + tablas, justo lo que el pack ya resuelve.
**Consecuencia:** todo el backend es .NET; el frontend es vanilla y se puede
reemplazar por SPA en el futuro sin tocar la API. Se gana simplicidad operativa
(no hay toolchain Node ni compilación de UI).

### ADR-003 — Un contenedor, `network_mode: host`, base Alpine
**Decisión:** contenedor Linux único con la app .NET + binario go2rtc, ejecutado
con red de host. Imagen base **Alpine (musl)**, multi-arch `amd64`/`arm64`.
**Motivo:** P2P de Xiaomi exige UDP con IP/puerto reales del host (TUTK remoto por
`uid`, CS2 con *LAN search* por broadcast) y WebRTC necesita candidatos ICE
alcanzables. El NAT de bridge rompe descubrimiento y obliga a publicar rangos UDP.
Además, app y go2rtc comparten `127.0.0.1`, lo que habilita ADR-004. Alpine reduce
la imagen ~45 % frente a Debian y disminuye la superficie de ataque; es viable
porque el binario de go2rtc es **estático** (verificado: `ldd` → *not a dynamic
executable*) y `SQLitePCLRaw` publica nativos `linux-musl-*`.
**Consecuencia:** `-p` no aplica; el firewall del host es la única frontera
(documentado). Se instala `icu-libs` (globalización) y se mantiene `curl` para el
healthcheck. Si en el futuro se necesita un componente con dependencia glibc, se
cambia solo la imagen base, no la arquitectura. **Docker Desktop (Windows/macOS)
no es objetivo soportado** para producción: usar host Linux (mini-PC, NAS, RPi) o
VM Linux.

### ADR-004 — Gateway RTSP propio para auth y ACL por dispositivo
**Decisión:** la app escucha RTSP en `:8554`; valida credenciales (Digest/Basic)
contra sus *device tokens*, aplica ACL por cámara y hace *splice* TCP a
`127.0.0.1:18554` (go2rtc, que exime a loopback de auth).
**Motivo:** go2rtc solo admite **un** usuario/contraseña global para todos los
streams (`internal/rtsp/rtsp.go:73-75`), incompatible con credenciales por
dispositivo, revocación y auditoría. Un proxy de bytes evita fork y evita el
problema de auth sin tocar Go.
**Consecuencia:** se implementa Digest (HA1) y Basic (hash lento) con
credenciales emitidas una sola vez; TLS opcional (`rtsps`) sobre `SslStream`.

### ADR-005 — LAN-only; HTTPS no se exige, se degrada
**Decisión:** la app **no impone TLS**. Funciona igual detrás de un reverse proxy
(Caddy/Traefik/Authelia) que en HTTP plano. Si el navegador no está en contexto
seguro, **todo funciona salvo el micrófono** (audio bidireccional), que se
deshabilita con un mensaje explicativo. RTSP en claro dentro de la LAN; TLS
opcional. Sin *port forwarding*.
**Motivo:** el usuario decide su infraestructura; el servidor no debe romperse por
no tener certificado. `getUserMedia` es la única función que el navegador ata al
contexto seguro.
**Consecuencia:** se documenta el flag de Chrome/Edge para orígenes inseguros
(`--unsafely-treat-insecure-origin-as-secure=http://host:puerto`) como vía de
pruebas, sin soporte oficial. La detección se hace en cliente
(`window.isSecureContext`) y la UI se adapta.

### ADR-006 — Capacidades basadas en evidencia; zoom digital en cliente
**Decisión:** modelo de capacidades con tres fuentes en cascada (ficha de modelo
curada → probe en runtime → override manual). `ptz` y `zoom` existen en el
esquema pero hoy siempre `false`. Zoom digital (recorte/escala en el elemento de
vídeo) es una función del visor, no de la cámara, y se etiqueta como tal.
**Motivo:** no se inventan capacidades; el esquema queda preparado para que un
modelo concreto las active si algún día se implementan.
**Consecuencia:** la UI oculta controles no soportados en lugar de mostrarlos
deshabilitados sin explicación.

### ADR-007 — Modo CPU **global** con excepción por cámara
**Decisión:** el control de coste es **del servicio**, no de la cámara. Un ajuste
global (`Lite` | `Auto` | `Calidad`) define el comportamiento de **todos** los
consumidores de CPU: transcoding, muestreo de movimiento, snapshots y grabación.
Por cámara solo existe una **excepción** (forzar/denegar transcode) para casos
puntuales.
**Motivo:** el hardware es uno y compartido; en una RPi no tiene sentido que cada
cámara decida. El ajuste global es predecible y explicable al usuario.
**Consecuencia:**
- `Lite` (por defecto en ARM/CPU débil): solo *passthrough*, sin transcode, sin
  grabación en continuo, muestreo de movimiento mínimo.
- `Auto` (por defecto en x86): *passthrough* + transcode **solo bajo demanda**
  (cliente que no soporta el códec) con detección de hardware al arrancar.
- `Calidad`: transcode permitido siempre, muestreo y grabación completos.
- Cambiar de perfil de vídeo **reabre** la sesión P2P (corte de 2–5 s), avisado en UI.

### ADR-008 — Reparto de protocolos
**Decisión:** WebRTC (WHEP a través de la app) para el visor web; RTSP para
dispositivos; HLS/MJPEG como *fallback*; snapshot JPEG para rejilla y carátulas.
**Motivo:** WebRTC es el único que da latencia baja y audio bidireccional en
navegador; RTSP es el estándar que OBS/VLC/NVR esperan.
**Consecuencia:** WebRTC necesita `webrtc.candidates` con la IP LAN y sin STUN
público (privacidad y fiabilidad en LAN).

### ADR-009 — Grabación en fase 2: segmentos *copy* + índice de eventos
**Decisión:** grabador con FFmpeg sobre el RTSP interno, `-c copy`, segmentos de
1 minuto (fMP4 con audio AAC o MKV con copia de audio), más índice en base de
datos. Detección de movimiento por software (muestreo de `/api/frame.jpeg` a baja
resolución). Retención configurable.
**Motivo:** copiar sin recodificar es barato y robusto; no existen eventos
nativos de cámara accesibles sin ingeniería inversa.
**Consecuencia:** grabar implica mantener la sesión P2P abierta 24/7, lo que
puede impedir el uso simultáneo de la app Mi Home. Se documenta.

### ADR-010 — ONVIF como exportación opcional de fase 3
**Decisión:** no se implementa ahora. Cuando se haga, se proxifica `/onvif/` de
go2rtc por la app, filtrando perfiles por dispositivo y mapeando credenciales.
**Motivo:** go2rtc ya implementa el servicio SOAP (descubrimiento, perfiles,
`GetStreamUri`), y **no anuncia PTZ** (`pkg/onvif/server.go:262-266`), así que no
compromete el mensaje del producto. El coste es de integración, no de protocolo.
**Consecuencia:** el esquema de nombres de stream debe ser estable desde v1.

### ADR-011 — go2rtc es dueño del token Xiaomi
**Decisión:** el token de cuenta lo gestiona y persiste go2rtc en su YAML
(volumen con permisos `0600`). La app guarda solo metadatos de la cuenta (userId,
región, alias) y nunca lee ni replica el token.
**Motivo:** una sola fuente de verdad evita desincronización y duplica menos
superficie de fuga.
**Consecuencia:** borrar una cuenta = eliminar su entrada en go2rtc vía API y su
metadata en la BD.

### ADR-012 — Observabilidad mínima
**Decisión:** logs estructurados a stdout y fichero rotativo, `/healthz` con
estado de go2rtc y de cada productor, watchdog que reinicia el sidecar, y
auditoría en BD.
**Motivo:** el P2P de Xiaomi es inestable por naturaleza; sin telemetría el
soporte es imposible.

### ADR-013 — Sin límites de rejilla; el usuario decide
**Decisión:** no se impone máximo de cámaras, de mosaicos ni de calidad. Tampoco
se muestran advertencias que condicionen: el usuario configura tamaño de rejilla,
perfil (HD/SD) y lo que su hardware soporte. La UI solo ofrece **diagnóstico
opcional** (fps, bitrate, jitter) sin emitir juicios ni bloquear.
**Motivo:** el coste real está en el cliente (decodificadores simultáneos, GPU,
ancho de banda) y solo el usuario conoce su hardware. Limitar por defecto sería
arbitrario y paternalista.
**Consecuencia:** se implementa rejilla **perezosa** (solo se negocian/decodifican
los *tiles* visibles, pausa fuera de viewport) como optimización **silenciosa**,
no como restricción. Documentación de dimensionado aparte, sin enforcement.

### ADR-014 — Anillo en RAM con volcado a disco por tres disparadores
**Decisión:** el grabador mantiene por cámara un **anillo de segmentos cortos
(2–5 s) en `tmpfs`**, con volcado ("promoción") a almacenamiento definitivo ante
**tres disparadores**:
1. **Evento de movimiento** → se guardan los segundos previos + los posteriores.
2. **Pérdida de la cámara** (EOF/error del productor) → se vuelca lo que había en
   el anillo, preservando los últimos segundos antes de la desconexión (robo,
   desenchufe, caída de red).
3. **Parada ordenada del servicio** (SIGTERM, con margen de 10–30 s) → se vuelca
   todo. Con batería/UPS en el host, un corte de luz permite cerrar y no perder
   nada.
**Motivo:** el anillo en RAM evita escrituras continuas y desgaste de disco, y los
tres disparadores cubren los casos en que esos datos tienen valor.
**Consecuencia:**
- Coste de memoria bajo: 30 s × 2 Mbps ≈ **7,5 MB por cámara** (20 cámaras ≈ 150 MB).
- El segmento en curso puede quedar truncado al volcar; con segmentos de 2–5 s la
  pérdida máxima es acotada.
- Sin batería y con corte abrupto, el anillo se pierde (aceptado).
- El volcado concurrente de muchas cámaras tras un apagón es acotado por el tamaño
  del anillo.
- Opción de anillo en **disco** para quien prefiera sobrevivir a cortes sin UPS;
  implica escritura continua (equivalente a grabación continua circular).

### ADR-015 — ONVIF comprometido (fase 3)
**Decisión:** se implementa la exportación ONVIF con ACL por dispositivo
(proxificando `/onvif/` y filtrando perfiles). Entra en el roadmap como entrega
comprometida, no como "quizá".
**Motivo:** coste contenido (go2rtc ya hace el SOAP) y valor para terceros (NVRs).
**Consecuencia:** condiciona que el nombre de stream sea estable y que el gateway
RTSP acepte credenciales de dispositivo (ya previsto en ADR-004).

### ADR-016 — SSO por cabecera **oportunista**, con login local de respaldo
**Decisión:** el modo SSO se **activa** para el despliegue con Authelia. La regla:
- Si la petición llega **desde la IP del proxy** (lista de confianza) y trae
  `Remote-User`, se autentica por SSO.
- En cualquier otro caso —cabecera ausente, IP no confiable o **Authelia
  deshabilitado**— se muestra el **login local** de la aplicación.
- El usuario SSO debe existir como cuenta local (sin auto-provisión); sus
  permisos por cámara siguen siendo nuestros.
**Motivo:** se elimina el doble login cuando hay SSO, y si el usuario apaga el
forward-auth el sistema sigue siendo utilizable sin reconfigurar nada.
**Consecuencia:** la cabecera **nunca** se acepta desde la LAN; si el proxy deja
de enviarla, no hay error, hay *fallback* a login local. Ambas vías se auditan con
su origen (`sso`/`local`).

### ADR-017 — Ventana de mantenimiento por cámara ("liberar 10 minutos")
**Decisión:** botón por cámara que **libera la sesión P2P** durante una ventana
configurable (10 min por defecto):
- Detiene el productor y **bloquea** su reapertura (se elimina el stream de
  go2rtc y se marca la cámara como `Released`).
- La UI muestra cuenta atrás y estado "liberada para la app Mi Home".
- Al terminar, se reintenta conectar; si falla, reintentos cada 30–60 s con
  backoff y estado "no disponible".
- Botón **"Reconectar ahora"** para cancelar la espera y forzar el intento.
- Toda la acción queda auditada.
**Motivo:** no podemos detectar si el móvil está usando la cámara; solo podemos
ceder el turno y reintentar. Es la única forma fiable de convivir con la app.
**Consecuencia:** durante la ventana **no hay grabación ni visualización** de esa
cámara (asumido). El mensaje de error no debe afirmar la causa: "no se pudo
conectar (posible uso simultáneo de la app Mi Home)".

### ADR-018 — Alta inicial desde la web y reseteo por CLI
**Decisión:** no se generan contraseñas. Mientras no exista ningún `Admin`, la
aplicación está en **modo instalación**: la web muestra un formulario para crear
el administrador y `POST /api/setup/admin` es el único endpoint de escritura
anónimo (y se cierra en cuanto hay un admin). El flag `--reset-admin-password`
(o `RESET_ADMIN_PASSWORD=true`) elimina los administradores y sus permisos, y
sale sin arrancar el servidor; la web vuelve a pedir crear uno.
**Motivo:** una contraseña generada y guardada en disco es un secreto que el
usuario no conoce y que se rompe en cuanto se pierde el fichero. El alta
explícita es predecible y auditable.
**Consecuencia:** el endpoint de instalación está limitado por rate-limit y por
la condición "sin administradores"; los visores, cámaras y tokens se conservan
al resetear. El reset exige acceso al host (Docker), no a la web.

---

## 3. Componentes

```
                        LAN
   ┌──────────────┬──────┴────────┬───────────────┬──────────────┐
   │ Navegador    │ OBS / VLC     │ NVR (fase 3)  │ Móvil        │
   │ (viewer)     │ (RTSP)        │ (ONVIF)       │ (HLS)        │
   └──────┬───────┴───────┬───────┴───────┬───────┴──────┬───────┘
          │ HTTPS/WSS     │ RTSP          │ HTTP/SOAP    │ HTTPS
┌─────────▼───────────────▼───────────────▼──────────────▼────────┐
│  DNX Camera Server (ASP.NET Core 10, host network)              │
│  ┌───────────────┬────────────────┬──────────────┬────────────┐ │
│  │ UI estática   │ API REST       │ RTSP Gateway │ Grabador   │ │
│  │ (DNX Web UI)  │ auth, CRUD     │ Digest+ACL   │ (fase 2)   │ │
│  ├───────────────┴────────────────┴──────────────┴────────────┤ │
│  │ Servicios: XiaomiGateway · CameraRegistry · Capabilities    │ │
│  │            · Health · Audit · Motion (fase 2)               │ │
│  ├─────────────────────────────────────────────────────────────┤ │
│  │ EF Core + SQLite (metadatos, usuarios, tokens, auditoría)   │ │
│  └─────────────────────────────────────────────────────────────┘ │
│            │ 127.0.0.1:1984 (API)   :18554 (RTSP)                │
│  ┌─────────▼──────────────────────────────────────────────────┐  │
│  │ go2rtc (sidecar, binario fijado)                           │  │
│  │  P2P TUTK/CS2 · fan-out · RTSP · WebRTC :8555 · snapshot    │  │
│  └─────────┬──────────────────────────────────────────────────┘  │
└────────────┼─────────────────────────────────────────────────────┘
             │ UDP P2P (LAN)  +  HTTPS (cloud Xiaomi, solo claves)
        ┌────▼─────┐   ┌──────────┐
        │ Cámara 1 │   │ Cámara N │
        └──────────┘   └──────────┘
```

### Responsabilidades
| Componente | Hace | No hace |
|---|---|---|
| UI estática (DNX Web UI) | Login, consola, visor, rejilla, fullscreen | Tocar vídeo |
| API REST | Contratos, RBAC, tokens, proxy de señalización | Hablar P2P |
| RTSP Gateway | Auth por dispositivo, ACL, splice TCP | Entender RTP |
| go2rtc | P2P, códecs, fan-out, reconexión, snapshot | Usuarios, permisos |
| Grabador | Segmentos, retención, índice | Detectar movimiento (lo hace Motion) |

---

## 4. Puertos y red

| Puerto | Proceso | Bind | Uso |
|---|---|---|---|
| 8080/tcp | app | `0.0.0.0` | HTTP: UI, API, WHEP, snapshots (TLS lo aporta el proxy, si existe) |
| 8554/tcp | app | `0.0.0.0` | RTSP autenticado (consumidores) |
| 8555/udp | go2rtc | `0.0.0.0` | WebRTC (media) |
| 8555/tcp | go2rtc | `0.0.0.0` | WebRTC sobre TCP (fallback) |
| 1984/tcp | go2rtc | `127.0.0.1` | API de control (**nunca** expuesta) |
| 18554/tcp | go2rtc | `127.0.0.1` | RTSP interno (solo gateway) |

Reglas de firewall del host: permitir el puerto de la app (`8080` o el del proxy),
`8554` y `8555/udp` **solo** desde la subred LAN. Salida: HTTPS a `*.mi.com`
(handshake) y UDP saliente P2P.

---

## 5. Modelo de datos (SQLite)

```
AppUser        Id, Username, PasswordHash, Role(Admin|Viewer), IsActive,
               CreatedAt, LastLoginAt

XiaomiAccount  Id, XiaomiUserId, Region, Alias, CreatedAt     (token: en go2rtc)

Camera         Id, AccountId, Did, Model, Name, Ip, Protocol(Legacy|Miss),
               Channel, Profile(Native|Hd|Sd|Auto), Transcode(None|H264),
               Enabled, CapabilitiesJson, CreatedAt, LastSeenAt

CameraGrant    UserId, CameraId, CanView, CanListen, CanTalk

DeviceToken    Id, CameraId, Name, SecretHash, DigestHa1, CreatedAt,
               LastUsedAt, LastIp, ExpiresAt, RevokedAt

AuditEvent     Id, At, ActorType(User|Device), ActorId, CameraId, Action,
               Ip, UserAgent, DetailJson

Recording      Id, CameraId, Path, StartedAt, DurationSec, SizeBytes,
               Trigger(Continuous|Motion|Manual), State          (fase 2)

MotionEvent    Id, CameraId, StartedAt, EndedAt, Score          (fase 2)

Setting        Key, Value
```

Índices: `Camera(AccountId)`, `CameraGrant(UserId)`, `DeviceToken(CameraId)`,
`AuditEvent(At)`, `Recording(CameraId, StartedAt)`.

---

## 6. Contratos de API (control plane)

```
GET    /api/setup/status           (anónimo; indica si falta el administrador)
POST   /api/setup/admin            (anónimo; solo sin administradores)

POST   /api/auth/login              POST /api/auth/logout      GET /api/auth/me
POST   /api/auth/password           (cambio de contraseña propio)

POST   /api/xiaomi/login            {username,password} → ok | captcha | verify
POST   /api/xiaomi/token            {userId,token,region} → alta por passToken
POST   /api/xiaomi/login/captcha    POST /api/xiaomi/login/verify
GET    /api/xiaomi/accounts         DELETE /api/xiaomi/accounts/{id}
GET    /api/xiaomi/devices?account= (lista con modelo, IP, did)

GET    /api/cameras                 POST /api/cameras            (importar)
GET    /api/cameras/{id}            PATCH /api/cameras/{id}      DELETE
GET    /api/cameras/{id}/capabilities
POST   /api/cameras/{id}/profile    {profile}
GET    /api/cameras/{id}/snapshot?w=

POST   /api/cameras/{id}/webrtc/offer   (señalización; proxya a go2rtc)
GET    /api/cameras/{id}/hls.m3u8       (fallback)
GET    /api/cameras/{id}/mjpeg          (fallback)

GET    /api/users                   POST /api/users            PATCH /api/users/{id}
GET    /api/users/{id}/grants       PUT  /api/users/{id}/grants
GET    /api/device-tokens           POST /api/device-tokens     DELETE …/{id}

GET    /api/audit?from=&to=&camera= GET /api/health
```

RTSP de dispositivo: `rtsp://<tokenId>:<secreto>@<host>:8554/<streamName>`.
El secreto se muestra una sola vez.

---

## 7. Seguridad

| Riesgo | Control |
|---|---|
| Acceso no autorizado a la UI | Cookie de sesión HttpOnly + SameSite=Strict, hash Argon2id/bcrypt, bloqueo por intentos, CSRF en formularios |
| Escalada de privilegios | RBAC fijo (`Admin`, `Viewer`) + ACL por cámara y acción |
| RTSP abierto | Gateway con Digest/Basic por dispositivo, ACL por stream, revocación inmediata, auditoría |
| Sniffing LAN | HTTPS si el usuario lo aporta (reverse proxy); RTSP sobre TLS opcional; en HTTP plano el tráfico de UI viaja visible (aceptado en LAN) |
| Fuga del token Xiaomi | Vive solo en el YAML de go2rtc con `0600`; jamás en logs ni respuestas |
| Enumeración de streams | Nombres de stream no adivinables (`cam_<16 bytes base32>`) y UI que no los expone |
| Fuerza bruta RTSP | Retardo progresivo + bloqueo por IP tras N fallos |
| Exposición accidental | go2rtc API en loopback; documentos de firewall; `/healthz` sin datos sensibles |

Contraseñas de dispositivo: se almacenan `bcrypt(secreto)` para Basic y
`HA1 = MD5(id:realm:secreto)` para Digest; el secreto en claro no se persiste.

---

## 8. Capacidades y perfiles

**Modo CPU del servicio** (`Lite` | `Auto` | `Calidad`, ADR-007) — ajuste global
que gobierna transcoding, muestreo de movimiento y grabación. Excepción por
cámara solo para forzar/denegar transcode.

```jsonc
{
  "video": { "codecs": ["h264","h265"], "lenses": 1 },
  "audio": { "out": true, "talk": true },
  "quality": { "profiles": ["hd","sd","auto"], "native": "hd", "hdIndex": 3 },
  "snapshot": true,
  "wakeup": false,
  "ptz": false,        // sin implementación en go2rtc
  "zoom": { "optical": false, "digitalInViewer": true }
}
```

- **Ficha curada** por modelo en `data/models/*.json` (versionada).
- **Probe** en el primer arranque del productor (códec y audio reales).
- **Override** manual desde la consola, marcado como tal.
- La UI del visor se genera a partir de las capacidades efectivas.

---

## 9. Grabación y eventos (fase 2)

**Dos políticas, ambas soportadas:**

| Política | Comportamiento | Uso |
|---|---|---|
| **Continuo con retención** | Se graba siempre; la retención borra por antigüedad/espacio y conserva más los tramos con evento | Simple y no pierde nada; más disco |
| **Solo evento** | Anillo en RAM/disco + promoción al detectar movimiento, pérdida de cámara o parada del servicio | Ahorra disco; conserva el "antes" |

- **Fuente:** RTSP interno (`127.0.0.1:18554`) → no añade sesión P2P nueva.
- **Formato:** segmentos de **2–5 s**, `-c copy`; audio AAC (transcode barato) en
  fMP4, o MKV con copia si se prioriza CPU cero.
- **Anillo:** `tmpfs` por defecto (10–30 s, ≈7,5 MB/cámara a 2 Mbps), opción a disco.
- **Volcado:** evento de movimiento · pérdida de cámara (EOF del productor) ·
  SIGTERM con margen de 10–30 s (ADR-014).
- **Movimiento:** muestreo de `/api/frame.jpeg?width=320` a 1–2 fps, diferencia de
  frames + umbral configurable; **no hay eventos nativos de cámara**.
- **Retención:** por días y por GB, por política.
- **Reproducción:** timeline en el visor con salto a evento.
- **Convivir con Mi Home:** ventana de liberación por cámara (ADR-017) con
  cuenta atrás, reintentos y "Reconectar ahora".
- **Riesgo asumido:** grabar mantiene la sesión P2P abierta de forma continua
  (ver §13.5) y el anillo en RAM se pierde con corte abrupto sin batería/UPS.

---

## 10. ONVIF (fase 3, opcional)

- Se proxifica `/onvif/` de go2rtc con credenciales por dispositivo.
- Se filtran los perfiles devueltos por `MediaGetProfiles` según la ACL (la app
  reescribe la respuesta SOAP).
- `GetStreamUri` se reescribe al host/puerto del gateway RTSP.
- No se anuncia PTZ (go2rtc no lo implementa). Sin impacto en v1/v2.

---

## 11. Despliegue

```yaml
services:
  camera-server:
    image: dnxcameraserver:0.1.0
    network_mode: host
    restart: unless-stopped
    user: "1000:1000"         # mismo UID que el dueño de ./data
    cap_add: [NET_BIND_SERVICE]
    volumes:
      - ./data:/data            # sqlite, go2rtc.yaml, certs
      - ./recordings:/recordings
    environment:
      - TZ=Europe/Madrid
      - LAN_CIDR=192.168.1.0/24
      - WEBRTC_CANDIDATE=192.168.1.10
      - ASPNETCORE_URLS=http://0.0.0.0:8080
      - SSO_HEADER=Remote-User
      - SSO_TRUSTED_PROXIES=192.168.1.20    # IP de Caddy/Authelia
```

- Con reverse proxy delante (Caddy/Authelia) este termina TLS y reenvía a
  `:8080`; sin proxy, se publica `:8080` y el audio bidireccional queda degradado.
- Si Authelia no envía cabeceras, la app cae a **login local** (ADR-016).

- `/data` contiene `camera.db`, `go2rtc.yaml` (0600), `certs/`.
- Multi-arch `amd64`/`arm64`; en ARM sin encoder v4l2 se fuerza perfil `native`.
- Healthcheck del contenedor vía `/healthz`.

---

## 12. Roadmap

| Fase | Contenido |
|---|---|
| **F1 — MVP** | Login Xiaomi (2FA/captcha), catálogo, capacidades, visor (rejilla perezosa/fullscreen/snapshot), RTSP gateway con tokens, RBAC, auditoría, health, modo CPU global |
| **F2 — Audio y grabación** | Talk (WebRTC, degradado sin TLS), grabador (continuo y solo-evento) + detección de movimiento + anillo RAM con volcado por evento/desconexión/parada + retención + timeline + ventana de liberación por cámara |
| **F3 — Extras** | ONVIF con ACL, HLS/MJPEG pulido, zoom digital avanzado, framework PTZ listo para modelos futuros |

---

## 13. Riesgos y límites conocidos

1. **Compatibilidad por modelo**: lista viva upstream (go2rtc issue #1982). UX de
   "cámara no soportada", nunca fallo silencioso.
2. **TUTK**: inestable (reconocido por el propio autor); backoff mitiga, latencia
   no.
3. **Dependencia de API no oficial de Xiaomi**: cambios de cloud, captchas o
   bloqueo de cuenta son riesgo de negocio.
4. **Internet necesario en cada handshake** (claves de cifrado).
5. **Una sesión P2P por cámara**: conflicto **posible** (no seguro) con la app Mi
   Home según firmware; muchos modelos admiten 2–3 clientes. La grabación continua
   mantiene la sesión tomada. Mitigación: liberar la sesión al quedar sin
   consumidores, no precargar por defecto y **ventana de liberación manual**
   (ADR-017) para ceder el turno a la app.
6. **Host networking obligatorio** → Docker Desktop no soportado en producción.
7. **go2rtc es mantenido por una persona**: versión fijada y plan de fork (solo
   auth por stream) si hace falta.
8. **Audio bidireccional** exige contexto seguro (HTTPS de confianza o `localhost`).
   Sin él, la función se deshabilita con un mensaje explícito; el resto no se ve
   afectado.

---

## 14. Estructura del repositorio (propuesta)

```
src/
  Dnx.CameraServer.Web/            ASP.NET Core: API + workers + RTSP gateway
    wwwroot/
      index.html                   shell (DNX Web UI)
      css/dnx-web-ui.css           pack de diseño (copia de la skill)
      js/theme-drawer.js           tema + drawer (pack)
      js/app.js                    router hash + sesión + fetch
      js/views/*.js                panel, cuentas, cámaras, usuarios, tokens…
      js/player.js                 WebRTC/HLS/MJPEG + talk + zoom digital
      assets/logo.png
  Dnx.CameraServer.Domain/         Entidades, capacidades, contratos
  Dnx.CameraServer.Data/           EF Core + SQLite
  Dnx.CameraServer.Tests/
data/models/*.json                 Fichas de capacidades por modelo
docker/                            Dockerfile, compose, plantilla go2rtc.yaml
source_base/go2rtc-master/         Referencia upstream (no se compila en el build)
```

---

## 15. Decisiones diferidas

- TLS: lo aporta el usuario (reverse proxy) o se accede en HTTP con audio
  bidireccional degradado. No se gestionan certificados en esta versión.
- Formato de grabación definitivo (fMP4+AAC vs MKV copia), tamaño de segmento y
  política por defecto (continuo vs solo-evento).
- Duración por defecto de la ventana de liberación (10 min) y backoff de reintentos.

---

## 16. UI — sistema de diseño DNX Web UI

**Fuente:** implementación de referencia del sistema de diseño **DNX Web UI**,
incluida en `wwwroot/css/dnx-web-ui.css` y `wwwroot/js/theme-drawer.js` (copias de
trabajo del pack). Reglas duras:

- **Sin framework y sin build**: HTML + CSS + JS vanilla. Cero dependencias.
- **Tokens primero**: colores y medidas solo desde variables CSS (`:root` /
  `:root[data-theme]`). Nada de hex sueltos en componentes.
- **Tema en 3 estados** (auto → oscuro → claro) con botón cíclico, persistido en
  `localStorage` (`dnx_theme`) y script anti-FOUC en `<head>`.
- **Shell**: sidebar fija (228px) con drawer en ≤820px, topbar sticky con sangrado,
  `100dvh` + `env(safe-area-inset-*)`, `viewport-fit=cover`.
- **Componentes**: `card`, `stat`, `badge`, `dot/LED`, `btn` (+`ghost`/`danger`/`sm`),
  tabla (`.table-wrap`, `.col-hide`), `switch`, `modal`, `toast`, `avatar`, `bar`.
- **Accesibilidad**: `aria-label`/`title` en botones-emoji y foco visible.

### Tokens base
| | Oscuro (defecto) | Claro |
|---|---|---|
| `--bg` | `#0b0f14` | `#f4f7fb` |
| `--panel` | `#121821` | `#ffffff` |
| `--line` | `#263243` | `#e2e8f0` |
| `--text` | `#e8eef6` | `#0f172a` |
| `--accent` | `#3b82f6` | `#3b82f6` |
| `--ok` / `--warn` / `--err` | `#22c55e` / `#f59e0b` / `#ef4444` | igual |

Tipografía `Segoe UI`, base 14px/1.45, mono `Cascadia Code` para IDs y hashes.

### Pantallas
**Consola (`/admin`, rol Admin)**
| Vista | Contenido |
|---|---|
| Instalación | Solo si no existe Admin: creación del primer administrador (sin contraseñas por defecto) |
| Panel | estado del servicio, productores activos, CPU/modo, disco, errores recientes |
| Cuentas Xiaomi | alta con 2FA/captcha, regiones, borrado, estado del token |
| Cámaras | importar del catálogo, nombre, IP, modelo, capacidades, perfil HD/SD, liberar/reconectar (ADR-017) |
| Usuarios | alta/edición, rol, permisos por cámara (`ver`/`oír`/`hablar`) |
| Dispositivos | tokens RTSP por cámara, secreto mostrado una vez, revocación, última IP/uso |
| Auditoría | filtros por fecha/usuario/cámara/acción |
| Ajustes | modo CPU (Lite/Auto/Calidad), grabación y retención, red, SSO, diagnóstico |

**Visor (`/viewer`, cualquier usuario autenticado)**
| Vista | Contenido |
|---|---|
| Rejilla | mosaicos configurables, silencio/audio único, estado por cámara, snapshot |
| Pantalla completa | stream individual, zoom digital, talk si la capacidad existe |
| Grabaciones (F2) | timeline por cámara con marcas de evento y huecos de mantenimiento |

### Vídeo (JS propio, sin librerías)
`js/player.js` encapsula: negociación WebRTC (WHEP contra nuestra API), *fallback*
HLS y MJPEG, reconexión, overlay de estado y zoom digital por transformación CSS.
El audio bidireccional se activa solo si `window.isSecureContext` (ADR-005).

### Convenciones
- UI en español; iconos emoji con `title`/`aria-label`.
- Feedback siempre por `toast`; formularios en `modal`; acciones destructivas con
  confirmación.
- El pack trae clases del dominio TCloud (`lanes`, `jobs`): se ignoran.
- **Marca**: `wwwroot/assets/logo.png` (sidebar y login), `favicon.ico` +
  `favicon-16/32.png`, `apple-touch-icon.png`, `icon-192/512.png`,
  `icon-maskable-512.png` y `manifest.webmanifest`. Se generan desde
  `resources/CameraServer.png` con `tools/generate-icons.ps1` (no editarlos a mano).
