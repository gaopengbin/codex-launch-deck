using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using ChatGPTProxyLauncherLite;

class CuaProxyForwardingTests
{
    static int checks;
    static void Assert(bool value, string name) { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name) { try { action(); } catch { Assert(true, name); return; } throw new Exception("FAIL expected rejection: " + name); }
    sealed class Fixture
    {
        internal string Root, Home, Runtime, Backups, Config, Manifest, Settings;
        internal CuaProxyForwardingMaintenance Service;
        internal Fixture(string basePath)
        {
            Root = Path.Combine(basePath, Guid.NewGuid().ToString("N")); Home = Path.Combine(Root, "home"); Runtime = Path.Combine(Root, "runtimes"); Backups = Path.Combine(Root, "backups");
            string market = Path.Combine(Home, ".tmp", "bundled-marketplaces", "openai-bundled");
            Settings = Path.Combine(Home, "config.toml"); Write(Settings, "[marketplaces.openai-bundled]\nsource_type = \"local\"\nsource = '" + market + "'\n[plugins.\"unified-computer-use@openai-bundled\"]\nenabled = true\n");
            Write(Path.Combine(market, ".agents", "plugins", "marketplace.json"), "{\"name\":\"openai-bundled\",\"plugins\":[{\"name\":\"unified-computer-use\",\"source\":{\"source\":\"local\",\"path\":\"./plugins/unified-computer-use\"}}]}");
            string descriptor = "{\"name\":\"unified-computer-use\",\"version\":\"26.930.61225\",\"mcpServers\":\"./.mcp.json\"}";
            Write(Path.Combine(market, "plugins", "unified-computer-use", ".codex-plugin", "plugin.json"), descriptor);
            string installed = Path.Combine(Home, "plugins", "cache", "openai-bundled", "unified-computer-use", "26.930.61225");
            Manifest = Path.Combine(installed, ".codex-plugin", "plugin.json"); Write(Manifest, descriptor);
            string bin = Path.Combine(Runtime, "a12345", "bin"), node = Path.Combine(bin, "node.exe"), repl = Path.Combine(bin, "node_repl.exe"), script = Path.Combine(bin, "node_modules", "@oai", "cua-repl", "bin", "cua-repl.mjs");
            Write(node, "fixture"); Write(repl, "fixture"); Write(script, "fixture");
            var server = new Dictionary<string, object> { { "command", node }, { "args", new[] { script } }, { "enabled", true }, { "env_vars", new[] { "CODEX_WINDOWS_REGISTERED_CORE", "KEEP" } }, { "future", new Dictionary<string, object> { { "number", 123 }, { "text", "中文😀" } } }, { "env", new Dictionary<string, object> { { "CUA_REPL_NODE_REPL_PATH", repl }, { "CUA_REPL_ENABLED_SURFACES", "browser" }, { "NODE_REPL_TRUSTED_SERVICES", "{\"browser\":\"@oai/browser-desktop/service\"}" } } } };
            Config = Path.Combine(installed, ".mcp.json"); Write(Config, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "mcpServers", new Dictionary<string, object> { { "cua_repl", server }, { "unrelated", new Dictionary<string, object> { { "enabled", false } } } } }, { "unknown", new[] { 1, 2, 3 } } }));
            Service = new CuaProxyForwardingMaintenance(Home, Runtime, Backups);
        }
        internal static void Write(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, value, new UTF8Encoding(false)); }
        internal void Edit(string from, string to) { File.WriteAllText(Config, File.ReadAllText(Config).Replace(from, to), new UTF8Encoding(false)); }
    }
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "launchdeck-cua-tests-" + Guid.NewGuid().ToString("N")));
        try
        {
            var f = new Fixture(root); byte[] original = File.ReadAllBytes(f.Config); var first = f.Service.Check();
            Assert(first.Missing.SequenceEqual(new[] { "HTTPS_PROXY", "NO_PROXY" }), "accurate missing list");
            var result = f.Service.Repair(first); var patched = f.Service.Check();
            Assert(result.Changed && patched.Missing.Length == 0, "repair forwards both keys");
            string beforeText = Encoding.UTF8.GetString(original), afterText = File.ReadAllText(f.Config);
            Assert(afterText == beforeText.Replace("[\"CODEX_WINDOWS_REGISTERED_CORE\",\"KEEP\"]", "[\"CODEX_WINDOWS_REGISTERED_CORE\",\"KEEP\",\"HTTPS_PROXY\",\"NO_PROXY\"]"), "all unrelated bytes and unknown fields preserved");
            DateTime stamp = File.GetLastWriteTimeUtc(f.Config); int files = Directory.GetFiles(f.Backups).Length;
            Assert(!f.Service.Repair(patched).Changed && stamp == File.GetLastWriteTimeUtc(f.Config) && Directory.GetFiles(f.Backups).Length == files, "idempotent no write and no backup");
            f.Service.Rollback(result.RecordPath); Assert(original.SequenceEqual(File.ReadAllBytes(f.Config)), "complete byte-exact rollback");
            Reject(() => f.Service.Rollback(result.RecordPath), "stale rollback refused");
            var stale = f.Service.Check(); f.Edit("\"KEEP\"", "\"CHANGED\""); byte[] changed = File.ReadAllBytes(f.Config);
            Reject(() => f.Service.Repair(stale), "stale inspection refused"); Assert(changed.SequenceEqual(File.ReadAllBytes(f.Config)), "concurrent bytes preserved");
            f = new Fixture(root); var snap = f.Service.Check(); f.Service.BeforeReplace = () => f.Edit("\"KEEP\"", "\"RACE\"");
            Reject(() => f.Service.Repair(snap), "pre-replace concurrent write refused"); Assert(File.ReadAllText(f.Config).Contains("RACE") && !File.ReadAllText(f.Config).Contains("HTTPS_PROXY"), "race not overwritten");
            f = new Fixture(root); snap = f.Service.Check(); byte[] failureOriginal = File.ReadAllBytes(f.Config); f.Service.ReplaceFile = (a,b,c) => { throw new IOException("simulated atomic replace failure"); };
            Reject(() => f.Service.Repair(snap), "atomic write failure reported"); Assert(failureOriginal.SequenceEqual(File.ReadAllBytes(f.Config)) && !Directory.GetFiles(Path.GetDirectoryName(f.Config), "*.launchdeck-tmp-*").Any(), "failure leaves original and removes temp");
            f = new Fixture(root); snap = f.Service.Check(); f.Service.ReplaceFile = (a,b,c) => { f.Edit("\"KEEP\"", "\"LATE-RACE\""); File.Replace(a,b,c); };
            Reject(() => f.Service.Repair(snap), "rename-gap race detected"); Assert(File.ReadAllText(f.Config).Contains("LATE-RACE") && !File.ReadAllText(f.Config).Contains("HTTPS_PROXY"), "late concurrent file restored");
            f = new Fixture(root); f.Edit("\"env_vars\":[\"CODEX_WINDOWS_REGISTERED_CORE\",\"KEEP\"],", ""); snap = f.Service.Check(); result = f.Service.Repair(snap); Assert(f.Service.Check().Missing.Length == 0, "missing env_vars property inserted"); f.Service.Rollback(result.RecordPath); Assert(snap.Bytes.SequenceEqual(File.ReadAllBytes(f.Config)), "insert rollback exact");
            f = new Fixture(root); var bom = new byte[] {239,187,191}.Concat(File.ReadAllBytes(f.Config)).ToArray(); File.WriteAllBytes(f.Config,bom); result = f.Service.Repair(f.Service.Check()); Assert(File.ReadAllBytes(f.Config).Take(3).SequenceEqual(bom.Take(3)), "UTF8 BOM retained"); f.Service.Rollback(result.RecordPath); Assert(bom.SequenceEqual(File.ReadAllBytes(f.Config)), "BOM rollback exact");
            f = new Fixture(root); f.Edit("\"enabled\":true", "\"enabled\":false"); Reject(() => f.Service.Check(), "disabled service refused");
            f = new Fixture(root); f.Edit("\"env_vars\":[", "\"env_vars\":true,\"other\":["); Reject(() => f.Service.Check(), "malformed env_vars refused");
            f = new Fixture(root); f.Edit("\"enabled\":true", "\"enabled\":true,\"enabled\":true"); Reject(() => f.Service.Check(), "duplicate JSON keys refused");
            f = new Fixture(root); File.WriteAllText(f.Config, "{oops"); Reject(() => f.Service.Check(), "invalid JSON refused");
            f = new Fixture(root); File.WriteAllText(f.Manifest, File.ReadAllText(f.Manifest).Replace("26.930.61225", "99.0.0")); Reject(() => f.Service.Check(), "installed version mismatch refused");
            f = new Fixture(root); File.WriteAllText(f.Settings, File.ReadAllText(f.Settings).Replace("enabled = true", "enabled = false")); Reject(() => f.Service.Check(), "disabled plugin refused");
            f = new Fixture(root); File.WriteAllText(f.Settings, File.ReadAllText(f.Settings).Replace("bundled-marketplaces", "..\\elsewhere")); Reject(() => f.Service.Check(), "marketplace traversal refused");
            f = new Fixture(root); f.Edit("@oai/browser-desktop/service", "@untrusted/service"); Reject(() => f.Service.Check(), "service identity mismatch refused");
            f = new Fixture(root); result = f.Service.Repair(f.Service.Check()); File.AppendAllText(Directory.GetFiles(f.Backups, "*.original.json").Single(), " "); Reject(() => f.Service.Rollback(result.RecordPath), "tampered rollback hash refused");
            f = new Fixture(root); Reject(() => f.Service.Rollback(Path.Combine(root, "foreign.record.json")), "rollback path outside backup root refused");
            f = new Fixture(root); f.Edit("\"unknown\":[1,2,3]", "\"unknown\":[1,2,3],\"mcpServers/cua_repl\":{\"env_vars\":[\"UNRELATED\"]}"); var slashOriginal = File.ReadAllText(f.Config); f.Service.Repair(f.Service.Check()); Assert(File.ReadAllText(f.Config) == slashOriginal.Replace("[\"CODEX_WINDOWS_REGISTERED_CORE\",\"KEEP\"]", "[\"CODEX_WINDOWS_REGISTERED_CORE\",\"KEEP\",\"HTTPS_PROXY\",\"NO_PROXY\"]"), "slash-containing unknown keys cannot redirect patch");
            f = new Fixture(root); snap = f.Service.Check(); File.WriteAllText(f.Backups, "occupied"); Reject(() => f.Service.Repair(snap), "backup creation failure refused"); Assert(snap.Bytes.SequenceEqual(File.ReadAllBytes(f.Config)), "backup failure leaves config exact");
            f = new Fixture(root); result = f.Service.Repair(f.Service.Check()); byte[] beforeRollbackFailure = File.ReadAllBytes(f.Config); f.Service.ReplaceFile = (a,b,c) => { throw new IOException("simulated rollback failure"); }; Reject(() => f.Service.Rollback(result.RecordPath), "rollback write failure reported"); Assert(beforeRollbackFailure.SequenceEqual(File.ReadAllBytes(f.Config)), "failed rollback preserves repaired file");
            f = new Fixture(root); string originalDirectory = Path.GetDirectoryName(f.Config), redirected = Path.Combine(f.Root,"redirected"); Directory.Move(originalDirectory,redirected);
            var junction = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe"), "/d /c mklink /J \""+originalDirectory+"\" \""+redirected+"\"") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var p=System.Diagnostics.Process.Start(junction)){p.StandardOutput.ReadToEnd();p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception("Fixture junction creation failed.");}
            Reject(() => f.Service.Check(), "redirected plugin directory refused");
            // A second cached version must not influence selection of authoritative marketplace version.
            f = new Fixture(root); Directory.CreateDirectory(Path.Combine(f.Home, "plugins", "cache", "openai-bundled", "unified-computer-use", "99.0.0")); Assert(f.Service.Check().Version == "26.930.61225", "cached newest version not guessed");
            f = new Fixture(root); f.Edit("\"CUA_REPL_NODE_REPL_PATH\":", "\"BROWSER_USE_TINYSKY_ENABLED\":\"1\",\"CUA_REPL_NODE_REPL_PATH\":");
            byte[] browserOriginal=File.ReadAllBytes(f.Config); int runtimeChecks=0;
            f.Service.VerifyBrowserRuntime=bin=>{runtimeChecks++;};
            snap=f.Service.Check(); Assert(snap.BrowserReadMode=="1", "browser read mode inspected");
            result=f.Service.RepairBrowserRead(snap);
            Assert(result.Changed && f.Service.Check().BrowserReadMode=="0" && runtimeChecks==1, "reviewed browser fallback configured");
            Assert(File.ReadAllText(f.Config)==Encoding.UTF8.GetString(browserOriginal).Replace("\"BROWSER_USE_TINYSKY_ENABLED\":\"1\"","\"BROWSER_USE_TINYSKY_ENABLED\":\"0\""), "compatibility changes only the exact selected flag");
            Assert(f.Service.Check().Missing.Length==2, "browser repair does not silently change proxy or permissions");
            Assert(!f.Service.RepairBrowserRead(f.Service.Check()).Changed && runtimeChecks==1, "browser repair idempotent");
            f.Service.Rollback(result.RecordPath); Assert(browserOriginal.SequenceEqual(File.ReadAllBytes(f.Config)), "browser repair exact rollback");
            f.Service.VerifyBrowserRuntime=bin=>{throw new IOException("unknown runtime");};
            Reject(()=>f.Service.RepairBrowserRead(f.Service.Check()), "unreviewed browser runtime refused");
            Assert(browserOriginal.SequenceEqual(File.ReadAllBytes(f.Config)), "unknown runtime leaves configuration unchanged");
            f = new Fixture(root); Reject(()=>f.Service.RepairBrowserRead(f.Service.Check()), "missing browser compatibility flag refused");
            f = new Fixture(root); f.Edit("\"CUA_REPL_NODE_REPL_PATH\":", "\"BROWSER_USE_TINYSKY_ENABLED\":\"2\",\"CUA_REPL_NODE_REPL_PATH\":");
            Reject(()=>f.Service.RepairBrowserRead(f.Service.Check()), "unknown browser mode refused");
            f = new Fixture(root); f.Edit("\"CUA_REPL_NODE_REPL_PATH\":", "\"BROWSER_USE_TINYSKY_ENABLED\":\"1\",\"CUA_REPL_NODE_REPL_PATH\":");
            f.Service.VerifyBrowserRuntime=bin=>{}; snap=f.Service.Check();f.Edit("\"KEEP\"","\"UPDATED\"");
            Reject(()=>f.Service.RepairBrowserRead(snap), "stale browser repair snapshot refused");
            f = new Fixture(root); f.Edit("\"CUA_REPL_NODE_REPL_PATH\":", "\"BROWSER_USE_TINYSKY_ENABLED\":\"1\",\"CUA_REPL_NODE_REPL_PATH\":");
            Reject(()=>f.Service.RepairBrowserRead(f.Service.Check()), "production runtime verifier refuses fixture binaries");
            Console.WriteLine("PASS " + checks + " checks. Fixtures retained: " + root); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Fixtures retained: " + root); return 1; }
    }
}
