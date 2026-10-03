using Microsoft.Win32;

namespace HappyDDZ.Assistant;

internal static class QqHallInstallation
{
    private static readonly Lazy<string?> InstallRoot = new(() =>
    {
        try { return Path.GetDirectoryName(FindLauncher()); }
        catch (IOException) { return null; }
    });

    public static bool IsHallExecutable(string path)
    {
        if (!string.Equals(Path.GetFileName(path), "QQGame.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var root = InstallRoot.Value;
        if (root is null) return false;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)) return true;
        return directory is not null &&
            string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(directory).StartsWith("Hall.", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(Path.GetFileName(directory)[5..], out _);
    }

    // Use the installed launcher, which chooses the current Hall.* version.
    // Registry values are paths only, never shell command lines.
    public static string FindLauncher()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry is null || (entry.GetValue("DisplayName") as string)?.Trim() != "QQ游戏") continue;
                    var icon = ParseIconPath(entry.GetValue("DisplayIcon") as string);
                    if (icon is not null && File.Exists(icon)) return icon;
                    if (entry.GetValue("InstallLocation") is string location && Path.IsPathFullyQualified(location))
                    {
                        var candidate = Path.Combine(location, "QQGame.exe");
                        if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var candidate = Path.Combine(Environment.GetFolderPath(folder), "Tencent", "QQGameTempest", "QQGame.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("未找到已安装的 QQ 游戏大厅，请先安装官方大厅再启动助手。");
    }

    internal static string? ParseIconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma].Trim();
        path = path.Trim('"');
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetFileName(path), "QQGame.exe", StringComparison.OrdinalIgnoreCase)) return null;
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return null; }
    }
}
