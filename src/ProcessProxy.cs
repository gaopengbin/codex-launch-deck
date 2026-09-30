using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace LaunchDeck.ProcessProxy
{
    internal sealed class ProxyPlan
    {
        public const string Scope = "chatgpt-process-tcp-443";
        public string ExecutablePath { get; set; }
        public string ProxyHost { get; set; }
        public int ProxyPort { get; set; }
        public bool Process443Approved { get; set; }
        public bool DriverApproved { get; set; }

        public void Validate()
        {
            if (!Process443Approved || !DriverApproved)
                throw new InvalidOperationException("Process TCP 443 scope and driver use must both be explicitly approved.");
            if (String.IsNullOrWhiteSpace(ExecutablePath) || !Path.IsPathRooted(ExecutablePath)
                || ExecutablePath.IndexOfAny(new[] { '*', '?', ';', ',', '\r', '\n', '\0' }) >= 0
                || ExecutablePath.Any(c => c > 127) || ExecutablePath.Length >= 1024)
                throw new ArgumentException("A single absolute ASCII package executable path is required.");
            string full = Path.GetFullPath(ExecutablePath);
            string name = Path.GetFileName(full);
            if ((!name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase))
                || !full.EndsWith("\\app\\" + name, StringComparison.OrdinalIgnoreCase)
                || !full.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", "OpenAI."), StringComparison.OrdinalIgnoreCase)
                || !full.Equals(ExecutablePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only the resolved OpenAI Store package executable is allowed.");
            IPAddress upstream;
            if (!IPAddress.TryParse(ProxyHost, out upstream) || !IsLoopback(upstream)
                || ProxyPort < 1 || ProxyPort > 65535)
                throw new ArgumentException("The upstream must be a literal loopback address and a valid port.");
        }

        internal static bool IsLoopback(IPAddress address)
        {
            if(IPAddress.IsLoopback(address)) return true;
            byte[] bytes=address.GetAddressBytes();
            if(bytes.Length!=16 || bytes[10]!=255 || bytes[11]!=255) return false;
            for(int i=0;i<10;i++) if(bytes[i]!=0) return false;
            return bytes[12]==127;
        }
        public string Serialize() { Validate(); return new JavaScriptSerializer().Serialize(this); }
        public static ProxyPlan Parse(string json)
        {
            if (json == null || json.Length > 4096) throw new ArgumentException("Invalid plan size.");
            var plan = new JavaScriptSerializer().Deserialize<ProxyPlan>(json);
            if (plan == null) throw new ArgumentException("Missing plan.");
            plan.Validate();
            return plan;
        }

        // Preview/matching tests use an observed address, never a CDN IP allow-list.
        // Native live rules intentionally do not claim strict domain isolation.
        public bool Matches(string executable, IPAddress destination, int port, bool tcp)
        {
            Validate();
            return tcp && port == 443 && destination != null && !IsLoopback(destination)
                && !destination.Equals(IPAddress.Any) && !destination.Equals(IPAddress.IPv6Any)
                && ExecutablePath.Equals(executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal interface IProxyEngine : IDisposable
    {
        uint AddHttpProxy(string host, ushort port);
        uint AddDirectLoopbackRule();
        uint AddProcess443Rule(string executablePath, uint proxyId);
        void DeleteRule(uint id);
        void DeleteProxy(uint id);
        bool Start();
        bool Stop();
    }

    internal sealed class ProxySession : IDisposable
    {
        private readonly IProxyEngine engine;
        private readonly List<uint> rules = new List<uint>();
        private uint proxy;
        private bool engineMayBeRunning;
        private bool disposed;
        public bool Ready { get; private set; }
        public string CleanupError { get; private set; }

        public ProxySession(IProxyEngine engine) { this.engine = engine; }
        public void Start(ProxyPlan plan)
        {
            if (disposed || Ready || proxy != 0) throw new InvalidOperationException("Session cannot be reused.");
            plan.Validate();
            try
            {
                proxy = engine.AddHttpProxy(plan.ProxyHost, (ushort)plan.ProxyPort);
                if (proxy == 0) throw new InvalidOperationException("Proxy configuration failed.");
                uint direct = engine.AddDirectLoopbackRule();
                if (direct == 0) throw new InvalidOperationException("Loopback protection failed.");
                rules.Add(direct);
                uint target = engine.AddProcess443Rule(plan.ExecutablePath, proxy);
                if (target == 0) throw new InvalidOperationException("Target rule failed.");
                rules.Add(target);
                engineMayBeRunning = true; // Even a partial Start must be rolled back.
                if (!engine.Start()) throw new InvalidOperationException("The packet engine did not become ready.");
                Ready = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Ready = false;
            bool stopped = !engineMayBeRunning;
            if (engineMayBeRunning)
            {
                try { stopped = engine.Stop(); if (!stopped) CleanupError = "Engine stop was not confirmed."; }
                catch { CleanupError = "Engine stop failed."; }
            }
            // Never remove rules under a possibly active engine: deleting all rules may
            // activate an upstream default rule. Exit the owned host instead.
            if (stopped)
            {
                foreach (uint id in rules.AsEnumerable().Reverse())
                    try { engine.DeleteRule(id); } catch { CleanupError = "Rule cleanup failed."; }
                if (proxy != 0) try { engine.DeleteProxy(proxy); } catch { CleanupError = "Proxy cleanup failed."; }
            }
            try { engine.Dispose(); } catch { CleanupError = "Engine disposal failed."; }
        }
    }

    internal static class NativePins
    {
        public const string SourceCommit = "02703a0672a8b94011a4698368a392f7734c10dc";
        public static IDictionary<string,string> Sha256 { get { return NativeArtifacts.Pins(); } }
        public static void Verify(string directory)
        {
            ProxySecurity.VerifyProtectedDirectory(directory);
            foreach(var pin in Sha256)
            {
                string path=Path.Combine(directory,pin.Key);
                ProxySecurity.VerifyProtectedFile(path);
                ProxySecurity.VerifyHash(path,pin.Value);
            }
            ProxySecurity.VerifyDriver(Path.Combine(directory,"WinDivert64.sys"),NativeArtifacts.DriverSigner);
        }
    }

    internal sealed class NativeEngine : IProxyEngine
    {
        private IntPtr module;
        private bool mayBeRunning;
        private bool startAttempted;
        private readonly List<FileStream> locks = new List<FileStream>();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetDefaultDllDirectories(uint flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint AddProxy(int type, [MarshalAs(UnmanagedType.LPStr)] string host, ushort port,
            IntPtr user, IntPtr password, int domainToProxy);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint AddRule([MarshalAs(UnmanagedType.LPStr)] string process,
            [MarshalAs(UnmanagedType.LPStr)] string hosts, [MarshalAs(UnmanagedType.LPStr)] string ports,
            IntPtr domains, int protocol, int action, uint proxy);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IdOperation(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Operation();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BoolOption(int enabled);
        private AddProxy addProxy;
        private AddRule addRule;
        private IdOperation deleteRule, deleteProxy;
        private Operation start, stop;

        public NativeEngine(string directory)
        {
            directory = Path.GetFullPath(directory);
            // Pin files open against replacement while verifying and using the native engine.
            try
            {
                NativePins.Verify(directory);
                foreach (string name in NativePins.Sha256.Keys)
                    locks.Add(new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read, FileShare.Read));
                NativePins.Verify(directory);
                if (!SetDefaultDllDirectories(0x800)) throw new InvalidOperationException("Cannot restrict native DLL lookup.");
                module = LoadLibraryEx(Path.Combine(directory, "ProxyBridge.dll"), IntPtr.Zero, 0x100 | 0x800);
                if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot load the approved native engine.");
                addProxy = Bind<AddProxy>("ProxyBridge_AddProxyConfig");
                addRule = Bind<AddRule>("ProxyBridge_AddRule");
                deleteRule = Bind<IdOperation>("ProxyBridge_DeleteRule");
                deleteProxy = Bind<IdOperation>("ProxyBridge_DeleteProxyConfig");
                start = Bind<Operation>("ProxyBridge_Start");
                stop = Bind<Operation>("ProxyBridge_Stop");
                Bind<BoolOption>("ProxyBridge_SetLocalhostViaProxy")(0);
                Bind<BoolOption>("ProxyBridge_SetTrafficLoggingEnabled")(0);
            }
            catch { Dispose(); throw; }
        }
        private T Bind<T>(string name) where T : class
        {
            IntPtr pointer = GetProcAddress(module, name);
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Native API mismatch.");
            return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
        }
        public uint AddHttpProxy(string host, ushort port) { return addProxy(0, host, port, IntPtr.Zero, IntPtr.Zero, 0); }
        public uint AddDirectLoopbackRule() { return addRule("*", "127.0.0.0-127.255.255.255;::1;::ffff:127.0.0.0/104", "*", IntPtr.Zero, 2, 1, 0); }
        public uint AddProcess443Rule(string path, uint proxy) { return addRule(path, "*", "443", IntPtr.Zero, 0, 0, proxy); }
        public void DeleteRule(uint id) { if (deleteRule(id) == 0) throw new InvalidOperationException("Cannot remove rule."); }
        public void DeleteProxy(uint id) { if (deleteProxy(id) == 0) throw new InvalidOperationException("Cannot remove proxy."); }
        public bool Start() { startAttempted = true; mayBeRunning = true; return start() != 0; }
        public bool Stop() { bool success = stop() != 0; if (success) mayBeRunning = false; return success; }
        public void Dispose()
        {
            if (startAttempted || mayBeRunning) return; // Retain DLL and locks until the owned worker exits.
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
            foreach (var file in locks) file.Dispose();
            locks.Clear();
        }
    }
}
