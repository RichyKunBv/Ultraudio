using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ultraudio.Services;

public enum UpdateStatus
{
    Error,
    Outdated,
    UpToDate,
    Newer
}

public static class UpdateService
{
    public static string GetCurrentVersion()
    {
        var infoVersion = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

        if (infoVersion != null && !string.IsNullOrEmpty(infoVersion.InformationalVersion))
        {
            var ver = infoVersion.InformationalVersion.Split('+')[0];
            return ver;
        }

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.8.2";
        if (version.EndsWith(".0") && version.Split('.').Length == 4)
        {
            version = version.Substring(0, version.Length - 2);
        }
        return version;
    }
    public static async Task<(UpdateStatus status, string? latestVersion)> CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Ultraudio");
            
            var response = await client.GetAsync("https://api.github.com/repos/RichyKunBv/Ultraudio/releases/latest");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tagElement))
                {
                    string latestTag = tagElement.GetString() ?? string.Empty;
                    string latestVersionStr = latestTag.TrimStart('v', 'V');
                    
                    if (Version.TryParse(currentVersion, out var curr) && Version.TryParse(latestVersionStr, out var latest))
                    {
                        if (curr < latest) return (UpdateStatus.Outdated, latestVersionStr);
                        if (curr == latest) return (UpdateStatus.UpToDate, latestVersionStr);
                        if (curr > latest) return (UpdateStatus.Newer, latestVersionStr);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateChecker] Error checking for updates: {ex.Message}");
        }
        return (UpdateStatus.Error, null);
    }

    public static string GetDirectDownloadUrl()
    {
        string baseUrl = "https://github.com/RichyKunBv/Ultraudio/releases/latest/download/";
        string fileName = "Ultraudio";

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                fileName = "Ultraudio-arm64.dmg";
            else
                fileName = "Ultraudio-x64.dmg";
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                fileName = "Ultraudio-arm64.exe"; 
            else
                fileName = "Ultraudio-x64.exe"; 
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
                fileName = "Ultraudio-arm64.AppImage";
            else
                fileName = "Ultraudio-x64.AppImage";
        }

        return baseUrl + fileName;
    }
}