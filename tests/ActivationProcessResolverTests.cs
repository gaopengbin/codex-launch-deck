using System;
class ActivationProcessResolverTests
{
    static int Main()
    {
        var start=new DateTime(2026,10,21,0,0,0,DateTimeKind.Utc); var now=start; int reads=0;
        string expected=@"C:\package\app\ChatGPT.exe";
        var valid=new ActivationCandidate{Id=42,Executable=expected,StartedUtc=start,HasWindow=true};
        int pid=ActivationProcessResolver.Wait(expected,start,_=>++reads==1?new ActivationCandidate[0]:new[]{valid},()=>now=now.AddMilliseconds(200),()=>now,TimeSpan.FromSeconds(1));
        if(pid!=42 || reads!=2) throw new Exception("Exited activation broker must allow verified replacement window.");
        int rejected=0;
        foreach(var invalid in new[]{
            new ActivationCandidate{Id=3,Executable=@"C:\other\app\ChatGPT.exe",StartedUtc=start,HasWindow=true},
            new ActivationCandidate{Id=4,Executable=expected,StartedUtc=start.AddMinutes(-1),HasWindow=true},
            new ActivationCandidate{Id=5,Executable=expected,StartedUtc=start,HasWindow=false}}) {
            now=start;
            try {ActivationProcessResolver.Wait(expected,start,_=>new[]{invalid},()=>now=now.AddMilliseconds(200),()=>now,TimeSpan.FromMilliseconds(400));}
            catch(TimeoutException){rejected++;}
        }
        if(rejected!=3)throw new Exception("Unrelated, old and windowless processes must be rejected.");
        Console.WriteLine("PASS activation handoff and three rejection/timeout scenarios.");return 0;
    }
}
