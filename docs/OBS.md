# Usar las cámaras en OBS

OBS no habla P2P con Xiaomi: consume RTSP desde DNXCameraServer.

## 1. Crear un token

1. Consola → **Dispositivos** → **Nuevo token**.
2. Elige la cámara y pon un nombre (p. ej. `OBS salón`).
3. Copia la URL que aparece. **Solo se muestra una vez.**

Formato:

```
rtsp://<tokenId>:<secreto>@<host>:8554/cam_xxxxxxxxxxxx
```

## 2. Añadir la fuente en OBS

1. **Fuentes → + → Media Source** (Fuente multimedia).
2. Desmarca *Local File*.
3. En **Input** pega la URL RTSP completa (con usuario y contraseña).
4. En **Input Format** deja `rtsp` (o vacío).
5. Marca *Restart playback when source becomes active* si quieres que reconecte al
   cambiar de escena.
6. Opcional: desactiva el audio en la fuente si no lo necesitas.

> La URL incluye las credenciales. OBS las reenvía en cada reconexión; por eso los
> tokens son de larga duración y revocables, no sesiones efímeras.

## 3. Latencia y códecs

- Latencia típica: **0,5–2 s** (la mayor parte la aporta el P2P de la cámara).
- H264 es lo más compatible. Si tu cámara emite **H265**, OBS puede reproducirlo,
  pero consume más CPU; si tienes problemas, cambia el perfil de esa cámara a `sd`
  o usa el modo CPU `Calidad` para habilitar transcode (fase 2).
- Audio: PCMA/OPUS según modelo. Si no lo necesitas, silencia la fuente.

## 4. Varias cámaras

Crea **un token por cámara**. Un token da acceso a una sola cámara: así puedes
revocar uno sin afectar al resto y auditar quién ve qué.

## 5. Revocación

**Dispositivos → Revocar**. A partir de ese momento la URL deja de autenticar
(inmediato, sin reiniciar el servicio). OBS mostrará error al reconectar.
