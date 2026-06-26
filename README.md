# Ultraudio 🎵

Ultraudio es un reproductor de audio Hi-Fi "Bit-Perfect" diseñado específicamente para formatos sin pérdida (lossless). Desarrollado con el objetivo de ofrecer la máxima calidad de sonido directamente a tu DAC (Digital-to-Analog Converter), evitando alteraciones en la señal de audio original.

## ✨ Características Principales

- **Bit-Perfect Audio**: Reproducción exacta, entregando la señal de audio a tu DAC con la mayor fidelidad posible.
- **Soporte de Formatos Lossless**: Optimizado para archivos FLAC y soporte planeado implementación para DSD, garantizando una experiencia de escucha en alta resolución.
- **Selector de DAC Dedicado**: Permite elegir la interfaz de salida de audio exacta (hardware) para dirigir la música.
- **Gestión de Archivos y Carpetas**: Reproduce canciones individuales o carga directorios enteros de música.
- **Multiplataforma**: Construido sobre Avalonia UI y .NET 10, compatible con Windows, macOS y Linux.

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

### Configuración del Dispositivo de Salida (DAC)
<img src="res/docs/selector de dac.png" width="762" alt="Selector de DAC"/>

## 🛠️ Tecnologías y Dependencias

El proyecto está desarrollado en **C# (.NET 10)** y se apoya en las siguientes bibliotecas y tecnologías:

- [Avalonia UI](https://avaloniaui.net/): Framework de interfaz de usuario para aplicaciones de escritorio multiplataforma.
- [BASS Audio Library](http://www.un4seen.com/): Motor de audio para reproducción. Se usan los wrappers `ManagedBass` y `ManagedBass.Flac`.
- [TagLib#](https://github.com/mono/taglib-sharp): Herramienta para extraer metadata

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

Para macOS, también tienes a tu disposición el script `build_ultraudio_in_macos.sh` que facilita la creación y el empaquetado como aplicación nativa de macOS (`.app`) y para windows y linux.
Para Windows está el archivo .ps1 `PRE_build_ultraudio_in_windows.ps1` para facilitar la creacion de paquetes de todos los sistemas y arquitecturas.
En Linux está el script `PRE_build_ultraudio_in_linux.sh` que facilita la creacion de paquetes de todos los sistemas y arquitecturas.

NOTA: En el script de linux y windows no se puede firmar la aplicacion para macOS ya que es algo exclusivo que se puede y debe hacer desde una mac (y todos los scripts estan en la carpeta `scripts/`).

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
| ⬜ | Windows 10 Pro | 22h2 | i5-4200M | 16 GB | |  |
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
| ✅ | macOS Tahoe | 26.5.1 | A18 Pro | 8 GB | v0.3.0 | dotnet 10 instalado |

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
| ⬜ | Debian | 13 | i5 M520 | 8 GB | |  |

</details>

---

### 🎶 Canciones de Prueba

> Pistas utilizadas para verificar la fidelidad Bit-Perfect de la reproducción.

| # | Nombre | Artista | Álbum | Formato |
| :---: | :--- | :--- | :--- | :---: |
| 1 | Massive Explosion (Instrumental) | 石元丈晴 | DISSIDIA FINAL FANTASY NT: Ultimate Collector's Edition Official Soundtrack | `FLAC` |
| 2 | Over Each Other | Linkin Park | From Zero | `FLAC` |

</details>

