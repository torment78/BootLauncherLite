

namespace BootLauncherLite.Services
{
    public class FileDialogService
    {
        public string? BrowseForExecutableOrScript()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Programs and Scripts|*.exe;*.bat;*.cmd;*.ps1|All files|*.*",
                CheckFileExists = true
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}

