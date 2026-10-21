using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ChatGPTProxyLauncherLite
{
    // The application regenerates plugin configuration after startup and updates.
    // Reapply only missing proxy forwarding keys; every write uses the existing
    // version, path, backup, byte-preservation and concurrent-write checks.
    internal sealed class CuaProxyForwardingWatch : IDisposable
    {
        private readonly object gate=new object();
        private readonly Action reconcile;
        private readonly System.Threading.Timer timer;
        private bool disposed;
        internal string LastError { get; private set; }
        internal CuaProxyForwardingWatch(Action action, int intervalMs=2000)
        {
            reconcile=action;
            timer=new System.Threading.Timer(_=>ReconcileOnce(),null,System.Threading.Timeout.Infinite,intervalMs);
            ReconcileOnce();
            timer.Change(intervalMs,intervalMs);
        }
        internal static CuaProxyForwardingWatch CreateDefault()
        {
            var maintenance=CuaProxyForwardingMaintenance.CreateDefault();
            return new CuaProxyForwardingWatch(()=> {
                var state=maintenance.Check();
                if(state.Missing.Length>0) maintenance.Repair(state);
            });
        }
        internal void ReconcileOnce()
        {
            if(!System.Threading.Monitor.TryEnter(gate)) return;
            try {
                if(disposed) return;
                try {reconcile(); LastError=null;}
                catch(Exception error) {LastError=error.GetType().Name+": "+error.Message;}
            } finally {System.Threading.Monitor.Exit(gate);}
        }
        public void Dispose()
        {
            lock(gate) {disposed=true; timer.Dispose();}
        }
    }
    // Validated configuration maintenance, shared by manual repair and the
    // forwarding watcher. No permission change, runtime-code patch or termination.
    internal sealed class CuaProxyForwardingMaintenance
    {
        internal sealed class Inspection
        {
            internal string Path, Version, Hash, RuntimeBin, BrowserReadMode;
            internal string[] Missing;
            internal byte[] Bytes;
            internal string Summary { get { return "Runtime: " + Version + "\r\nConfig: " + Path + "\r\nSHA256: " + Hash + "\r\nBrowser read: " + BrowserReadMode + " (0 = DOM compatibility; 1 = TinySky)\r\n" + (Missing.Length == 0 ? "HTTPS_PROXY / NO_PROXY: forwarded (no write needed)." : "Missing forwarding: " + String.Join(", ", Missing)); } }
        }
        internal sealed class RepairResult { internal bool Changed; internal string RecordPath; }
        private readonly string home, runtimeRoot, backupRoot;
        internal Action BeforeReplace = null;
        internal Action<string> VerifyBrowserRuntime = VerifyKnownBrowserRuntime;
        internal Action<string, string, string> ReplaceFile;
        internal string BackupRoot { get { return backupRoot; } }
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1048576 };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static CuaProxyForwardingMaintenance CreateDefault()
        {
            string selected = Environment.GetEnvironmentVariable("CODEX_HOME");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new CuaProxyForwardingMaintenance(String.IsNullOrWhiteSpace(selected) ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : selected,
                System.IO.Path.Combine(local, "OpenAI", "Codex", "runtimes", "cua_node"),
                System.IO.Path.Combine(local, "LaunchDeck", "CuaForwardingBackups"));
        }
        internal CuaProxyForwardingMaintenance(string codexHome, string runtimes, string backups)
        {
            home = Canonical(codexHome); runtimeRoot = Canonical(runtimes); backupRoot = Canonical(backups);
            ReplaceFile = delegate(string temp, string target, string displaced) { File.Replace(temp, target, displaced); };
        }
        private static string Canonical(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException("Missing path.");
            if (path.StartsWith(@"\\?\")) path = path.Substring(4);
            if (!System.IO.Path.IsPathRooted(path) || path.StartsWith(@"\\")) throw new IOException("Local absolute path required.");
            return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }
        private static void SafePath(string path, string root)
        {
            string full = Canonical(path), basePath = Canonical(root);
            if (!full.Equals(basePath, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(basePath + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Path outside expected root.");
            for (string current = full; !String.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected path refused.");
        }
        private byte[] Read(string path) { if (new FileInfo(path).Length > 1048576) throw new IOException("Configuration too large."); return File.ReadAllBytes(path); }
        internal static string Hash(byte[] bytes) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Text(byte[] bytes) { return Utf8.GetString(bytes).TrimStart('\uFEFF'); }
        private Dictionary<string, object> Object(object value) { var result = value as Dictionary<string, object>; if (result == null) throw new IOException("Unexpected JSON object."); return result; }
        private Dictionary<string, object> Parse(byte[] bytes) { string text = Text(bytes); new JsonSpan(text).Scan(); return Object(json.DeserializeObject(text)); }
        private static string StringValue(Dictionary<string, object> value, string key) { object raw; if (!value.TryGetValue(key, out raw) || !(raw is string)) throw new IOException("Missing string: " + key); return (string)raw; }
        private static bool Enabled(Dictionary<string, object> value) { object raw; return value.TryGetValue("enabled", out raw) && raw is bool && (bool)raw; }
        private static string TomlSection(string text, string header)
        {
            var matches = Regex.Matches(text, @"(?m)^\s*\[" + Regex.Escape(header) + @"\]\s*(?:#.*)?$");
            if (matches.Count != 1) throw new IOException("Missing or ambiguous configuration section: " + header);
            int start = matches[0].Index + matches[0].Length;
            var next = Regex.Match(text.Substring(start), @"(?m)^\s*\[");
            return next.Success ? text.Substring(start, next.Index) : text.Substring(start);
        }
        private string TomlString(string section, string key)
        {
            var matches = Regex.Matches(section, @"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*('([^'\r\n]*)'|""(?:[^""\\\r\n]|\\.)*"")\s*(?:#.*)?$");
            if (matches.Count != 1) throw new IOException("Missing or unsupported TOML value: " + key);
            string raw = matches[0].Groups[1].Value;
            return raw[0] == '\'' ? raw.Substring(1, raw.Length - 2) : json.Deserialize<string>(raw);
        }
        internal Inspection Check()
        {
            SafePath(home, home);
            string settings = System.IO.Path.Combine(home, "config.toml"); SafePath(settings, home);
            string config = Text(Read(settings));
            string pluginSection = TomlSection(config, "plugins.\"unified-computer-use@openai-bundled\"");
            if (Regex.Matches(pluginSection, @"(?m)^\s*enabled\s*=\s*true\s*(?:#.*)?$").Count != 1 || Regex.Matches(pluginSection, @"(?m)^\s*enabled\s*=").Count != 1) throw new IOException("Unified computer use plugin is not explicitly enabled.");
            string marketplaceSection = TomlSection(config, "marketplaces.openai-bundled");
            if (TomlString(marketplaceSection, "source_type") != "local") throw new IOException("Expected local bundled marketplace.");
            string marketplace = Canonical(TomlString(marketplaceSection, "source"));
            string expectedMarketplace = System.IO.Path.Combine(home, ".tmp", "bundled-marketplaces", "openai-bundled");
            if (!marketplace.Equals(expectedMarketplace, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsupported bundled marketplace path; no version guessed.");
            SafePath(marketplace, home);
            string marketFile = System.IO.Path.Combine(marketplace, ".agents", "plugins", "marketplace.json"); SafePath(marketFile, marketplace);
            var market = Parse(Read(marketFile));
            if (StringValue(market, "name") != "openai-bundled") throw new IOException("Marketplace identity mismatch.");
            var plugins = market["plugins"] as object[]; if (plugins == null) throw new IOException("Unexpected marketplace plugins.");
            var entries = plugins.Select(Object).Where(p => StringValue(p, "name") == "unified-computer-use").ToArray();
            if (entries.Length != 1) throw new IOException("Ambiguous bundled plugin.");
            var source = Object(entries[0]["source"]);
            if (StringValue(source, "source") != "local" || StringValue(source, "path") != "./plugins/unified-computer-use") throw new IOException("Unexpected bundled plugin source.");
            string templateManifest = System.IO.Path.Combine(marketplace, "plugins", "unified-computer-use", ".codex-plugin", "plugin.json"); SafePath(templateManifest, marketplace);
            var template = Parse(Read(templateManifest));
            string version = StringValue(template, "version");
            if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") || StringValue(template, "name") != "unified-computer-use" || StringValue(template, "mcpServers") != "./.mcp.json") throw new IOException("Unsupported plugin manifest.");
            string pluginPath = System.IO.Path.Combine(home, "plugins", "cache", "openai-bundled", "unified-computer-use", version);
            SafePath(pluginPath, home);
            string manifestPath = System.IO.Path.Combine(pluginPath, ".codex-plugin", "plugin.json"); SafePath(manifestPath, pluginPath);
            var manifest = Parse(Read(manifestPath));
            if (StringValue(manifest, "name") != "unified-computer-use" || StringValue(manifest, "version") != version || StringValue(manifest, "mcpServers") != "./.mcp.json") throw new IOException("Installed plugin identity mismatch.");
            string path = System.IO.Path.Combine(pluginPath, ".mcp.json"); SafePath(path, pluginPath);
            byte[] bytes = Read(path); var root = Parse(bytes); var server = Object(Object(root["mcpServers"])["cua_repl"]);
            if (!Enabled(server)) throw new IOException("cua_repl service is disabled.");
            string command = Canonical(StringValue(server, "command")); SafePath(command, runtimeRoot);
            string bin = System.IO.Path.GetDirectoryName(command);
            if (System.IO.Path.GetFileName(command) != "node.exe" || System.IO.Path.GetFileName(bin) != "bin" || !System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(bin)).Equals(runtimeRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected CUA node runtime.");
            var args = server["args"] as object[];
            string script = System.IO.Path.Combine(bin, "node_modules", "@oai", "cua-repl", "bin", "cua-repl.mjs"); SafePath(script, runtimeRoot);
            if (args == null || args.Length != 1 || !(args[0] is string) || !Canonical((string)args[0]).Equals(script, StringComparison.OrdinalIgnoreCase) || !File.Exists(command) || !File.Exists(script)) throw new IOException("Unexpected CUA launch entry.");
            var env = Object(server["env"]);
            string repl = Canonical(StringValue(env, "CUA_REPL_NODE_REPL_PATH")); SafePath(repl, runtimeRoot);
            if (!repl.Equals(System.IO.Path.Combine(bin, "node_repl.exe"), StringComparison.OrdinalIgnoreCase) || !File.Exists(repl) || !StringValue(env, "CUA_REPL_ENABLED_SURFACES").Split(',').Contains("browser")) throw new IOException("Unexpected CUA service environment.");
            var services = Object(json.DeserializeObject(StringValue(env, "NODE_REPL_TRUSTED_SERVICES")));
            if (StringValue(services, "browser") != "@oai/browser-desktop/service") throw new IOException("Unexpected browser service identity.");
            object raw; string[] names = new string[0];
            if (server.TryGetValue("env_vars", out raw)) { var list = raw as object[]; if (list == null || list.Any(x => !(x is string))) throw new IOException("Unexpected env_vars list."); names = list.Cast<string>().ToArray(); }
            object readMode;
            return new Inspection { Path = path, Version = version, Bytes = bytes, Hash = Hash(bytes), RuntimeBin = bin,
                BrowserReadMode = env.TryGetValue("BROWSER_USE_TINYSKY_ENABLED", out readMode) && readMode is string ? (string)readMode : "unavailable",
                Missing = new[] { "HTTPS_PROXY", "NO_PROXY" }.Where(x => !names.Contains(x)).ToArray() };
        }
        private static void VerifyKnownBrowserRuntime(string bin)
        {
            var pins = new Dictionary<string,string[]> {
                { @"node_modules\@oai\cua\dist\lib\js\oai_js_cua\src\tinysky_alt\bind_tab.js", new[]{"a693e078ecbaa34020cf0caa373edb9c525dad87fd3ada62706a07eadaa6dc2a"} },
                { @"node_modules\@oai\browser-desktop\scripts\browser-service.mjs", new[]{
                    "be80dd92f34c1c912f2061bcedbc366fd422190f80077e90a7b63c873f6ff3f3", // 26.1007.71326
                    "44594ea0a8238c09dfb46e28acdd610e5053a05011047e1c78fb98f210195a4e"  // 26.1013.31256: reviewed flag/fallback semantics unchanged
                } }
            };
            foreach (var pin in pins) {
                string path = System.IO.Path.Combine(bin,pin.Key); SafePath(path,bin);
                if (!File.Exists(path) || new FileInfo(path).Length > 33554432 || !pin.Value.Contains(Hash(File.ReadAllBytes(path))))
                    throw new IOException("Browser runtime changed; compatibility repair requires review of this version.");
            }
        }
        internal RepairResult RepairBrowserRead(Inspection expected)
        {
            var current = Check();
            if (expected == null || current.Path != expected.Path || current.Hash != expected.Hash)
                throw new IOException("Configuration changed. Check again before repair.");
            if (current.BrowserReadMode == "0") return new RepairResult { Changed = false };
            if (current.BrowserReadMode != "1") throw new IOException("Browser read compatibility is unavailable in this configuration.");
            VerifyBrowserRuntime(current.RuntimeBin);
            string text = Text(current.Bytes);
            var span = new JsonSpan(text, "/mcpServers/cua_repl/env/BROWSER_USE_TINYSKY_ENABLED"); span.Scan();
            if (span.Start < 0 || text.Substring(span.Start,span.End-span.Start) != "\"1\"") throw new IOException("Unsupported browser read flag.");
            string patched = text.Substring(0,span.Start) + "\"0\"" + text.Substring(span.End);
            byte[] body = Utf8.GetBytes(patched);
            byte[] output = current.Bytes.Take(3).SequenceEqual(new byte[] {239,187,191}) ? new byte[] {239,187,191}.Concat(body).ToArray() : body;
            return CommitRepair(current,output);
        }
        internal RepairResult Repair(Inspection expected)
        {
            var current = Check();
            if (expected == null || current.Path != expected.Path || current.Hash != expected.Hash) throw new IOException("Configuration changed. Check again before repair.");
            if (current.Missing.Length == 0) return new RepairResult { Changed = false };
            string text = Text(current.Bytes); var span = new JsonSpan(text); span.Scan();
            var root = Parse(current.Bytes); var server = Object(Object(root["mcpServers"])["cua_repl"]); object raw;
            var names = server.TryGetValue("env_vars", out raw) ? ((object[])raw).Cast<string>().ToList() : new List<string>();
            names.AddRange(current.Missing);
            string value = json.Serialize(names.Distinct().ToArray());
            string patched = span.Start >= 0 ? text.Substring(0, span.Start) + value + text.Substring(span.End) : text.Insert(span.ServiceEnd, ",\"env_vars\":" + value);
            byte[] body = Utf8.GetBytes(patched); byte[] output = current.Bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }) ? new byte[] { 239, 187, 191 }.Concat(body).ToArray() : body;
            return CommitRepair(current,output);
        }
        private RepairResult CommitRepair(Inspection current, byte[] output)
        {
            Parse(output);
            SafePath(backupRoot, backupRoot); Directory.CreateDirectory(backupRoot);
            string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" + Guid.NewGuid().ToString("N");
            string original = System.IO.Path.Combine(backupRoot, id + ".original.json"), record = System.IO.Path.Combine(backupRoot, id + ".record.json");
            WriteNew(original, current.Bytes);
            var metadata = new Dictionary<string, object> { { "target", current.Path }, { "version", current.Version }, { "original", System.IO.Path.GetFileName(original) }, { "before", current.Hash }, { "after", Hash(output) } };
            WriteNew(record, Utf8.GetBytes(json.Serialize(metadata)));
            Swap(current.Path, current.Bytes, output);
            return new RepairResult { Changed = true, RecordPath = record };
        }
        internal void Rollback(string recordPath)
        {
            SafePath(recordPath, backupRoot); var record = Parse(Read(recordPath));
            var current = Check();
            if (StringValue(record, "target") != current.Path || StringValue(record, "version") != current.Version || StringValue(record, "after") != current.Hash) throw new IOException("Rollback refused: current configuration differs from this repair.");
            string name = StringValue(record, "original"); if (System.IO.Path.GetFileName(name) != name || !name.EndsWith(".original.json")) throw new IOException("Invalid rollback filename.");
            string original = System.IO.Path.Combine(backupRoot, name); SafePath(original, backupRoot); byte[] bytes = Read(original);
            if (Hash(bytes) != StringValue(record, "before")) throw new IOException("Rollback backup hash mismatch.");
            Parse(bytes); Swap(current.Path, current.Bytes, bytes);
        }
        private static void WriteNew(string path, byte[] bytes)
        {
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
        }
        private void Swap(string target, byte[] expected, byte[] desired)
        {
            string temp = target + ".launchdeck-tmp-" + Guid.NewGuid().ToString("N"), displaced = temp + ".displaced";
            try
            {
                SafePath(target, home); WriteNew(temp, desired);
                if (BeforeReplace != null) BeforeReplace();
                // Verify paths and identity again after any delay. App reconciliation must not be overwritten.
                var fresh = Check();
                if (fresh.Path != target || fresh.Hash != Hash(expected)) throw new IOException("Concurrent configuration change. Nothing replaced.");
                ReplaceFile(temp, target, displaced);
                if (Hash(Read(displaced)) != Hash(expected))
                {
                    if (Hash(Read(target)) == Hash(desired)) File.Replace(displaced, target, null);
                    throw new IOException("Concurrent replacement detected. Displaced configuration restored when safe; review retained backup files.");
                }
                if (Hash(Read(target)) != Hash(desired)) throw new IOException("Configuration changed after replacement. Original backup retained; no newer content overwritten.");
                File.Delete(displaced);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        // Locate just the selected JSON value, preserving all other bytes, formatting and unknown fields.
        private sealed class JsonSpan
        {
            private readonly string text, selectedPath; private int pos;
            internal int Start = -1, End, ServiceEnd = -1;
            internal JsonSpan(string value, string selected = "/mcpServers/cua_repl/env_vars") { text = value; selectedPath = selected; }
            internal void Scan() { Value(""); White(); if (pos != text.Length) throw new IOException("Unexpected JSON suffix."); }
            private void White() { while (pos < text.Length && Char.IsWhiteSpace(text[pos])) pos++; }
            private string String()
            {
                White(); int start = pos; if (pos >= text.Length || text[pos++] != '"') throw new IOException("Expected JSON string.");
                while (pos < text.Length) { char c = text[pos++]; if (c == '\\') { pos++; continue; } if (c == '"') return new JavaScriptSerializer().Deserialize<string>(text.Substring(start, pos - start)); }
                throw new IOException("Incomplete JSON string.");
            }
            private void Value(string path)
            {
                if (path.Count(c => c == '/') > 64) throw new IOException("JSON nesting limit exceeded.");
                White(); if (pos >= text.Length) throw new IOException("Incomplete JSON.");
                int start = pos;
                if (text[pos] == '{')
                {
                    pos++; White(); var keys = new HashSet<string>();
                    if (text[pos] != '}') for (;;)
                    {
                        string key = String(); if (!keys.Add(key)) throw new IOException("Duplicate JSON key refused."); White(); if (text[pos++] != ':') throw new IOException("Expected colon.");
                        Value(path + "/" + key.Replace("~", "~0").Replace("/", "~1")); White(); if (text[pos] == '}') break; if (text[pos++] != ',') throw new IOException("Expected comma.");
                    }
                    if (path == "/mcpServers/cua_repl") ServiceEnd = pos;
                    pos++;
                }
                else if (text[pos] == '[') { pos++; White(); if (text[pos] != ']') for (;;) { Value(path + "/[]"); White(); if (text[pos] == ']') break; if (text[pos++] != ',') throw new IOException("Expected comma."); } pos++; }
                else if (text[pos] == '"') String();
                else { while (pos < text.Length && !Char.IsWhiteSpace(text[pos]) && text[pos] != ',' && text[pos] != '}' && text[pos] != ']') pos++; if (pos == start) throw new IOException("Invalid JSON token."); }
                if (path == selectedPath) { Start = start; End = pos; }
            }
        }
    }
}
