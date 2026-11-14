# Contribuir

Gracias por el interés. Este proyecto nació como una herramienta personal para
ver cámaras Xiaomi en la LAN, y las contribuciones son bienvenidas siempre que
respeten el diseño (ver `Project_Architecture.md`).

## Puesta en marcha

```bash
dotnet build DNXCameraServer.slnx
dotnet test
```

Para ejecutar en local sin cámaras:

```bash
export DATA_DIR="$PWD/data"
export RECORDINGS_DIR="$PWD/recordings"
export DNX_ADMIN_PASSWORD="dev-password-123"
dotnet run --project src/DNXCameraServer.Web
```

Sin el binario de go2rtc la aplicación arranca en **modo degradado**: UI, cuentas,
catálogo y auditoría funcionan; el vídeo no. Está explicado en `docs/DEV.md`.

## Reglas del proyecto

1. **No se modifica go2rtc**: se usa como binario fijado por versión y checksum.
   El código upstream no está en este repositorio.
2. **No se inventan capacidades**: si go2rtc no implementa PTZ, zoom óptico o
   eventos nativos, la UI no los ofrece. Las capacidades por modelo viven en
   `data/models/*.json` y solo se declaran con evidencia.
3. **Todo endpoint requiere autorización** salvo el estado de instalación, el
   login y los assets estáticos.
4. **Cambios de arquitectura** se anotan como ADR en `Project_Architecture.md`
   antes de implementarlos.
5. UI, logs y documentación en **español**; identificadores de código en inglés.

## Antes de abrir un PR

- `dotnet build` y `dotnet test` en verde (37 pruebas y creciendo).
- Una prueba por cada corrección de comportamiento.
- Cambios pequeños y con un objetivo claro; evita reformateos masivos.
- Si tocas la UI, respeta el sistema de diseño DNX Web UI (`wwwroot/css/dnx-web-ui.css`,
  14 px base, tokens CSS, tema auto/oscuro/claro).

## Reportar problemas

- **Bugs**: issue con versión, modelo de cámara, región de la cuenta y pasos.
- **Seguridad**: no abras issue; usa el aviso privado (ver `SECURITY.md`).
