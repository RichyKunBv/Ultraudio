# Makefile inteligente para Ultraudio (Linux & Unix-like)

.PHONY: all build run clean restore package install uninstall publish-x64 publish-arm64

# Proyecto principal
PROJECT = src/Ultraudio.csproj

# Detección automática de arquitectura
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_M),x86_64)
    DOTNET_ARCH = x64
else ifeq ($(UNAME_M),aarch64)
    DOTNET_ARCH = arm64
else ifeq ($(UNAME_M),armv7l)
    DOTNET_ARCH = arm
else
    DOTNET_ARCH = $(UNAME_M)
endif

RUNTIME_ID = linux-$(DOTNET_ARCH)
PUBLISH_DIR = src/bin/Release/net10.0/$(RUNTIME_ID)/publish

# Variables de instalación estándar en Linux
DESTDIR ?=
PREFIX ?= /usr/local
INSTALL_BIN = $(DESTDIR)$(PREFIX)/bin
INSTALL_LIB = $(DESTDIR)$(PREFIX)/lib/ultraudio
INSTALL_SHARE = $(DESTDIR)$(PREFIX)/share

# Objetivo por defecto
all: build

# Restaurar dependencias
restore:
	dotnet restore $(PROJECT)

# Compilar el proyecto
build: restore
	dotnet build $(PROJECT)

# Ejecutar el reproductor
run:
	dotnet run --project $(PROJECT)

# Empaquetar para la arquitectura actual
package:
	dotnet publish $(PROJECT) -c Release -r $(RUNTIME_ID) --self-contained true -p:PublishSingleFile=true

# Instalar la aplicación en el sistema (requiere permisos de superusuario si PREFIX=/usr/local)
install: package
	@echo "Instalando Ultraudio..."
	install -d $(INSTALL_LIB)
	cp -r $(PUBLISH_DIR)/* $(INSTALL_LIB)/
	install -d $(INSTALL_BIN)
	ln -sf $(PREFIX)/lib/ultraudio/Ultraudio $(INSTALL_BIN)/ultraudio
	
	# Instalar icono
	install -d $(INSTALL_SHARE)/icons/hicolor/512x512/apps
	install -m 644 src/Assets/icon.png $(INSTALL_SHARE)/icons/hicolor/512x512/apps/ultraudio.png
	
	# Instalar archivo .desktop
	install -d $(INSTALL_SHARE)/applications
	@echo "[Desktop Entry]" > $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Type=Application" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Name=Ultraudio" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "GenericName=Reproductor de Audio" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Comment=Aplicación para la reproducción y gestión de audio" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Exec=$(PREFIX)/bin/ultraudio" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Icon=ultraudio" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Terminal=false" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Categories=Audio;AudioVideo;Player;" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "StartupNotify=true" >> $(INSTALL_SHARE)/applications/ultraudio.desktop
	
	@echo "Instalación completada. Puedes ejecutar 'ultraudio' desde el menú o la terminal."

# Desinstalar la aplicación
uninstall:
	@echo "Desinstalando Ultraudio..."
	rm -rf $(INSTALL_LIB)
	rm -f $(INSTALL_BIN)/ultraudio
	rm -f $(INSTALL_SHARE)/icons/hicolor/512x512/apps/ultraudio.png
	rm -f $(INSTALL_SHARE)/applications/ultraudio.desktop
	@echo "Desinstalación completada."

# Limpiar binarios y objetos compilados
clean:
	dotnet clean $(PROJECT)
	rm -rf src/bin src/obj

# Compatibilidad con comandos explícitos
publish-x64:
	dotnet publish $(PROJECT) -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

publish-arm64:
	dotnet publish $(PROJECT) -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
