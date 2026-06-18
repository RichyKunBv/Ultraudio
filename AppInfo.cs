using System.Reflection;

namespace Ultraudio;

/// <summary>
/// Fuente única de verdad para la información de la aplicación.
/// La versión se lee del ensamblado, que a su vez se genera desde
/// la propiedad <Version> del archivo .csproj.
/// Solo cambias la versión en el .csproj y se propaga a todos lados.
/// </summary>
public static class AppInfo
{
    public const string Name = "Ultraudio";
    public const string Company = "EsMeSolutions";
    public const string Description = "Reproductor Hi-Fi Bit-Perfect para formatos sin pérdida.";

    /// <summary>
    /// Lee la versión directamente del ensamblado (ej: "0.1.1").
    /// </summary>
    public static string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

    /// <summary>
    /// Versión con formato para UI (ej: "v0.1.1").
    /// </summary>
    public static string VersionDisplay => $"v{Version}";

    /// <summary>
    /// Texto completo para "Acerca de" (ej: "Ultraudio v0.1.1 - Pure Hi-Fi Player").
    /// </summary>
    public static string AboutText => $"{Name} {VersionDisplay} - Pure Hi-Fi Player";
}
