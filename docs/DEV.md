# Desarrollo

## Requisitos

- .NET SDK **10.0** (probado con 10.0.203).
- Opcional: Docker (solo para probar la imagen).
- Opcional: binario de go2rtc (`/opt/go2rtc/go2rtc`) si quieres medios reales.

### Referencia upstream de go2rtc

El código de go2rtc **no se versiona** en este repositorio (es un proyecto externo,
MIT). La imagen descarga el binario fijado por versión y checksum. Si necesitas
leer su código para depurar protocolos:

```bash
git clone --depth 1 --branch v1.9.15 \
  https://github.com/AlexxIT/go2rtc.git source_base/go2rtc-master
```

Ese directorio está en `.gitignore`: es solo referencia, nunca se modifica ni se
compila como parte del producto.

## Compilar y probar

```bash
dotnet build DNXCameraServer.slnx
dotnet test
```

## Ejecutar en local (sin Docker)

```bash
export DATA_DIR="$PWD/data"
export RECORDINGS_DIR="$PWD/recordings"
export GO2RTC_BINARY="$PWD/tools/go2rtc"   # opcional
export WEBRTC_CANDIDATE="192.168.1.10:8555"
export DNX_ADMIN_PASSWORD="dev-password-123"
export ASPNETCORE_URLS="http://0.0.0.0:8090"
dotnet run --project src/DNXCameraServer.Web
```

Si `GO2RTC_BINARY` no existe, la app arranca en **modo degradado**: UI, login,
cuentas, catálogo y auditoría funcionan; el vídeo no. Es el modo normal de
desarrollo sin cámaras.

Abre `http://localhost:8090` y entra con `admin` / la contraseña definida.

> En `localhost` el micrófono funciona sin TLS (contexto seguro). En red, no.

## Estructura

```
src/DNXCameraServer.Domain   Entidades, capacidades, constantes
src/DNXCameraServer.Data     EF Core + SQLite + migraciones
src/DNXCameraServer.Web      API, workers, gateway RTSP, wwwroot
tests/                       xUnit (auth, digest, capacidades, ACL)
docker/Dockerfile            Imagen multi-stage (Alpine + go2rtc fijado)
compose.yaml                 Despliegue con network_mode: host
data/models/*.json           Fichas de capacidades por modelo
```

## Migraciones

```bash
dotnet tool install --global dotnet-ef --version 10.0.15
dotnet ef migrations add <Nombre> --project src/DNXCameraServer.Data --output-dir Migrations
```

Se aplican solas al arrancar (`MigrateAsync`).

## Convenciones

- Identificadores en inglés; UI, logs y documentación en español.
- Nada de lógica por modelo en el código: se declara en `data/models/*.json`.
- No se modifica `source_base/go2rtc-master` (referencia upstream).
- Todo endpoint requiere autorización salvo login y assets.

## Pruebas manuales mínimas

1. Login correcto e incorrecto (y bloqueo por intentos).
2. Alta de cuenta Xiaomi con 2FA.
3. Importar cámara y verla en el visor.
4. Crear token, conectar OBS, revocar y comprobar rechazo.
5. Liberar cámara 10 min y verificar que el RTSP responde 503.
6. Como `viewer`, comprobar que no ve cámaras sin permiso.

## Iconos y marca

El PNG maestro está en `resources/CameraServer.png` (256×256, transparente).
Todas las variantes se generan con:

```powershell
pwsh tools/generate-icons.ps1
```

Produce en `wwwroot/assets/`: `logo.png`, `apple-touch-icon.png`, `icon-192.png`,
`icon-512.png`, `icon-maskable-512.png`; y en `wwwroot/`: `favicon.ico`,
`favicon-16.png`, `favicon-32.png`. No editar esas copias a mano: se regeneran.

Los navegadores cachean el favicon con insistencia; para verlo hay que hacer
Ctrl+F5 o abrir una pestaña nueva.
