# Ultraudio 🎵

Ultraudio es un reproductor de audio Hi-Fi "Bit-Perfect" diseñado específicamente para formatos sin pérdida (lossless). Desarrollado con el objetivo de ofrecer la máxima calidad de sonido directamente a tu DAC (Digital-to-Analog Converter), evitando alteraciones en la señal de audio original.

## ✨ Características Principales

- **Bit-Perfect Audio**: Reproducción exacta, entregando la señal a tu DAC con la mayor fidelidad posible.
- **Soporte Lossless & CUE**: Optimizado para FLAC, con soporte de hojas CUE para dividir álbumes.
- **Visualizador de Espectro**: Análisis de frecuencias de audio en tiempo real integrado en la interfaz.
- **Ventana de Configuración Avanzada**: Selector de DAC dedicado y persistencia de preferencias del usuario.
- **Gestión Avanzada de Librería**: Extracción automática de portadas (Cover Art), escaneo de librerías y registro de historial de reproducción.
- **Integración con Sistema**: Soporte nativo de Teclas Multimedia (Media Keys), integración en la barra de menú (macOS) y servicio HTTP.
- **Multiplataforma**: Construido sobre Avalonia UI y .NET 10, para Windows, macOS y Linux.

## 📸 Interfaz y Uso

### Interfaz Principal
<img src="res/docs/ultraudio sin pista.png" width="762" alt="Ultraudio sin pista"/>

### Carga y Reproducción de Pistas
<img src="res/docs/elegir cancion en flac.png" width="762" alt="Elegir cancion en flac"/>
<br/>
<img src="res/docs/ultraudio con pista.png" width="762" alt="Ultraudio con pista"/>

### Selección de Directorio Musical
<img src="res/docs/elegir carpeta con flacs.png" width="762" alt="Elegir carpeta con flacs"/>
<br/>
<img src="res/docs/ultraudio con carpeta.png" width="762" alt="Ultraudio con carpeta"/>

### Ventana de Configuración (Preferencias y Selección de DAC)
<img src="res/docs/config 1.png" width="762" alt="Configuración General"/>
<br/>
<img src="res/docs/config 2.png" width="762" alt="Configuración de Audio y DAC"/>

### Integración con macOS (Barra de Menú)
<img src="res/docs/barra de menu macos.png" width="762" alt="Barra de Menú en macOS"/>

## 🛠️ Tecnologías y Dependencias

El proyecto está desarrollado en **C# (.NET 10)** y se apoya en las siguientes bibliotecas y tecnologías:

