// Fixture for the documented preflight command, no network or OS configuration.
using System;
using System.IO;
using System.Runtime.InteropServices;
class HostProbe {
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    static int Main(string[] args) {
        int status = args.Length == 1 && args[0] == "--check-runtime" ? 0 : 2;
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe-result.txt"),status.ToString());
        // The managed test host runs inside an instrumented build sandbox. End
        // this fixture process after recording the result, with its exit code.
        TerminateProcess(GetCurrentProcess(), (uint)status);
        return status;
    }
}
