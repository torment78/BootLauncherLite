using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


namespace BootLauncherLite.Services
{
    public class LaunchSequenceService
    {
        private readonly LogService _logService;

        // Global default: wait 5 seconds after launch before we start minimizing
        private const int DefaultMinimizeInitialDelayMs = 5000;

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private async Task<bool> TryMinimizeProcessWindowAsync(
            Process proc,
            int timeoutMs,
            bool force)
        {
            try
            {
                var sw = Stopwatch.StartNew();

                while (!proc.HasExited && sw.ElapsedMilliseconds < timeoutMs)
                {
                    proc.Refresh();
                    var hwnd = proc.MainWindowHandle;

                    if (hwnd != IntPtr.Zero)
                    {
                        // 2 = SW_MINIMIZE
                        ShowWindowAsync(hwnd, 2);
                        _logService.Log($"Minimized process {proc.ProcessName} ({proc.Id})");
                        return true;
                    }

                    await Task.Delay(500);
                }

                _logService.Log(
                    $"Failed to minimize {proc.ProcessName} ({proc.Id}) within {timeoutMs}ms");
                return false;
            }
            catch (Exception ex)
            {
                _logService.Log($"Error in TryMinimizeProcessWindowAsync: {ex}");
                return false;
            }
        }

        public LaunchSequenceService()
        {
            _logService = new LogService();
        }

        public void StartProcess(LaunchItem item)
        {
            if (item == null)
                return;

            // Helper: normalize per-item delay (nullable-safe)
            int GetInitialDelayMs(LaunchItem li)
            {
                int value = li.MinimizeInitialDelayMs ?? DefaultMinimizeInitialDelayMs;
                if (value <= 0)
                    value = DefaultMinimizeInitialDelayMs;
                return value;
            }

            try
            {
                // === Kill-only items ===
                if (item.KillInsteadOfLaunch)
                {
                    KillByNameOrPath(item);
                    return;
                }

                // === Launch items ===
                if (string.IsNullOrWhiteSpace(item.FullPath))
                {
                    _logService.Log("StartProcess: Skipped item with empty path.");
                    return;
                }

                string path = item.FullPath.Trim();
                string? workingDir;

                try
                {
                    workingDir = Path.GetDirectoryName(path);
                    if (string.IsNullOrWhiteSpace(workingDir))
                        workingDir = Environment.CurrentDirectory;
                }
                catch
                {
                    workingDir = Environment.CurrentDirectory;
                }

                bool launcherIsElevated = MainWindow.IsElevated();
                bool itemWantsAdmin = item.RunAsAdmin;

                // Relay is *only* for explicit command-relay items
                bool needsRelay = item.UseCmdRelay;
                bool hasArgs = !string.IsNullOrWhiteSpace(item.Arguments);

                _logService.Log(
                    $"StartProcess: {item.DisplayName}, " +
                    $"LauncherElevated={launcherIsElevated}, ItemRunAsAdmin={itemWantsAdmin}, " +
                    $"UseCmdRelay={item.UseCmdRelay}, ForceMinimize={item.ForceMinimize}, StartMinimized={item.StartMinimized}");

                // ===================================================
                //  CASE A: Launcher is ELEVATED
                // ===================================================
                if (launcherIsElevated)
                {
                    // Figure out what kind of file this is
                    string ext = string.Empty;
                    try
                    {
                        ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
                    }
                    catch { /* ignore */ }

                    bool isExeLike = ext == ".exe" || ext == ".bat" || ext == ".cmd";
                    bool anyMinimizeFlag = item.StartMinimized || item.ForceMinimize;

                    if (itemWantsAdmin)
                    {
                        // App also wants admin -> just start it normally (inherits elevated token)
                        var psiAdmin = new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true,
                            WorkingDirectory = workingDir
                        };

                        if (hasArgs)
                            psiAdmin.Arguments = item.Arguments;

                        _logService.Log("StartProcess: elevated -> launching with inherited admin token.");
                        var procAdmin = Process.Start(psiAdmin);

                        // Even in admin mode we can still force-minimize if requested
                        if (procAdmin != null && anyMinimizeFlag)
                        {
                            int initialDelayMs = GetInitialDelayMs(item);
                            int timeoutMs = initialDelayMs + 15000; // try for 15s after that

                            _logService.Log(
                                $"[{item.DisplayName}] elevated minimize: initialDelay={initialDelayMs}ms, timeout={timeoutMs}ms");

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(initialDelayMs);
                                    await TryMinimizeProcessWindowAsync(procAdmin, timeoutMs, item.ForceMinimize);
                                }
                                catch (Exception ex)
                                {
                                    _logService.Log($"Minimize task failed (elevated/admin) for {item.DisplayName}: {ex.Message}");
                                }
                            });
                        }

