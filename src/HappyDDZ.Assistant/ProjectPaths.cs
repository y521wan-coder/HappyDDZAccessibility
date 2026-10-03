namespace HappyDDZ.Assistant;

internal static class ProjectPaths
{
    public static string Root { get; } = FindRoot();
    public static string CaptureExe => Path.Combine(Root, "native", "bin", "WindowCapture.exe");
    public static string RapidDll => Path.Combine(Root, "vendor", "RapidV6", "Rapid.dll");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "native")) &&
                Directory.Exists(Path.Combine(dir.FullName, "vendor")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到项目目录中的 native 和 vendor 文件夹。请从完整项目包运行。 ");
    }
}
