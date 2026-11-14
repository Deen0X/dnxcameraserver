# Solución de problemas

## No se puede añadir la cuenta Xiaomi

### Síntoma: código 70016

Mensaje del gateway:

```json
{"code":70016,"description":"登录验证失败","result":"error","captchaUrl":null}
```

**Qué significa:** Xiaomi ha rechazado la **verificación de seguridad** del login
por contraseña. No implica necesariamente que la contraseña sea incorrecta: es su
sistema anti-bot, que desconfía de logins hechos desde un servidor, una red
distinta a la habitual o tras varios intentos seguidos.

**Importante:** no reintentes en bucle. Cada intento fallido puede alargar el
bloqueo temporal (de minutos a horas).

#### Solución A — verificar desde la misma red y reintentar

1. Desde un equipo **en la misma red** que el servidor, entra en
   <https://account.xiaomi.com> con la misma cuenta.
2. Completa cualquier verificación que pida (SMS, email, dispositivo de confianza).
3. Espera **15–30 minutos** si hubo varios intentos fallidos.
4. Vuelve a la consola → **Cuentas Xiaomi → Cuenta Xiaomi** e inténtalo de nuevo.

#### Solución B — alta con token (recomendada si A no funciona)

Se salta el flujo de contraseña usando las cookies de una sesión web ya validada.

1. En un navegador **de la misma red**, inicia sesión en <https://account.xiaomi.com>.
2. Abre DevTools → **Application** (o Almacenamiento) → **Cookies** →
   `https://account.xiaomi.com`.
3. Copia dos valores:
   - `userId` — numérico (p. ej. `1234567890`).
   - `passToken` — cadena larga que empieza por `V1:`.
4. Consola → **Cuentas Xiaomi → 🔑 Con token**: rellena `userId`, `passToken` y la
   región (China = `cn`).
5. El sistema guarda el token en la config de go2rtc y **verifica** que Xiaomi lo
   acepte listando dispositivos.

Si el token deja de funcionar (caducó o se cerró la sesión en el navegador), repite
la extracción y guárdalo de nuevo.

> **Nota técnica:** al guardar un token, go2rtc se **reinicia** (re-ejecuta el
> binario, ~1–2 s). Es necesario porque solo lee los tokens al arrancar; los
> streams activos se reconectan solos.

### El token era correcto pero falló la primera vez

Ocurría por un detalle de go2rtc: `PATCH /api/config` escribe el fichero pero **no
recarga el mapa de tokens en memoria**. Desde la corrección, tras escribir el token
se llama a `/api/restart`, de modo que la validación ya usa el token real. Si viste
el error con una versión anterior, vuelve a intentarlo.

### La cuenta se añade pero no aparecen cámaras

- Comprueba la **región**: debe ser la del alta en la app Mi Home (`cn`, `de`,
  `i2`, `ru`, `sg`, `us`). Una cuenta china con región `de` no encuentra nada.
- Pulsa **Cargar dispositivos** de nuevo: el listado se pide a la nube en ese momento.

## go2rtc se reinicia solo

El supervisor lo relanza automáticamente (backoff 1 s → 5 s → 10 s → 30 s).
Si ves `fatal error: concurrent map read and map write`, es una carrera conocida
de go2rtc en su módulo Xiaomi: la aplicación **serializa** los logins para no
dispararla. Aun así, si ocurre, el servicio se recupera solo.

## El vídeo no arranca

1. `/healthz` debe decir `go2rtc: ok`.
2. `WEBRTC_CANDIDATE` debe ser `IP_DEL_HOST:8555` (la IP de la LAN del servidor).
3. En OBS/VLC usa la URL RTSP del token, no la del navegador.
4. Si la cámara está **liberada** (cedida a la app Mi Home), no hay stream: espera
   a que termine la ventana o pulsa Reconectar.

## Olvidé la contraseña del administrador

```bash
docker compose stop
docker compose run --rm dnxcameraserver --reset-admin-password
docker compose start
```

Vuelve a la web: pedirá crear el administrador de nuevo. No borra cámaras,
tokens ni visores.
