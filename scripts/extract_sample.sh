#!/bin/bash

# va calado en macOS nada mas, no en Linux asi que si no funciona en Linux no e problema mio

set -euo pipefail

OUTDIR="res/Samples"
DEFAULT_START="00:00:30"
DEFAULT_DURATION="00:00:15"


error() {
    echo "Error: $1" >&2
    exit ${2:-1}
}


clean_path() {
    local p="$1"
    p="$(printf '%s' "$p" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
    p="${p#\"}"; p="${p%\"}"
    p="${p#\'}"; p="${p%\'}"
    p="${p#file://}"
    eval echo "$p"
}

ALLOWED_EXT=(flac wav aiff aif dsf dff)

is_allowed_audio() {
    local f="$1"
    local ext="${f##*.}"
    ext="$(printf '%s' "$ext" | tr '[:upper:]' '[:lower:]')"
    for e in "${ALLOWED_EXT[@]}"; do
    if [[ "$e" == "$ext" ]]; then
        return 0
    fi
    done
    return 1
}


read -r -p "Arrastra un archivo de audio (${ALLOWED_EXT[*]}) y presiona Enter: " INPUT
INPUT=$(clean_path "${INPUT}")

[ -z "$INPUT" ] && error "No se proporcionó ninguna ruta."
[ ! -e "$INPUT" ] && error "El archivo o directorio '$INPUT' no existe."
[ -d "$INPUT" ] && error "Se esperaba un archivo de audio, pero se arrastró un directorio."

if ! is_allowed_audio "$INPUT"; then
    error "Formato no soportado. Extensiones aceptadas: ${ALLOWED_EXT[*]}"
fi


read -r -p "Inicio (HH:MM:SS) [${DEFAULT_START}]: " START_IN
START="${START_IN:-$DEFAULT_START}"

read -r -p "Duración (HH:MM:SS) [${DEFAULT_DURATION}]: " DURATION_IN
DURATION="${DURATION_IN:-$DEFAULT_DURATION}"


mkdir -p "$OUTDIR"
base=$(basename -- "$INPUT")
name="${base%.*}"
OUTPATH="$OUTDIR/$name.flac"


if ! command -v ffmpeg >/dev/null 2>&1; then
    error "ffmpeg no está instalado en el PATH. Instálalo primero (ej. brew install ffmpeg)"
fi

echo "Procesando audio..."

ffmpeg -y -i "$INPUT" -ss "$START" -t "$DURATION" -c copy "$OUTPATH" || error "ffmpeg falló al procesar el audio."

echo "¡Listo! Archivo creado en: $OUTPATH"