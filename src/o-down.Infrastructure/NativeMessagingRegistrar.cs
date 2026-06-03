using Microsoft.Win32;
using System.Text;

namespace o_down.Infrastructure;

public static class NativeMessagingRegistrar
{
    public const string ChromeExtensionId  = "o-down-chrome-stub";
    public const string FirefoxExtensionId = "o-down-firefox-stub";
    public const string HostName           = "o_down_native_messaging";

    public static void Register(string nativeHostExePath)
    {
        var escapedPath = nativeHostExePath.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var chromeManifest = $$"""
        {
          "name": "{{HostName}}",
          "description": "o-down native messaging host",
          "path": "{{escapedPath}}",
          "type": "stdio",
          "allowed_origins": [
            "chrome-extension://{{ChromeExtensionId}}/"
          ]
        }
        """;

        var firefoxManifest = $$"""
        {
          "name": "{{HostName}}",
          "description": "o-down native messaging host",
          "path": "{{escapedPath}}",
          "type": "stdio",
          "allowed_extensions": [
            "{{FirefoxExtensionId}}@temporary-addon",
            "{{ChromeExtensionId}}@temporary-addon"
          ]
        }
        """;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var chromeDir   = Path.Combine(localAppData, "o-down", "native-messaging", "chrome");
        var firefoxDir  = Path.Combine(localAppData, "o-down", "native-messaging", "firefox");
        Directory.CreateDirectory(chromeDir);
        Directory.CreateDirectory(firefoxDir);
        File.WriteAllText(Path.Combine(chromeDir,  $"{HostName}.json"), chromeManifest, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(firefoxDir, $"{HostName}.json"), firefoxManifest, new UTF8Encoding(false));

        TryRegisterRegistry($@"SOFTWARE\Google\Chrome\NativeMessagingHosts\{HostName}", Path.Combine(chromeDir, $"{HostName}.json"));
        TryRegisterRegistry($@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts\{HostName}", Path.Combine(chromeDir, $"{HostName}.json"));
        TryRegisterRegistry($@"SOFTWARE\Mozilla\NativeMessagingHosts\{HostName}", Path.Combine(firefoxDir, $"{HostName}.json"));
    }

    public static void Unregister()
    {
        TryUnregisterRegistry($@"SOFTWARE\Google\Chrome\NativeMessagingHosts\{HostName}");
        TryUnregisterRegistry($@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts\{HostName}");
        TryUnregisterRegistry($@"SOFTWARE\Mozilla\NativeMessagingHosts\{HostName}");
    }

    public static string[] ManifestDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.Combine(localAppData, "o-down", "native-messaging", "chrome"),
            Path.Combine(localAppData, "o-down", "native-messaging", "firefox")
        };
    }

    private static void TryRegisterRegistry(string key, string value)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(key);
            k?.SetValue(string.Empty, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static void TryUnregisterRegistry(string key)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(key, false); } catch { }
    }
}
