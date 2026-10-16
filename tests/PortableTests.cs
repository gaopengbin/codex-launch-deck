using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using LaunchDeck.Portable;
using LaunchDeck.ProcessProxy;

internal static class PortableTests
{
    private static int checks;
    private static void Check(bool value,string label) { if(!value) throw new Exception(label); checks++; }
    private static void Reject(Action action,string label) { bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,label); }
    private static int Main()
    {
        try
        {
            string directory=Path.Combine(Path.GetTempPath(),"LaunchDeck-portable-test-"+Guid.NewGuid().ToString("N"));
            foreach(string bad in new[]{"../escape","a/../../escape","C:/escape","/absolute","\\absolute","a//b","a/./b","a./file","a /file","file:stream"})
                Reject(()=>Bootstrap.SafeTarget(directory,bad),"reject path "+bad);
            using(var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read))
            {
                var manifest=Bootstrap.ReadAndVerify(archive);Check(manifest.Files.Count>=6,"embedded identity");
                Bootstrap.Materialize(archive,manifest,directory,false);
                foreach(var entry in manifest.Files) {
                    string path=Bootstrap.SafeTarget(directory,entry.Key);
                    ProxySecurity.VerifyHash(path,entry.Value);checks++;
                }
                Check(!File.Exists(Path.Combine(directory,"Install-Enhanced.ps1")),"no installer payload");
                Bootstrap.Materialize(archive,manifest,directory,false);checks++;
                string exe=Path.Combine(directory,"ChatGPTProxyLauncher.exe");File.AppendAllText(exe,"corrupt");
                Reject(()=>Bootstrap.Materialize(archive,manifest,directory,false),"modified cached EXE rejected");
                Reject(()=>Bootstrap.Materialize(archive,manifest,directory,true),"arbitrary privileged destination rejected");
            }
            // Regression for the screenshot: FullControl contains Synchronize.
            // Compare effective writable rights, never stripped actual vs raw expected.
            var acl=new DirectorySecurity();acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            acl.SetAccessRuleProtection(true,false);
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),FileSystemRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,AccessControlType.Allow));
            Check(!ProxySecurity.HasUnprivilegedWrite(acl),"normal protected ACL accepted");
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.Write,AccessControlType.Allow));
            Check(ProxySecurity.HasUnprivilegedWrite(acl),"user writable ACL rejected");
            Console.WriteLine("PASS portable extraction, checksums, reuse, tamper rejection, paths and ACL: "+checks+" checks");
            Console.WriteLine("Fixture retained: "+directory);return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
