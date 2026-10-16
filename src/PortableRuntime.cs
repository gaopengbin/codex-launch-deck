using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using LaunchDeck.ProcessProxy;

namespace ChatGPTProxyLauncherLite
{
    internal sealed class PortableResume
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public bool Theme { get; set; }
        public string ThemeSlug { get; set; }
        public bool English { get; set; }
    }

    internal static class PortableRuntime
    {
        internal static PortableResume Resume;
        internal static void ReadArguments(string[] args)
        {
            if (args.Length == 0) return;
            if (args.Length != 2 || args[0] != "--portable-resume" || args[1].Length > 4096)
                throw new ArgumentException("Invalid portable launch arguments.");
            Resume = new JavaScriptSerializer().Deserialize<PortableResume>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1])));
            if (Resume == null || Resume.Host == null || Resume.Host.Length > 255 || Resume.Port < 1 || Resume.Port > 65535)
                throw new ArgumentException("Invalid portable launch settings.");
        }

        // The ordinary UI runs without elevation. Only the optional native mode
        // needs an immutable runtime, and its existing protected-peer checks stay intact.
        internal static bool Handoff(PortableResume settings)
        {
            if (Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\')
                .Equals(ProxyController.InstallDirectory, StringComparison.OrdinalIgnoreCase)) return false;
            string portable = Environment.GetEnvironmentVariable("LAUNCHDECK_PORTABLE_EXE");
            if (String.IsNullOrEmpty(portable) || !File.Exists(portable))
                throw new InvalidOperationException("请从免安装版 LaunchDeck.exe 打开启动器。");
            if (!Directory.Exists(ProxyController.InstallDirectory))
            {
                var info = new ProcessStartInfo(portable, "--prepare-runtime")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var process = Process.Start(info))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new InvalidOperationException("增强代理组件准备未完成；没有启动客户端。请查看刚才显示的错误。");
                }
            }
            // Verify the entire protected bundle before starting its normal-user UI.
            PortableBundleVerifier.Verify(ProxyController.InstallDirectory);
            string value = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(settings)));
            var launch = new ProcessStartInfo(Path.Combine(ProxyController.InstallDirectory, "ChatGPTProxyLauncher.exe"), "--portable-resume " + value)
            { UseShellExecute = false, WorkingDirectory = ProxyController.InstallDirectory };
            Process.Start(launch);
            return true;
        }
    }
}
