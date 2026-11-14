# Seguridad

## Versiones soportadas

Solo la última versión publicada (`main`) recibe correcciones de seguridad.

## Cómo reportar una vulnerabilidad

**No abras un issue público.** Usa **Security → Report a vulnerability** en GitHub
(avisos privados) e incluye:

- Versión o commit afectado.
- Pasos mínimos para reproducirlo.
- Impacto que crees que tiene (lectura de vídeo, acceso a cámaras, escalada de rol…).
- Si aplica, configuración relevante (sin secretos: no pegues tokens ni contraseñas).

Objetivo de respuesta: acuse en 72 h y una evaluación inicial en 7 días.

## Modelo de seguridad del producto

Este servicio está diseñado para **red local** y así se documenta en
`Project_Architecture.md` (ADR-005). Los puntos que más nos importan:

| Superficie | Control |
|---|---|
| UI/API | Cookie `HttpOnly` + `SameSite=Strict`, hash de contraseña, rate-limit de login |
| Instalación inicial | El alta del administrador solo está abierta mientras no exista ningún `Admin` |
| RTSP | Credenciales por dispositivo (Digest/Basic), ACL por cámara, revocación inmediata |
| API interna de go2rtc | Solo `127.0.0.1:1984`; nunca se publica |
| Secretos | Token de Xiaomi custodiado por go2rtc (`go2rtc.yaml`, `0600`); nunca en logs ni en la UI |
| Exposición | Sin publicación a Internet por diseño; el acceso remoto es responsabilidad del operador (VPN) |

### Fuera de alcance (no son vulnerabilidades)

- Tener el servicio accesible desde Internet sin proxy/VPN.
- Ausencia de TLS cuando el operador no lo aporta (se documenta la degradación del micrófono).
- Que Xiaomi bloquee el login por contraseña (limitación del proveedor: ver `docs/TROUBLESHOOTING.md`).
- El acceso físico al host o al volumen `data/`.

## Buenas prácticas de despliegue

1. Cambia la contraseña del administrador tras la instalación.
2. No expongas `8090`, `8554` ni `8555` fuera de la LAN.
3. Mantén el host y la imagen actualizados (`docker compose up -d --build`).
4. Guarda `data/` con permisos restringidos: contiene la base de datos y los tokens.
