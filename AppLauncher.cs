using System.Diagnostics;
using System.IO;

public static class AppLauncher
{
    public static void LaunchNormal(string exePath, string arguments = "")
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        string workingDir = Path.GetDirectoryName(exePath)!;

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = workingDir,
            UseShellExecute = true,
        };

        Process.Start(psi);
    }

    public static void LaunchViaCmdRelay(string exePath, string arguments = "")
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        string workingDir = Path.GetDirectoryName(exePath)!;
        string exeName = Path.GetFileName(exePath);

        string cmd = $"cd /d \"{workingDir}\" && \"{exeName}\" {arguments}";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + cmd,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(psi);
    }
}
