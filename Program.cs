using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace DualSenseBattery.App;

static class Program
{
    internal const string APP_ID = "DualSenseBattery.App";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [STAThread]
    static void Main()
    {
        // Register AUMID so ToastNotificationManager works for non-packaged desktop apps.
        // HKCU requires no admin privileges and persists across reboots.
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + APP_ID);
        key.SetValue("DisplayName", "DualSense Battery");

        SetCurrentProcessExplicitAppUserModelID(APP_ID);

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
