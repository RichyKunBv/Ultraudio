# Ultraudio 🎵

Ultraudio es un reproductor de audio Hi-Fi "Bit-Perfect" diseñado específicamente para formatos sin pérdida (lossless). Desarrollado con el objetivo de ofrecer la máxima calidad de sonido directamente a tu DAC (Digital-to-Analog Converter), evitando alteraciones en la señal de audio original.

## ✨ Características Principales

- **Bit-Perfect Audio**: Reproducción exacta, entregando la señal de audio a tu DAC con la mayor fidelidad posible.
- **Soporte de Formatos Lossless**: Optimizado para archivos FLAC y soporte planeado implementacion para DSD, garantizando una experiencia de escucha en alta resolución.
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

Para macOS, también tienes a tu disposición el script `build_ultraudio_formacos.sh` que facilita la creación y el empaquetado como aplicación nativa de macOS (`.app`). (Se está trabajando en un script para Windows y Linux, solo que solo soy una persona y encima soy estudiante asi que mi tiempo es algo limitado, pero la aplicacion esta hecha con <3)

## Licencia
Este proyecto está bajo la licencia Apache 2.0, a excepción de las 
bibliotecas de audio BASS (ubicadas en `/lib`), las cuales son propiedad 
de Un4seen Developments y se incluyen únicamente para uso no comercial.
Cualquier persona que decida hacer un fork de este proyecto o distribuirlo con fines comerciales es absolutamente responsable de adquirir la licencia comercial correspondiente de BASS Audio Library o remover sus dependencias del código.
<img src="res/docs/acerca de.png" width="762" alt="Acerca De"/>

---

<details>
<summary>TESTS</summary>

## Entornos de Prueba

Este proyecto ha sido probado y verificado en las siguientes configuraciones de hardware y software:

<details>
<summary>Windows</summary>

| Sistema Operativo | Arquitectura / CPU | Memoria RAM | Notas |
| :--- | :--- | :--- | :--- |
|   |   |   |   |

</details>

<details>
<summary>macOS</summary>

| Sistema Operativo | Arquitectura / CPU | Memoria RAM | Notas |
| :--- | :--- | :--- | :--- |
| macOS 27 | ARM / M1 | 8 GB | Beta de macOS con dotnet 10 instalado|
| macOS 26.5.1 | ARM / A18 Pro | 8 GB | dotnet 10 instalado |
| macOS 15.7.7 | x86_64 / i5 (2 Puertos Thunderbolt) | 8 GB | dotnet no instalado |

</details>

<details>
<summary>Linux</summary>


| Sistema Operativo | Arquitectura / CPU | Memoria RAM | Notas |
| :--- | :--- | :--- | :--- |
| Fedora | ARM / 4 nucleos | 4 GB | Virtualizado en VMware Fusion |

</details>

---

<summary>Canciones</summary>

## Se ha probado con estas canciones

| Nombre | Artista | Album | Formato |
| :--- | :--- | :--- | :--- |
| Massive Explosion (Instrumental) | 石元丈晴 | DISSIDIA FINAL FANTASY NT: Ultimate Collector's Edition Official Soundtrack | FLAC |

</details>