- [Avalonia UI](https://avaloniaui.net/): Framework de interfaz de usuario para aplicaciones de escritorio multiplataforma.
- [BASS Audio Library](http://www.un4seen.com/): Motor de audio para reproducción. Se usan los wrappers `ManagedBass` y `ManagedBass.Flac`.
- [TagLib#](https://github.com/mono/taglib-sharp): Herramienta para extraer metadata

## 📁 Estructura del Proyecto

```text
Ultraudio/
├── lib/               # Bibliotecas nativas (BASS) para macOS, Windows y Linux
├── res/               # Recursos de la aplicación (imágenes de docs, samples)
├── scripts/           # Scripts de compilación, empaquetado y sincronización
├── src/               # Código fuente de la aplicación (Avalonia UI, C#)
├── Ultraudio.slnx     # Archivo de solución de .NET
└── README.md          # Documentación del proyecto
```

## 🚀 Cómo compilar y ejecutar

### Requisitos Previos
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Las bibliotecas nativas de BASS están configuradas en el directorio `lib/` y se copian automáticamente al compilar según el Sistema Operativo.

### Compilación y Ejecución
Asegúrate de ejecutar los comandos desde el directorio raíz del proyecto (donde se encuentra `Ultraudio.slnx`):

```bash
# Compilar el proyecto
dotnet build

# Ejecutar el reproductor
dotnet run --project src/Ultraudio.csproj
```

Para la creación y empaquetado de la aplicación, dispones de scripts automatizados en el directorio `scripts/`:

- **macOS**: `scripts/build_ultraudio_in_macos.sh` (empaqueta como `.app` nativa y también compila para Windows y Linux).
- **Windows**: `scripts/PRE_build_ultraudio_in_windows.ps1` (facilita la creación de paquetes para múltiples sistemas y arquitecturas).
- **Linux**: `scripts/PRE_build_ultraudio_in_linux.sh` (facilita la creación de paquetes para múltiples sistemas y arquitecturas).

> **Nota:** Todos los scripts se encuentran en la carpeta `scripts/`. En los scripts de Linux y Windows no se puede firmar la aplicación para macOS, ya que este paso es exclusivo y debe realizarse desde una Mac.

## Licencia
Este proyecto está bajo la licencia Apache 2.0, a excepción de las 
bibliotecas de audio BASS (ubicadas en `/lib`), las cuales son propiedad 
de Un4seen Developments y se incluyen únicamente para uso no comercial.
Cualquier persona que decida hacer un fork de este proyecto o distribuirlo con fines comerciales es absolutamente responsable de adquirir la licencia comercial correspondiente de BASS Audio Library o remover sus dependencias del código.
<img src="res/docs/acerca de.png" width="762" alt="Acerca De"/>

---

<details>
<summary>TESTS — Entornos de Prueba y Verificación</summary>

<br/>

> **Nota:** Este proyecto ha sido probado y verificado en las siguientes configuraciones de hardware y software.  

| Simbolo | Significado |
| :---: | :--- |
| ✅ | Verificado |
| ❌ | No funciona |
| ⬜  | Pendiente |

---

### Windows

<details>
<summary>&nbsp;&nbsp;ARM</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :--- | :--- | :--- | :--- | :--- | :--- |
| ✅ | Windows 11 Pro | 25h2 | 4 núcleos | 4 GB | v0.3.1 | Virtualizado en VMware Fusion, sin instalar nada de .NET |

</details>

<details>
<summary>&nbsp;&nbsp;x86_64</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| ⬜ | Windows 11 Home | 25h2 | i7-1255U | 16 GB | |  |
| ✅ | Windows 10 Pro | 22h2 | i5-4200M | 16 GB | v0.3.1 | soporte extendido y sin dotnet |
| ⬜ | Windows 10 Pro | 22h2 | i5-3230M | 10 GB | |  |

</details>

---

### macOS

<details>
<summary>&nbsp;&nbsp;ARM</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| ✅ | macOS 27 Golden Gate | 27 | M1 | 8 GB | v0.3.0 | Beta de macOS con dotnet 10 instalado |
| ✅ | macOS Tahoe | 26.5.1 | A18 Pro | 8 GB | v0.5.0 | dotnet 10 instalado |

</details>

<details>
<summary>&nbsp;&nbsp;x86_64</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| ✅ | macOS 15.7.7 | 15.7.7 | i5 (2 Puertos Thunderbolt) | 8 GB | v0.3.0 | dotnet no instalado |

</details>

---

### Linux

<details>
<summary>&nbsp;&nbsp;ARM</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| ✅ | Fedora | 44 | 4 núcleos | 4 GB | v0.3.0 | Virtualizado en VMware Fusion |

</details>

<details>
<summary>&nbsp;&nbsp;x86_64</summary>

<br/>

| ✅ | Sistema Operativo | Version SO | CPU | RAM | Version de app | Notas |
| :---: | :---: | :--- | :--- | :--- | :--- | :--- |
| ⬜ | Fedora | 44 | i7-1255U | 16 GB | |  |
| ⬜ | Arch | | i5-5250U | 4 GB | |  |
| ⬜ | Debian | 13 | Celeron N3350 | 4 GB | |  |
| ✅ | Fedora | 44 | i5 M520 | 8 GB | v0.5.0 | Clonado y compilado |

</details>

---

### 🎶 Canciones de Prueba

> Pistas utilizadas para verificar la fidelidad Bit-Perfect de la reproducción (Ejemplos de 15 segundos de las canciones probadas en la carpeta `res/Samples/`).

| # | Nombre | Artista | Álbum | Formato | Version de app |
| :---: | :--- | :--- | :--- | :---: |
| 1 | Massive Explosion (Instrumental) | 石元丈晴 | DISSIDIA FINAL FANTASY NT: Ultimate Collector's Edition Official Soundtrack | `FLAC` | v0.1.2 |
| 2 | Over Each Other | Linkin Park | From Zero | `FLAC` | v0.2.0 |
| 3 | In the end | Linkin Park | Hybrid Theory | `FLAC`| 0.3.0 |
| 4 | Faith | Limp Bizkit | Greatest Hits | `FLAC`| 0.3.1 |

</details>

