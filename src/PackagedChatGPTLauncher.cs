using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

// Store apps must be activated by their registered AppUserModelId. Windows
// builds their environment from HKCU\Environment, not from this helper's own
// process. Keep the registry override inside a short, serialized launch window
// and restore the exact prior values even when activation fails.
internal static class PackagedChatGPTLauncher
{
    private const string BackupPath = @"Software\GeoD\CodexProxyLaunchDeck\ProxyEnvBackup";
    private static readonly string[] ProxyKeys = {
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "NODE_USE_ENV_PROXY"
    };

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }

    private static readonly Guid ActivationManagerClsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    [STAThread]
    private static int Main(string[] args)
    {
        Uri proxy;
        if (args.Length < 1 || args.Length > 2 || !Uri.TryCreate(args[0], UriKind.Absolute, out proxy)
            || proxy.Scheme != Uri.UriSchemeHttp || String.IsNullOrEmpty(proxy.Host) || proxy.Port <= 0
            || proxy.AbsolutePath != "/")
        {
            Console.Error.WriteLine("Usage: PackagedChatGPTLauncher.exe http://host:port [cdp-port]");
            return 2;
        }

        int cdpPort = 0;
        if (args.Length == 2 && (!Int32.TryParse(args[1], out cdpPort) || cdpPort < 1 || cdpPort > 65535))
        {
            Console.Error.WriteLine("Invalid CDP port.");
            return 2;
        }

        try
        {
            using (var mutex = new Mutex(false, @"Local\CodexProxyLaunchDeckProxyEnv"))
            {
                bool ownsMutex;
                try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
                catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex)
                    throw new TimeoutException("Another ChatGPT proxy launch is in progress.");
                try
                {
                    RestorePreviousEnvironment();
                    string family = FindPackageFamily();
                    if (family == null)
                        throw new InvalidOperationException("ChatGPT/Codex Store package is not installed.");

                    SaveEnvironment();
                    uint pid;
                    try
                    {
                        SetProxyEnvironment(args[0]);
                        pid = Activate(family, args[0], cdpPort);
                    }
                    finally
                    {
                        RestorePreviousEnvironment();
                    }
                    if (pid == 0)
                        throw new InvalidOperationException("Windows did not return an application process ID.");
                    using (Process process = Process.GetProcessById((int)pid))
                    {
                        Thread.Sleep(500);
                        if (process.HasExited)
                            throw new InvalidOperationException("ChatGPT exited immediately after activation.");
                    }
                    Console.WriteLine(pid);
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message + " (0x" + error.HResult.ToString("X8") + ")");
            return 1;
        }
    }

    private static uint Activate(string family, string proxy, int cdpPort)
    {
        var manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ActivationManagerClsid));
        try
        {
            string arguments = "--proxy-server=" + proxy + " --proxy-bypass-list=localhost;127.0.0.1;[::1]";
            if (cdpPort > 0) arguments += " --remote-debugging-port=" + cdpPort;
            uint pid;
            manager.ActivateApplication(family + "!App", arguments, 0, out pid);
            return pid;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    private static string FindPackageFamily()
    {
        const string script = "$p=@(Get-AppxPackage -Name 'OpenAI.Codex';Get-AppxPackage -Name 'OpenAI.ChatGPT-Desktop')|Sort-Object Version -Descending|Select-Object -First 1;if($p){[Console]::Out.Write($p.PackageFamilyName)}";
        var start = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command \"" + script + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        using (Process process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd().Trim();
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Cannot query ChatGPT package: " + errors.Trim());
            return output.Length == 0 ? null : output;
        }
    }

    private static void SaveEnvironment()
    {
        using (RegistryKey environment = Registry.CurrentUser.CreateSubKey("Environment"))
        using (RegistryKey backup = Registry.CurrentUser.CreateSubKey(BackupPath))
        {
            foreach (string key in ProxyKeys)
            {
                object previous = environment.GetValue(key, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                backup.SetValue(key + ".present", previous == null ? 0 : 1, RegistryValueKind.DWord);
                if (previous != null)
                    backup.SetValue(key, previous, environment.GetValueKind(key));
            }
            backup.SetValue("complete", 1, RegistryValueKind.DWord);
            backup.Flush();
        }
    }

    private static void SetProxyEnvironment(string proxy)
    {
        using (RegistryKey environment = Registry.CurrentUser.CreateSubKey("Environment"))
        {
            environment.SetValue("HTTP_PROXY", proxy, RegistryValueKind.String);
            environment.SetValue("HTTPS_PROXY", proxy, RegistryValueKind.String);
            environment.SetValue("NO_PROXY", "localhost,127.0.0.1,::1", RegistryValueKind.String);
            environment.SetValue("NODE_USE_ENV_PROXY", "1", RegistryValueKind.String);
            environment.Flush();
        }
    }

    private static void RestorePreviousEnvironment()
    {
        using (RegistryKey backup = Registry.CurrentUser.OpenSubKey(BackupPath))
        {
            if (backup == null) return;
            if (Convert.ToInt32(backup.GetValue("complete", 0)) == 1)
            {
                using (RegistryKey environment = Registry.CurrentUser.CreateSubKey("Environment"))
                {
                    foreach (string key in ProxyKeys)
                    {
                        if (Convert.ToInt32(backup.GetValue(key + ".present", 0)) == 1)
                            environment.SetValue(key, backup.GetValue(key, null, RegistryValueOptions.DoNotExpandEnvironmentNames), backup.GetValueKind(key));
                        else
                            environment.DeleteValue(key, false);
                    }
                    environment.Flush();
                }
            }
        }
        Registry.CurrentUser.DeleteSubKeyTree(BackupPath, false);
    }
}
