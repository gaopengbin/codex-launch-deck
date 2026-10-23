using System;
using System.Threading;
internal static class ControlledActivationOutput
{
    private static void Main()
    {
        // Fill stderr beyond a pipe buffer before stdout. A serial reader hangs.
        Console.Error.Write(new string('x',65536));
        Thread.Sleep(2000);
        Console.Out.WriteLine("123");
    }
}
