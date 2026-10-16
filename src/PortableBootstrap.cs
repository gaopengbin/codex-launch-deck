using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using LaunchDeck.ProcessProxy;

namespace LaunchDeck.Portable
{
    internal static class Bootstrap
    {
        internal sealed class Manifest
        {
            public string BundleId { get; set; }
            public Dictionary<string,string> Files { get; set; }
        }
        [STAThread]
        internal static int Main(string[] args)
        {
            try
            {
                if(args.Length>1 || (args.Length==1 && args[0]!="--prepare-runtime" && args[0]!="--verify-payload"))
                    throw new ArgumentException("Invalid portable launcher arguments.");
                using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                using(var archive=new ZipArchive(payload,ZipArchiveMode.Read))
                {
                    var manifest=ReadAndVerify(archive);
                    if(args.Length==1 && args[0]=="--verify-payload") return 0;
                    bool elevated=args.Length==1;
                    string directory;
                    if(elevated)
                    {
                        using(var identity=WindowsIdentity.GetCurrent())
                            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                                throw new UnauthorizedAccessException("增强代理需要 Windows 管理员授权。");
                        directory=Path.Combine(ProxySecurity.ProtectedRoot,manifest.BundleId);
                    }
                    else directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LaunchDeck","Portable",manifest.BundleId);
                    Materialize(archive,manifest,directory,elevated);
                    if(elevated) return 0; // Preparation never starts a driver or client.
                    var start=new ProcessStartInfo(Path.Combine(directory,"ChatGPTProxyLauncher.exe"))
                    {UseShellExecute=false,WorkingDirectory=directory};
                    start.EnvironmentVariables["LAUNCHDECK_PORTABLE_EXE"]=Assembly.GetExecutingAssembly().Location;
                    Process.Start(start);
                    return 0;
                }
            }
            catch(Exception error)
            {
                MessageBox.Show(error.Message,"LaunchDeck 免安装版",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
        internal static string SafeTarget(string directory,string name)
        {
            if(String.IsNullOrEmpty(name) || name.IndexOf(':')>=0 || name.StartsWith("/") || name.StartsWith("\\"))
                throw new InvalidDataException("Unsafe bundled path.");
            string[] parts=name.Replace('\\','/').Split('/');
            foreach(string part in parts)
                if(part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" "))
                    throw new InvalidDataException("Unsafe bundled path.");
            string root=Path.GetFullPath(directory).TrimEnd('\\')+"\\";
            string path=Path.GetFullPath(Path.Combine(directory,name.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe bundled path.");
            return path;
        }
        internal static string Hash(Stream stream)
        {
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        }
        internal static Manifest ReadAndVerify(ZipArchive archive)
        {
            Manifest manifest;
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("manifest.json"))
            using(var reader=new StreamReader(input)) manifest=new JavaScriptSerializer().Deserialize<Manifest>(reader.ReadToEnd());
            if(manifest==null || manifest.BundleId!=NativeArtifacts.BundleId || manifest.Files==null || manifest.Files.Count<6)
                throw new InvalidDataException("Invalid embedded bundle identity.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in archive.Entries)
            {
                SafeTarget(Path.GetTempPath(),entry.FullName);
                string expected;
                if(!seen.Add(entry.FullName) || !manifest.Files.TryGetValue(entry.FullName,out expected))
                    throw new InvalidDataException("Unexpected embedded component.");
                using(var data=entry.Open()) if(Hash(data)!=expected) throw new InvalidDataException("Embedded component checksum mismatch.");
            }
            if(seen.Count!=manifest.Files.Count) throw new InvalidDataException("Incomplete embedded bundle.");
            return manifest;
        }
        private static DirectorySecurity DirectoryAcl()
        {
            var acl=new DirectorySecurity();
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            acl.SetAccessRuleProtection(true,false);
            var inherit=InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit;
            foreach(var kind in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinUsersSid})
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(kind,null),
                    kind==WellKnownSidType.BuiltinUsersSid?FileSystemRights.ReadAndExecute:FileSystemRights.FullControl,
                    inherit,PropagationFlags.None,AccessControlType.Allow));
            return acl;
        }
        private static FileSecurity FileAcl()
        {
            var acl=new FileSecurity();
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            acl.SetAccessRuleProtection(true,false);
            foreach(var kind in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinUsersSid})
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(kind,null),
                    kind==WellKnownSidType.BuiltinUsersSid?FileSystemRights.ReadAndExecute:FileSystemRights.FullControl,AccessControlType.Allow));
            return acl;
        }
        private static void EnsureDirectory(string path,bool elevated)
        {
            ProxySecurity.RejectReparseAncestors(path);
            if(!Directory.Exists(path))
            {
                if(elevated) Directory.CreateDirectory(path,DirectoryAcl());
                else Directory.CreateDirectory(path);
            }
            if(elevated && ProxySecurity.HasUnprivilegedWrite(Directory.GetAccessControl(path)))
                throw new UnauthorizedAccessException("增强代理组件目录可被普通用户写入，已停止。没有修改现有权限。");
        }
        internal static void Materialize(ZipArchive archive,Manifest manifest,string directory,bool elevated)
        {
            ProxySecurity.RejectReparseAncestors(directory);
            if(elevated)
            {
                // Fixed destination, embedded bytes, no external script or arbitrary
                // destination/source arguments. Existing versions are never replaced.
                if(!directory.Equals(Path.Combine(ProxySecurity.ProtectedRoot,NativeArtifacts.BundleId),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid protected runtime destination.");
                EnsureDirectory(ProxySecurity.ProtectedRoot,true);
            }
            EnsureDirectory(directory,elevated);
            foreach(var entry in archive.Entries)
            {
                string path=SafeTarget(directory,entry.FullName);
                string parent=Path.GetDirectoryName(path);
                // Make each parent individually so none starts with a writable ACL.
                var parents=new Stack<string>();
                for(string current=parent;!current.Equals(directory,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current)) parents.Push(current);
                while(parents.Count>0) EnsureDirectory(parents.Pop(),elevated);
                EnsureDirectory(parent,elevated);
                ProxySecurity.RejectReparseAncestors(path);
                string expected=manifest.Files[entry.FullName];
                if(File.Exists(path))
                {
                    if(elevated) ProxySecurity.VerifyProtectedFile(path);
                    ProxySecurity.VerifyHash(path,expected);
                    continue;
                }
                using(var source=entry.Open())
                using(var output=elevated
                    ?new FileStream(path,FileMode.CreateNew,FileSystemRights.Write,FileShare.None,65536,FileOptions.None,FileAcl())
                    :new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) source.CopyTo(output);
                if(elevated) ProxySecurity.VerifyProtectedFile(path);
                ProxySecurity.VerifyHash(path,expected);
            }
            if(elevated) NativePins.Verify(Path.Combine(directory,"process-proxy-native"));
        }
    }
}
