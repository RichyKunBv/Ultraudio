#!/bin/bash

# Definir las rutas
PROJECT_DIR="$HOME/Ultraudio"
ARM_APP="$HOME/UltraudioARM64macOS/Ultraudio.app"
X64_APP="$HOME/UltraudioX86_64macOS/Ultraudio.app"

echo "=== Iniciando compilación de Ultraudio ==="

# 1. Extraer la versión del archivo .csproj usando awk
VERSION=$(awk -F'[><]' '/<Version>/{print $3}' "$PROJECT_DIR/Ultraudio.csproj")

if [ -z "$VERSION" ]; then
    echo "Error: No se pudo encontrar la etiqueta <Version> en el .csproj"
    exit 1
fi

echo "Versión detectada: $VERSION"
echo "Publicando binarios..."

# 2. Compilar ambas arquitecturas
cd "$PROJECT_DIR"
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

echo "Copiando archivos y actualizando Info.plist..."

# 3. Procesar versión ARM64
cp -a "$PROJECT_DIR/bin/Release/net10.0/osx-arm64/publish/." "$ARM_APP/Contents/MacOS/"
cp "$PROJECT_DIR/icon.icns" "$ARM_APP/Contents/Resources/"
# plutil edita el valor de la llave CFBundleVersion de forma segura
plutil -replace CFBundleVersion -string "$VERSION" "$ARM_APP/Contents/Info.plist"

# 4. Procesar versión X86_64
cp -a "$PROJECT_DIR/bin/Release/net10.0/osx-x64/publish/." "$X64_APP/Contents/MacOS/"
cp "$PROJECT_DIR/icon.icns" "$X64_APP/Contents/Resources/"
plutil -replace CFBundleVersion -string "$VERSION" "$X64_APP/Contents/Info.plist"

echo "Limpiando atributos y firmando aplicaciones..."

# 5. Firmar ARM64
xattr -cr "$ARM_APP"
codesign --force --deep --sign - "$ARM_APP"

# 6. Firmar X86_64
xattr -cr "$X64_APP"
codesign --force --deep --sign - "$X64_APP"

echo "=== ¡Listo! Ultraudio v$VERSION empaquetado para ambas arquitecturas ==="
