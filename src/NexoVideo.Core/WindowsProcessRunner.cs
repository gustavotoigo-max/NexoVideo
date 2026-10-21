using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace NexoVideo.Core;

/// <summary>Cria suspenso, associa ao Job Object e só então permite executar ferramentas externas.</summary>
public sealed class WindowsProcessRunner(string logDirectory)
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        int timeoutSeconds, int memoryMb, CancellationToken cancellation, Action<string>? outputLine = null,
        bool captureOutput = false, Action<ProcessExecution>? completed = null, Func<double?>? mediaTimestamp = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        await using var log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        await log.WriteLineAsync(executable + " " + string.Join(' ', arguments.Select(Quote)));
        using var stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        using var input = Native.CreateFile("NUL", 0x80000000, 3, nint.Zero, 3, 0, nint.Zero);
        if (input.IsInvalid || !Native.SetHandleInformation(input, 1, 1)) throw new Win32Exception();
        using var job = Native.CreateJobObject(nint.Zero, null);
        if (job.IsInvalid) throw new Win32Exception();
        var limits = new Native.ExtendedLimits
        {
            Basic = new Native.BasicLimits { LimitFlags = 0x2000 | 0x200 }, // KILL_ON_JOB_CLOSE | JOB_MEMORY
            JobMemory = (nuint)memoryMb * 1024 * 1024
        };
        if (!Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()))
            throw new Win32Exception();
        var startup = new Native.StartupInfo
        {
            Size = Marshal.SizeOf<Native.StartupInfo>(), Flags = 0x100,
            Input = input.DangerousGetHandle(), Output = stdout.ClientSafePipeHandle.DangerousGetHandle(),
            Error = stderr.ClientSafePipeHandle.DangerousGetHandle()
        };
        var command = new StringBuilder(Quote(executable) + " " + string.Join(' ', arguments.Select(Quote)));
        if (!Native.CreateProcess(executable, command, nint.Zero, nint.Zero, true, 0x08000004,
                nint.Zero, Path.GetDirectoryName(executable), ref startup, out var information))
            throw new Win32Exception();
        using var processHandle = new SafeFileHandle(information.Process, true);
        using var threadHandle = new SafeFileHandle(information.Thread, true);
        if (!Native.AssignProcessToJobObject(job, information.Process))
        {
            Native.TerminateProcess(information.Process, 1);
            throw new Win32Exception();
        }
        using var process = Process.GetProcessById((int)information.ProcessId);
        stdout.DisposeLocalCopyOfClientHandle(); stderr.DisposeLocalCopyOfClientHandle();
        input.Dispose();
        var started = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        var diagnostics = new List<DiagnosticMessage>();
        var output = new StringBuilder();
        var truncated = false;
        var diagnosticLines = 0;
        var logCharacters = 0;
        long peak = 0;
        const int outputLimit = 2 * 1024 * 1024, logLimit = 1024 * 1024;

        async Task Pump(Stream stream, bool diagnostic)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var buffer = new char[4096]; var line = new StringBuilder();
            async Task Deliver()
            {
                var text = line.ToString().TrimEnd('\r'); line.Clear();
                if (diagnostic)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        diagnosticLines = Math.Min(int.MaxValue - 1, diagnosticLines) + 1;
                        // Pipes assíncronos: o último PTS é contexto, nunca a posição exata do erro.
                        if (diagnostics.Count < 200) diagnostics.Add(new(watch.Elapsed.TotalMilliseconds,
                            text[..Math.Min(text.Length, 2048)], mediaTimestamp?.Invoke()));
                    }
                    if (logCharacters < logLimit)
                    {
                        var remaining = logLimit - logCharacters;
                        var portion = text[..Math.Min(text.Length, remaining)];
                        await log.WriteLineAsync(portion); logCharacters += portion.Length + 1;
                    }
                }
                else outputLine?.Invoke(text);
            }
            int length;
            while ((length = await reader.ReadAsync(buffer)) != 0)
            {
                if (captureOutput && !diagnostic)
                {
                    var take = Math.Min(length, Math.Max(0, outputLimit - output.Length));
                    output.Append(buffer, 0, take);
                    if (take < length) truncated = true;
                }
                for (var i = 0; i < length; i++)
                {
                    if (buffer[i] == '\n') await Deliver();
                    else if (line.Length < 16384) line.Append(buffer[i]);
                    else truncated = true;
                }
            }
            if (line.Length > 0) await Deliver();
        }

        var outTask = Pump(stdout, false); var errTask = Pump(stderr, true);
        if (Native.ResumeThread(information.Thread) == uint.MaxValue) throw new Win32Exception();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellation);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token);
            var counters = new Native.MemoryCounters { Size = (uint)Marshal.SizeOf<Native.MemoryCounters>() };
            if (Native.GetProcessMemoryInfo(processHandle, ref counters, counters.Size)) peak = (long)counters.PeakWorkingSet;
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellation.IsCancellationRequested;
            Native.TerminateJobObject(job, 1);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        finally
        {
            // Um filho não pode manter pipes/arquivos abertos após o processo principal sair.
            Native.TerminateJobObject(job, 0);
            await Task.WhenAll(outTask, errTask);
        }
        watch.Stop();
        if (logCharacters >= logLimit) await log.WriteLineAsync("[Log limitado a 1 MiB de texto; contadores continuam ativos.]");
        await log.FlushAsync();
        using var logInput = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var logHash = Convert.ToHexString(await SHA256.HashDataAsync(logInput, CancellationToken.None));
        completed?.Invoke(new(executable, arguments.ToArray(), started, watch.Elapsed.TotalMilliseconds,
            process.ExitCode, timedOut, cancellation.IsCancellationRequested, truncated, logCharacters >= logLimit,
            diagnosticLines, peak > 0 ? peak : null, logPath, diagnostics, logHash));
        cancellation.ThrowIfCancellationRequested();
        return new(process.ExitCode, timedOut, truncated, output.ToString(), logPath, diagnosticLines, peak);
    }

    public static string Quote(string argument)
    {
        // Regras CommandLineToArgvW: barra invertida antes de aspas e no fim precisa ser duplicada.
        var output = new StringBuilder("\""); var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') { output.Append('\\', slashes * 2 + 1); output.Append('"'); }
            else { output.Append('\\', slashes); output.Append(character); }
            slashes = 0;
        }
        output.Append('\\', slashes * 2); return output.Append('"').ToString();
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits
        { public long ProcessTime, JobTime; public uint LimitFlags; public nuint MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcesses; public nuint Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters
        { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits
        { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
        { public int Size; public string? Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags; public short Show, ReservedSize; public nint ReservedData, Input, Output, Error; }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo
        { public nint Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryCounters
        { public uint Size, PageFaults; public nuint PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile; }
        [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessMemoryInfo(SafeFileHandle process, ref MemoryCounters counters, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits information, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(nint process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(nint thread);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateProcess(string application, StringBuilder command, nint processAttributes, nint threadAttributes, bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfo startup, out ProcessInfo information);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    }
}
