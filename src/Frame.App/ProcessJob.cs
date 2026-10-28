using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Frame.App;

// Keeps FRAME's FFmpeg processes from outliving FRAME (0.6.2). A normal Quit already stops capture, but a crash, "End task",
// a power loss of the UI thread or a killed process left the capture FFmpeg running: nobody read its progress pipe any more
// and it buffered frames in memory for hours (12 GB seen overnight).
//  1. Every FFmpeg started through Backend.Start joins a Windows job object with KILL_ON_JOB_CLOSE. When FRAME ends for any
//     reason Windows closes the job handle and ends all of its processes.
//  2. At start-up FRAME ends capture FFmpeg processes left by an older build (no job) or by a failed assignment: orphans of
//     THIS data folder's Recovery directory whose parent process is gone. Other FFmpeg processes are never touched.
static class ProcessJob
{
    static readonly nint job = Create();
    static nint Create()
    {
        try
        {
            nint handle = CreateJobObject(0, null);
            if (handle == 0) return 0;
            var limits = new JobExtendedLimits { Basic = new JobBasicLimits { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ } };
            int size = Marshal.SizeOf<JobExtendedLimits>(); nint buffer = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(limits, buffer, false); if (!SetInformationJobObject(handle, 9 /* JobObjectExtendedLimitInformation */, buffer, (uint)size)) { CloseHandle(handle); return 0; } }
            finally { Marshal.FreeHGlobal(buffer); }
            return handle; // deliberately never closed: it closes when FRAME's process ends
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    public static void Add(Process process)
    {
        if (job == 0) { DiagnosticLog.Warning("process-job", "Job object unavailable: FFmpeg will not be ended automatically if FRAME crashes."); return; }
        try { if (!AssignProcessToJobObject(job, process.Handle)) DiagnosticLog.Warning("process-job", "FFmpeg could not join the FRAME job object (error " + Marshal.GetLastWin32Error() + ")."); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { DiagnosticLog.Warning("process-job", "FFmpeg could not join the FRAME job object: " + e.Message); }
    }

    // Ends orphaned capture FFmpeg processes of this data folder. Returns how many were ended.
    public static int KillOrphans(string recoveryFolder)
    {
        int ended = 0;
        string marker = Path.GetFullPath(recoveryFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var p in Process.GetProcessesByName("ffmpeg"))
        {
            using (p)
            {
                try
                {
                    var (parent, command) = Inspect(p);
                    if (command == null || !command.Contains("part-%08d.mkv", StringComparison.Ordinal) || !command.Contains("gfxcapture", StringComparison.Ordinal) || !command.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
                    if (ParentAlive(parent, p.StartTime)) continue;
                    p.Kill(true); p.WaitForExit(3000); ended++;
                    DiagnosticLog.Write("orphan-ffmpeg", $"Ended capture FFmpeg process {p.Id} left behind by an earlier FRAME that ended without stopping it.");
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return ended;
    }
    static bool ParentAlive(int parentId, DateTime childStart)
    {
        if (parentId <= 0) return false;
        try { using var parent = Process.GetProcessById(parentId); return parent.StartTime <= childStart; } // a newer process with a reused id is not the parent
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    // Parent process id and command line of another process of the same user (PEB; x64 layout, FRAME is x64 only).
    static (int Parent, string? Command) Inspect(Process p)
    {
        nint handle = OpenProcess(0x0410 /* QUERY_INFORMATION | VM_READ */, false, p.Id);
        if (handle == 0) return (0, null);
        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0 || info.PebBaseAddress == 0) return (0, null);
            int parent = (int)info.InheritedFromUniqueProcessId;
            var pointer = new byte[8];
            if (!ReadProcessMemory(handle, info.PebBaseAddress + 0x20, pointer, 8, out _)) return (parent, null);
            nint parameters = (nint)BitConverter.ToInt64(pointer);
            var text = new byte[16];
            if (parameters == 0 || !ReadProcessMemory(handle, parameters + 0x70, text, 16, out _)) return (parent, null);
            int length = BitConverter.ToUInt16(text, 0); nint buffer = (nint)BitConverter.ToInt64(text, 8);
            if (length == 0 || buffer == 0 || length > 64 * 1024) return (parent, null);
            var characters = new byte[length];
            if (!ReadProcessMemory(handle, buffer, characters, length, out _)) return (parent, null);
            return (parent, System.Text.Encoding.Unicode.GetString(characters));
        }
        finally { CloseHandle(handle); }
    }

    [StructLayout(LayoutKind.Sequential)] struct ProcessBasicInformation { public nint ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId; }
    [StructLayout(LayoutKind.Sequential)] struct JobBasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct JobExtendedLimits { public JobBasicLimits Basic; public IoCounters Io; public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern nint CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, int size, out nint read);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(nint process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);
}
