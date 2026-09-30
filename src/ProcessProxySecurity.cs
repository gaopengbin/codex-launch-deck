using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LaunchDeck.ProcessProxy
{
    internal static class ProxySecurity
    {
        public static readonly string ProtectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LaunchDeckProcessProxy");
        private const FileSystemRights Writable = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle handle, out uint pid);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint pid);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint rights, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder image, ref uint length);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder path);

        internal static string HashFile(string path)
        {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
        internal static void VerifyHash(string path, string expected)
        {
            if(expected==null || expected.Length!=64 || expected.Any(c=>!Uri.IsHexDigit(c)) || !HashFile(path).Equals(expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Component identity verification failed.");
        }
        internal static bool IsPrivilegedSid(SecurityIdentifier sid)
        {
            return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || sid.Value=="S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"; // TrustedInstaller
        }
        internal static bool HasUnprivilegedWrite(FileSystemSecurity security)
        {
            if(!IsPrivilegedSid((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier)))) return true;
            foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
                if(rule.AccessControlType==AccessControlType.Allow && (rule.FileSystemRights & Writable)!=0 && !IsPrivilegedSid((SecurityIdentifier)rule.IdentityReference)) return true;
            return false;
        }
        internal static void RejectReparseAncestors(string path)
        {
            string current=Path.GetFullPath(path);
            while(current!=null)
            {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidOperationException("Redirected component paths are not allowed.");
                var parent=Directory.GetParent(current); current=parent==null?null:parent.FullName;
            }
        }
        internal static void VerifyProtectedDirectory(string directory)
        {
            directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            string root=Path.GetFullPath(ProtectedRoot).TrimEnd(Path.DirectorySeparatorChar);
            if(!directory.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
                throw new InvalidOperationException("Approved components must be installed in the protected Launch Deck directory.");
            RejectReparseAncestors(directory);
            string current=directory;
            while(true)
            {
                if(HasUnprivilegedWrite(Directory.GetAccessControl(current))) throw new InvalidOperationException("Component directory is writable by an unprivileged principal.");
                if(current.Equals(root,StringComparison.OrdinalIgnoreCase)) break;
                current=Directory.GetParent(current).FullName;
            }
        }
        internal static void VerifyProtectedFile(string path)
        {
            VerifyProtectedDirectory(Path.GetDirectoryName(path));
            if(!File.Exists(path)) throw new InvalidOperationException("Approved component is missing.");
            RejectReparseAncestors(path);
            if(HasUnprivilegedWrite(File.GetAccessControl(path))) throw new InvalidOperationException("Component file is writable by an unprivileged principal.");
        }
        internal static void VerifyRegisteredPackage(ProxyPlan plan)
        {
            plan.Validate();
            string root=Path.GetDirectoryName(Path.GetDirectoryName(plan.ExecutablePath));
            string fullName=Path.GetFileName(root);
            if(!(fullName.StartsWith("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)||fullName.StartsWith("OpenAI.ChatGPT-Desktop_",StringComparison.OrdinalIgnoreCase))
                || !fullName.EndsWith("__2p2nqsd0c76g0",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unapproved package family.");
            uint count=0;
            if(GetPackagePathByFullName(fullName,ref count,null)!=122 || count<1 || count>4096) throw new InvalidOperationException("Target package is not registered.");
            var value=new StringBuilder((int)count);
            if(GetPackagePathByFullName(fullName,ref count,value)!=0 || !Path.GetFullPath(value.ToString()).Equals(root,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Package path changed.");
            RejectReparseAncestors(plan.ExecutablePath);
            if(!File.Exists(plan.ExecutablePath)) throw new InvalidOperationException("Package executable is missing.");
        }
        internal static void VerifyPeer(uint pid, string sid, string expectedHash)
        {
            if(pid==0) throw new InvalidOperationException("Missing control peer.");
            IntPtr process=OpenProcess(0x1000,false,pid); // QUERY_LIMITED_INFORMATION, never execute/terminate.
            if(process==IntPtr.Zero) throw new InvalidOperationException("Cannot identify control peer.");
            IntPtr token=IntPtr.Zero;
            try
            {
                var image=new StringBuilder(32768); uint length=(uint)image.Capacity;
                if(!QueryFullProcessImageName(process,0,image,ref length) || !OpenProcessToken(process,8,out token)) throw new InvalidOperationException("Cannot verify control peer identity.");
                using(var identity=new WindowsIdentity(token)) if(identity.User.Value!=sid) throw new InvalidOperationException("Control peer user mismatch.");
                VerifyProtectedFile(image.ToString());
                VerifyHash(image.ToString(),expectedHash);
            }
            finally { if(token!=IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
        }
        internal static PipeSecurity PipeAcl(SecurityIdentifier user)
        {
            var acl=new PipeSecurity(); acl.SetAccessRuleProtection(true,false); acl.SetOwner(user);
            acl.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
            acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            return acl;
        }
        internal static void VerifyDriver(string path, string signerThumbprint)
        {
            // Timestamp-aware Authenticode trust; revocation checking is not disabled.
            var file=new TrustFile { Size=(uint)Marshal.SizeOf(typeof(TrustFile)), Path=path };
            IntPtr data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            Marshal.StructureToPtr(file,data,false);
            var trust=new TrustData { Size=(uint)Marshal.SizeOf(typeof(TrustData)), UiChoice=2, RevocationChecks=1, UnionChoice=1, File=data, StateAction=1, ProviderFlags=0x80 }; // chain excluding root
            Guid action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                int result=WinVerifyTrust(new IntPtr(-1),ref action,ref trust);
                if(result!=0) throw new InvalidOperationException("Driver signature trust failed (0x"+result.ToString("X8")+"). No security policy is changed.");
                using(var cert=new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    if(!cert.Thumbprint.Equals(signerThumbprint,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Driver signer identity changed.");
            }
            finally { trust.StateAction=2; WinVerifyTrust(new IntPtr(-1),ref action,ref trust); Marshal.DestroyStructure(data,typeof(TrustFile)); Marshal.FreeHGlobal(data); }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle; public IntPtr KnownSubject; }
        [StructLayout(LayoutKind.Sequential)] private struct TrustData
        { public uint Size; public IntPtr PolicyCallback, SipClient; public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File; public uint StateAction; public IntPtr StateData, Url; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings; }
        [DllImport("wintrust.dll",ExactSpelling=true)] private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
    }
    internal static class ProxyProtocol
    {
        internal const int MaxFrame=4096;
        internal static string ReadLine(TextReader reader)
        {
            var value=new StringBuilder(); int c;
            while((c=reader.Read())!=-1) { if(c=='\n') return value.ToString().TrimEnd('\r'); if(value.Length>=MaxFrame || c=='\0') throw new InvalidDataException("Invalid control frame."); value.Append((char)c); }
            if(value.Length!=0) throw new InvalidDataException("Truncated control frame.");
            return null;
        }
        internal static void DisposePipeWriter(StreamWriter writer)
        {
            // BrokerSession reports engine cleanup independently. StreamWriter.Dispose
            // retries flushing buffered status after a peer closes the pipe; that
            // transport failure must not overwrite a confirmed rollback exit code.
            try { writer.Dispose(); } catch(IOException) {}
        }
        internal static void RequireStop(string command)
        { if(command!=null && command!="STOP") throw new InvalidDataException("Unknown control command."); }
        internal static string NewPipeName() { return "LaunchDeck.ProcessProxy."+Guid.NewGuid().ToString("N"); }
        internal static bool IsPipeName(string name)
        { Guid token; return name!=null && name.StartsWith("LaunchDeck.ProcessProxy.",StringComparison.Ordinal) && Guid.TryParseExact(name.Substring("LaunchDeck.ProcessProxy.".Length),"N",out token); }
    }
}
