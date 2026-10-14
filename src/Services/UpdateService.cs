using Ultraudio.Core;
using System.Diagnostics;
using System.IO;
using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using Windows.ApplicationModel;
using Windows.Services.Store;
#endif

namespace Ultraudio.Services;

public enum UpdateStatus
{
    Error,
    Outdated,
    UpToDate,
    Newer,
    MicrosoftStoreUpdateAvailable,
    MicrosoftStoreUpToDate,
    AssetUnavailable
}

public enum StoreUpdateInstallStatus
{
    Completed,
    NoUpdates,
    Canceled,
    Failed
}

public static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/RichyKunBv/Ultraudio/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    private static readonly HttpClient DownloadClient = CreateDownloadClient();

#if WINDOWS
    [ComImport]
    [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize(IntPtr windowHandle);
    }
#endif

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ultraudio-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = CreateClient();
        client.Timeout = TimeSpan.FromHours(2);
        return client;
    }

    /// <summary>
    /// True only for a package signed and installed by the Microsoft Store.
    /// Sideloaded MSIX packages continue to use the GitHub release updater.
    /// </summary>
    public static bool IsMicrosoftStoreInstallation
    {
        get
        {
#if WINDOWS
            try
            {
                return Package.Current.SignatureKind == PackageSignatureKind.Store;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
#else
            return false;
#endif
        }
    }

    public static string GetCurrentVersion() => AppInfo.Version;

    public static async Task<(UpdateStatus status, string? latestVersion, string? downloadUrl)> CheckForUpdatesAsync(string currentVersion)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsMicrosoftStoreInstallation)
        {
            var (status, latestVersion) = await CheckMicrosoftStoreUpdatesAsync();
            return (status, latestVersion, null);
        }

        try
        {
            using var response = await Client.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn("Update", $"GitHub returned {(int)response.StatusCode} while checking for updates.");
                return (UpdateStatus.Error, null, null);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagElement))
                return (UpdateStatus.Error, null, null);

            string latestVersion = (tagElement.GetString() ?? string.Empty).TrimStart('v', 'V');
            if (!Version.TryParse(currentVersion.TrimStart('v', 'V'), out var current)
                || !Version.TryParse(latestVersion, out var latest))
                return (UpdateStatus.Error, null, null);

            if (current < latest)
            {
                var downloadUrl = FindAssetUrl(root, GetCurrentAssetName());
                return downloadUrl is null
                    ? (UpdateStatus.AssetUnavailable, latestVersion, null)
                    : (UpdateStatus.Outdated, latestVersion, downloadUrl);
            }

            if (current > latest) return (UpdateStatus.Newer, latestVersion, null);
            return (UpdateStatus.UpToDate, latestVersion, null);
        }
        catch (Exception ex)
        {
            Log.Warn("Update", $"Error checking GitHub releases: {ex.Message}");
            return (UpdateStatus.Error, null, null);
        }
    }

    /// <summary>
    /// Uses the Store's own update channel for Store-installed packages. Must be
    /// called from the UI thread because the Store may show an approval dialog.
    /// </summary>
    public static async Task<StoreUpdateInstallStatus> InstallMicrosoftStoreUpdatesAsync(IntPtr windowHandle)
    {
#if WINDOWS
        if (!IsMicrosoftStoreInstallation)
            throw new InvalidOperationException("Ultraudio no está instalado desde Microsoft Store.");
        if (windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo obtener la ventana para mostrar la confirmación de Microsoft Store.");

        var storeContext = StoreContext.GetDefault();
        ((IInitializeWithWindow)(object)storeContext).Initialize(windowHandle);
        var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
        if (updates.Count == 0)
            return StoreUpdateInstallStatus.NoUpdates;

        var result = await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        return result.OverallState switch
        {
            StorePackageUpdateState.Completed => StoreUpdateInstallStatus.Completed,
            StorePackageUpdateState.Canceled => StoreUpdateInstallStatus.Canceled,
            _ => StoreUpdateInstallStatus.Failed
        };
#else
        throw new PlatformNotSupportedException("Las actualizaciones de Microsoft Store solo están disponibles en Windows.");
#endif
    }

    public static string GetCurrentAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsMicrosoftStoreInstallation)
            throw new InvalidOperationException("Las instalaciones de Microsoft Store deben actualizarse mediante Microsoft Store, no con el instalador EXE.");

        string architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"Ultraudio-{architecture}.exe";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"Ultraudio-{architecture}.dmg";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"Ultraudio-{architecture}.AppImage";

        throw new PlatformNotSupportedException("Esta plataforma no tiene un instalador publicado.");
    }

    public static async Task DownloadFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<(long bytesRead, long? totalBytes)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await DownloadClient.GetAsync(
            downloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            progress?.Report((totalRead, totalBytes));
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("El instalador no fue encontrado.", installerPath);

        ProcessStartInfo startInfo;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(installerPath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            File.SetUnixFileMode(installerPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            startInfo = new ProcessStartInfo(installerPath) { UseShellExecute = false };
        }
        else
        {
            throw new PlatformNotSupportedException("No se puede iniciar el instalador en esta plataforma.");
        }

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("No se pudo iniciar el instalador.");
    }

    private static async Task<(UpdateStatus status, string? latestVersion)> CheckMicrosoftStoreUpdatesAsync()
    {
#if WINDOWS
        try
        {
            var updates = await StoreContext.GetDefault().GetAppAndOptionalStorePackageUpdatesAsync();
            return updates.Count > 0
                ? (UpdateStatus.MicrosoftStoreUpdateAvailable, null)
                : (UpdateStatus.MicrosoftStoreUpToDate, null);
        }
        catch (Exception ex)
        {
            Log.Warn("Update", $"Error checking Microsoft Store updates: {ex.Message}");
            return (UpdateStatus.Error, null);
        }
#else
        return (UpdateStatus.Error, null);
#endif
    }

    private static string? FindAssetUrl(JsonElement release, string assetName)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), assetName, StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var url)
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var downloadUri)
                && downloadUri.Scheme == Uri.UriSchemeHttps)
            {
                return downloadUri.AbsoluteUri;
            }
        }

        return null;
    }
}
