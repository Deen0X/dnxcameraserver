#!/usr/bin/env bash
# Sube un artefacto (APK, pack, captura) a TCloud/Telegram en streaming.
#
#   ./tools/upload-file.sh app/build/outputs/apk/debug/app-debug.apk "resumen"
#   ./tools/upload-file.sh ruta/pack.sensepack "" --front
#
# Carga .tcloud.env en el proceso: el token no se imprime ni se persiste.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="$ROOT/.tcloud.env"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: falta $ENV_FILE" >&2
    exit 2
fi

set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

FILE="${1:-}"
NOTES="${2:-}"
FRONT="${3:-}"

if [[ -z "$FILE" ]]; then
    echo "Uso: $0 <fichero> [notas] [--front]" >&2
    exit 2
fi

[[ "$FILE" = /* ]] || FILE="$ROOT/$FILE"
if [[ ! -f "$FILE" ]]; then
    echo "ERROR: no existe $FILE" >&2
    exit 2
fi

PROJECT="${TCLOUD_PROJECT:-$(basename "$ROOT")}"
LABEL="$(basename "$FILE")"

GRADLE="$ROOT/app/build.gradle.kts"
[[ -f "$GRADLE" ]] || GRADLE="$ROOT/app/build.gradle"
if [[ "$FILE" == *.apk && -f "$GRADLE" ]]; then
    VERSION="$(grep -oE 'versionName[[:space:]]*=?[[:space:]]*"[^"]+"' "$GRADLE" | head -n1 | grep -oE '"[^"]+"' | tr -d '"')"
    [[ -n "$VERSION" ]] && LABEL="$VERSION (debug)"
fi

STAMP="$(date '+%Y-%m-%d %H:%M')"
CAPTION="$PROJECT | $LABEL | $STAMP"
[[ -n "$NOTES" ]] && CAPTION="$CAPTION | $NOTES"

ARGS=("$ROOT/tools/tcloud_upload.py" "$FILE" --caption "$CAPTION")
[[ "$FRONT" == "--front" ]] && ARGS+=(--front)

PY=python3
command -v python3 >/dev/null 2>&1 || PY=python

echo "Subiendo $FILE"
exec "$PY" "${ARGS[@]}"