                        return;
                    }
                    else
                    {
                        // SAFE case for explorer de-elevate:
                        //  - exe/bat/cmd
                        //  - NO custom args
                        //  - NO minimize flags
                        //  - NO command relay
                        if (isExeLike && !hasArgs && !needsRelay && !item.StartMinimized && !item.ForceMinimize)
                        {
                            string argLine = $"\"{path}\"";

                            var psiUser = new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = argLine,
                                UseShellExecute = true
                            };

                            _logService.Log($"StartProcess: elevated -> launching non-admin via explorer.exe {argLine}");
                            Process.Start(psiUser);
                            return;
                        }

                        // Otherwise: run directly so the app sees its arguments
                        var psiNormal = new ProcessStartInfo
                        {
                            FileName = path,
                            UseShellExecute = true,
                            WorkingDirectory = workingDir
                        };

                        if (hasArgs)
                            psiNormal.Arguments = item.Arguments;

                        _logService.Log("StartProcess: elevated -> launching directly (args and/or flags present).");

                        var proc = Process.Start(psiNormal);

                        // Apply minimize in elevated mode as well
                        if (proc != null && (item.StartMinimized || item.ForceMinimize))
                        {
                            int initialDelayMs = GetInitialDelayMs(item);
                            int timeoutMs = initialDelayMs + 15000;

                            _logService.Log(
                                $"[{item.DisplayName}] elevated minimize: initialDelay={initialDelayMs}ms, timeout={timeoutMs}ms");

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(initialDelayMs);
                                    await TryMinimizeProcessWindowAsync(proc, timeoutMs, item.ForceMinimize);
                                }
                                catch (Exception ex)
                                {
                                    _logService.Log($"Minimize task failed (elevated/direct) for {item.DisplayName}: {ex.Message}");
                                }
                            });
                        }

                        return;
                    }
                }

                // ===================================================
                //  CASE B: Launcher is NOT elevated
                // ===================================================

                // If this item explicitly needs the PowerShell relay (special shell case),
                // we use that – *independent* of ForceMinimize.
                if (needsRelay)
                {
                    RunViaPowerShellRelay(item, path, workingDir);
                    return;
                }

                // Normal non-elevated start
                var psiPlain = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    WorkingDirectory = workingDir,
                    WindowStyle = item.StartMinimized
                        ? ProcessWindowStyle.Minimized
                        : ProcessWindowStyle.Normal
                };

                if (hasArgs)
                    psiPlain.Arguments = item.Arguments;

                if (itemWantsAdmin)
                {
                    psiPlain.Verb = "runas";
                }

                var procPlain = Process.Start(psiPlain);

                // Non-elevated: use the same WinAPI minimize helper so
                // ForceMinimize behaves identical to elevated mode.
                if (procPlain != null && (item.StartMinimized || item.ForceMinimize))
                {
                    int initialDelayMs = GetInitialDelayMs(item);
                    int timeoutMs = initialDelayMs + 15000;

                    _logService.Log(
                        $"[{item.DisplayName}] non-elevated minimize: initialDelay={initialDelayMs}ms, timeout={timeoutMs}ms");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(initialDelayMs);
                            await TryMinimizeProcessWindowAsync(procPlain, timeoutMs, item.ForceMinimize);
                        }
                        catch (Exception ex)
                        {
                            _logService.Log($"Minimize task failed (non-elevated) for {item.DisplayName}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logService.Log($"StartProcess error for {item?.DisplayName}: {ex}");
            }
        }


        private void RunViaPowerShellRelay(LaunchItem item, string path, string workingDir)
        {
            try
            {
                string exe = path;
                string args = item.Arguments ?? string.Empty;

                // Basic sanity check: if path is actually a folder, warn – Windows will just open Explorer.
                if (!File.Exists(exe) && Directory.Exists(exe))
                {
                    _logService.Log(
                        $"RunViaPowerShellRelay: '{exe}' is a directory, not a file. " +
                        "Windows will open Explorer – consider pointing to the actual .exe.");
                }

                // Escape for PowerShell single-quoted strings
                string escExe = exe.Replace("'", "''");
                string escArgs = args.Replace("'", "''");
                string escWd = workingDir.Replace("'", "''");

                // Per-item minimize initial delay in seconds for PowerShell (nullable-safe, no duplicate)
                int initialDelayMs = item.MinimizeInitialDelayMs ?? DefaultMinimizeInitialDelayMs;
                if (initialDelayMs <= 0)
                    initialDelayMs = DefaultMinimizeInitialDelayMs;
                int initialDelaySec = initialDelayMs / 1000;

                var sb = new StringBuilder();

                sb.Append("$exe = '").Append(escExe).Append("';");
                sb.Append("$args = '").Append(escArgs).Append("';");
                sb.Append("$wd = '").Append(escWd).Append("';");
                sb.Append("$psi = New-Object System.Diagnostics.ProcessStartInfo;");
                sb.Append("$psi.FileName = $exe;");
                sb.Append("$psi.Arguments = $args;");
                sb.Append("$psi.WorkingDirectory = $wd;");
                sb.Append("$psi.UseShellExecute = $true;");
                sb.Append("$proc = [System.Diagnostics.Process]::Start($psi);");

                // Only bother with minimize logic if requested
                if (item.ForceMinimize || item.StartMinimized)
                {
                    if (initialDelaySec > 0)
                    {
                        sb.Append("Start-Sleep -Seconds ").Append(initialDelaySec).Append(";");
                    }

                    sb.Append(@"
if ($null -ne $proc) {
    try { $proc.WaitForInputIdle(5000) | Out-Null } catch {}
    $proc.Refresh()
    $hwnd = $proc.MainWindowHandle
    if ($hwnd -ne 0) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WinUtil {
    [DllImport(""user32.dll"")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}
'@

        # 2 = SW_MINIMIZE
        [WinUtil]::ShowWindowAsync($hwnd, 2) | Out-Null
    }
}
");
                }

                // Build final PowerShell -Command string
                string psScript = sb.ToString();

                var psiRelay = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" +
                        psScript.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _logService.Log($"RunViaPowerShellRelay: launching {item.DisplayName} via PowerShell relay.");
                Process.Start(psiRelay);
            }
            catch (Exception ex)
            {
                _logService.Log($"RunViaPowerShellRelay error for {item?.DisplayName}: {ex}");
            }
        }

        // KillByNameOrPath unchanged...
        private void KillByNameOrPath(LaunchItem item)
        {
            try
            {
                string? nameForKill = null;

                if (!string.IsNullOrWhiteSpace(item.FullPath))
                {
                    try
                    {
                        nameForKill = Path.GetFileNameWithoutExtension(item.FullPath);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (string.IsNullOrWhiteSpace(nameForKill))
                {
                    _logService.Log("KillByNameOrPath: no process name found.");
                    return;
                }

                var processes = Process.GetProcessesByName(nameForKill);
                int killCount = 0;

                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill();
                        killCount++;
                    }
                    catch (Exception ex)
                    {
                        _logService.Log(
                            $"KillByNameOrPath: failed to kill {nameForKill} (PID={p.Id}): {ex.Message}");
                    }
                }

                _logService.Log($"KillByNameOrPath: killed {killCount} instance(s) of {nameForKill}.");
            }
            catch (Exception ex)
            {
                _logService.Log($"KillByNameOrPath error: {ex}");
            }
        }
    }
}

