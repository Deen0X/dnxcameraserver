# Despliegue

## 1. Requisitos

- Host **Linux** (amd64 o arm64) con Docker Engine ≥ 24 y Compose v2.
- El host debe estar en la **misma LAN** que las cámaras Xiaomi.
- Salida a Internet para el handshake de claves de Xiaomi.
- Puertos libres: `8090`, `8554`, `8555` (udp+tcp), `1984` y `18554` (estos dos
  últimos solo en loopback).

> Docker Desktop (Windows/macOS) no es compatible con el modo de red necesario.

## 2. Primera puesta en marcha

```bash
mkdir -p /opt/dnxcameraserver && cd /opt/dnxcameraserver
# copia compose.yaml, docker/, src/, Directory.Build.props y data/models
mkdir -p data recordings
docker compose up -d --build
docker compose logs -f
```

Comprobaciones:

```bash
curl -s http://127.0.0.1:8090/healthz | jq
curl -s http://127.0.0.1:8090/readyz
```

`/healthz` devuelve `degraded` mientras go2rtc no esté listo; el supervisor lo
arranca y reinicia solo.

### Instalación inicial

La primera vez **no hay usuarios**. Al abrir la web aparece una pantalla de
instalación que pide crear el administrador (usuario y contraseña, mínimo 8
caracteres). No existen contraseñas por defecto ni ficheros con secretos.

- El modo instalación permanece activo **mientras no exista ningún Admin**.
- Cualquier petición a `/api/setup/*` devuelve `409` en cuanto hay un admin.
- El login responde `409 setup_required` mientras falte el administrador.

### Resetear el administrador

Si se olvida la contraseña, se lanza el contenedor con `--reset-admin-password`:

```bash
cd /opt/dnxcameraserver
docker compose stop
docker compose run --rm dnxcameraserver --reset-admin-password
docker compose start
```

El comando elimina **solo los administradores** (y sus permisos), conserva
visores, cámaras y tokens, y termina sin arrancar el servidor. Al abrir la web
volverá a pedir crear el administrador.

También se admite la variable `RESET_ADMIN_PASSWORD=true` para el mismo efecto.

### UID/GID del contenedor

El servicio corre como `1000:1000` (el usuario habitual del host) para que `data/`
y `recordings/` sean legibles y no queden ficheros propiedad de `root`:

```yaml
user: "1000:1000"
```

Si tu usuario tiene otro UID, ajústalo o cambia el propietario de los directorios:

```bash
sudo chown -R 1000:1000 data recordings
```

## 3. Configuración

Todas las variables se definen en `compose.yaml`. Las importantes:

| Variable | Ejemplo | Notas |
|---|---|---|
| `WEBRTC_CANDIDATE` | `192.168.1.10:8555` | IP:puerto del host en la LAN. **Obligatoria** para WebRTC |
| `LAN_CIDR` | `192.168.1.0/24` | Documental y para validaciones |

> **Valores locales sin ensuciar el repo:** crea `compose.override.yaml` junto al
> `compose.yaml` (Compose lo fusiona automáticamente y está en `.gitignore`):
>
> ```yaml
> services:
>   dnxcameraserver:
>     environment:
>       LAN_CIDR: 192.168.1.0/24
>       WEBRTC_CANDIDATE: 192.168.1.10:8555
> ```
| `CPU_MODE` | `Auto` | `Lite` (ARM/CPU débil), `Auto`, `Calidad` |
| `RTSP_LISTEN_PORT` | `8554` | Puerto del gateway RTSP |
| `SSO_ENABLED` | `false` | Activa SSO por cabecera |
| `SSO_TRUSTED_PROXIES` | `192.168.1.20` | IP de Caddy/Authelia; **obligatoria** si SSO está activo |

## 4. Reverse proxy y SSO (opcional)

La app no gestiona TLS. Si ya tienes Caddy/Authelia:

```caddy
dnxcamera.example.lan {
    reverse_proxy 127.0.0.1:8090
}
```

Para evitar el doble login:

```yaml
SSO_ENABLED: "true"
SSO_HEADER: Remote-User
SSO_TRUSTED_PROXIES: 127.0.0.1,192.168.1.20
```

- La cabecera **solo** se acepta desde esas IPs.
- Si Authelia deja de enviarla, la app muestra su **login local** sin reconfigurar.
- El usuario SSO debe existir como cuenta local (no hay auto-provisión).
- Los permisos por cámara siguen siendo de la aplicación.

Sin TLS, todo funciona salvo el micrófono (hablar a la cámara).

## 5. Firewall

Con `network_mode: host` no hay mapeo de puertos; el firewall del host manda:

```bash
# permitir la LAN
sudo ufw allow from 192.168.1.0/24 to any port 8090 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 8554 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 8555 proto udp
```

No abras `1984` ni `18554`: son internos.

## 6. Actualización

```bash
cd /opt/dnxcameraserver
git pull            # o copia la nueva versión
docker compose up -d --build
```

- Las migraciones de base de datos se aplican al arrancar.
- go2rtc está fijado por versión y checksum dentro de la imagen.

## 7. Respaldo

| Qué | Dónde |
|---|---|
| Base de datos | `data/camera.db` |
| Config de go2rtc (incluye tokens Xiaomi) | `data/go2rtc.yaml` (permisos 0600) |
| Grabaciones (fase 2) | `recordings/` |

```bash
tar -czf dnx-backup-$(date +%F).tar.gz data/
```

Los tokens de Xiaomi **no** se pueden exportar desde la app: viven en
`data/go2rtc.yaml`. Protégelo.

## 8. Problemas frecuentes

| Síntoma | Causa probable | Solución |
|---|---|---|
| `/healthz` = `degraded` | go2rtc no arranca | `docker compose logs` y comprobar `/opt/go2rtc/go2rtc` |
| No conecta cámaras | Cámara en otra subred / sin Internet | Verificar LAN y salida a `*.mi.com` |
| Vídeo en negro en el visor | `WEBRTC_CANDIDATE` incorrecto | Poner `IP:8555` del host |
| "No se pudo conectar" tras liberar | La app Mi Home sigue usando la cámara | Esperar el reintento o pulsar Reconectar |
| OBS no conecta | Credenciales revocadas o puerto 8554 cerrado | Revisar token y firewall |
| Micrófono deshabilitado | Sin HTTPS | Usar proxy TLS o `localhost` |
