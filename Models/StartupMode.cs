namespace BootLauncherLite.Models
{
    public enum StartupMode
    {
        RegistryRun = 0,          // HKCU\Run, normal user
        TaskSchedulerElevated = 1 // Task Scheduler, run with highest privileges
    }
}

