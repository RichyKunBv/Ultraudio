#!/bin/bash

# es para hacer las compilaciones para macOS, realmente ignorenlo ya que es algo mas para mi que para ustedes, nada mas para mas agilidad
# Tambien como yo trabajo en macOS los comandos son en UNIX y no funconarian bien en Linux y mucho menos en Windows

# Definir las rutas
PROJECT_DIR="$HOME/Ultraudio"
ARM_APP="$HOME/UltraudioARM64macOS/Ultraudio.app"
X64_APP="$HOME/UltraudioX86_64macOS/Ultraudio.app"

carpeta() {
    echo "Creando carpetas..."
    mkdir -p "$X64_APP/Contents/"{MacOS,Resources}
    mkdir -p "$ARM_APP/Contents/"{MacOS,Resources}
    
    # IMPORTANTE: Copiar el Info.plist base para que plutil tenga algo que editar después
    cp "$PROJECT_DIR/src/Info.plist" "$X64_APP/Contents/" 2>/dev/null || echo "Aviso: No se encontró Info.plist base"
    cp "$PROJECT_DIR/src/Info.plist" "$ARM_APP/Contents/" 2>/dev/null
    
    echo "Carpetas creadas"
}

actualizar() {
    echo "=== Iniciando compilación de Ultraudio ==="

    # 1. Extraer la versión del archivo .csproj usando awk
    VERSION=$(awk -F'[><]' '/<Version>/{print $3}' "$PROJECT_DIR/src/Ultraudio.csproj")

    if [ -z "$VERSION" ]; then
        echo "Error: No se pudo encontrar la etiqueta <Version> en el .csproj"
        return
    fi

    echo "Versión detectada: $VERSION"
    echo "Publicando binarios..."

    # 2. Compilar ambas arquitecturas
    cd "$PROJECT_DIR/src"
    dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
    dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

    echo "Copiando archivos y actualizando Info.plist..."

    # 3. Procesar versión ARM64
    cp -a "$PROJECT_DIR/src/bin/Release/net10.0/osx-arm64/publish/." "$ARM_APP/Contents/MacOS/"
    cp "$PROJECT_DIR/src/Assets/icon.icns" "$ARM_APP/Contents/Resources/"
    # plutil edita el valor de la llave CFBundleVersion de forma segura
    plutil -replace CFBundleVersion -string "$VERSION" "$ARM_APP/Contents/Info.plist"

    # 4. Procesar versión X86_64
    cp -a "$PROJECT_DIR/src/bin/Release/net10.0/osx-x64/publish/." "$X64_APP/Contents/MacOS/"
    cp "$PROJECT_DIR/src/Assets/icon.icns" "$X64_APP/Contents/Resources/"
    plutil -replace CFBundleVersion -string "$VERSION" "$X64_APP/Contents/Info.plist"

    echo "Limpiando atributos y firmando aplicaciones..."

    # 5. Firmar ARM64
    xattr -cr "$ARM_APP"
    codesign --force --deep --sign - "$ARM_APP"

    # 6. Firmar X86_64
    xattr -cr "$X64_APP"
    codesign --force --deep --sign - "$X64_APP"

    echo "=== ¡Listo! Ultraudio v$VERSION empaquetado para ambas arquitecturas ==="
}

press_any_key() {
    echo -e "\nPulsa cualquier tecla para volver al menú..."
    read -n 1 -s -r
}

# --- MENÚ PRINCIPAL ---
show_menu() {
    cd "$PROJECT_DIR" || exit 1
    clear
    echo -e "   1) Crear estructura de carpetas"
    echo -e "   2) Compilar y empaquetar"
    echo -e "   X) Salir"
    read -p "   >> Introduce tu elección: " choice
    echo ""

    case "$choice" in
        1) carpeta; press_any_key ;;
        2) actualizar; press_any_key ;;
        X|x) echo "Saliendo... ¡Hasta pronto!"; exit 0 ;;
        *) echo "Opción inválida. Por favor, intenta de nuevo."; sleep 2 ;;
    esac
}

# Bucle infinito para mantener el menú corriendo
while true; do
    show_menu
done