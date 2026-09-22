using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace YikaiLocal;

// 端口排查：查本机端口的监听者是哪个进程（IPv4 + IPv6），用来区分面板自己的服务和其他程序。
// FreePort 只知道“端口被占”，不知道“被谁占”；这里补上归属，供启动报错、--ports 输出和端口排查窗口使用。
public static class PortDiagnostics
{
    public sealed record Listener(int Port, int Pid);

    [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int code, ref ProcessBasicInformation info, int size, out int returned);

    // 父进程 PID：用于把监听者归到面板启动的进程树上（Windows 上 mysqld 会把自己重启为子进程）。
    public static int? ParentPid(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var info = new ProcessBasicInformation();
            return NtQueryInformationProcess(process.Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _) == 0
                ? (int)info.InheritedFromUniqueProcessId.ToInt64() : null;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }   // 提权进程不允许查询。
        catch (ExternalException) { return null; }
    }

    // 与 FreePort 使用的 GetActiveTcpListeners 一致：只关心正在监听的端口。
    public static List<Listener> Listeners()
    {
        var result = new List<Listener>();
        foreach (var family in new[] { 2, 23 })   // AF_INET / AF_INET6
        {
            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
            if (size <= 0) continue;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, family, 5, 0) != 0) continue;
                var count = Marshal.ReadInt32(buffer);
                // IPv4 行 24 字节：State、LocalAddr、LocalPort、RemoteAddr、RemotePort、Pid；
                // IPv6 行 56 字节：LocalAddr[16]、LocalScopeId、LocalPort、RemoteAddr[16]、RemoteScopeId、RemotePort、State、Pid（State 在末尾）。
                var stride = family == 2 ? 24 : 56;
                var stateOffset = family == 2 ? 0 : 48;
                var portOffset = family == 2 ? 8 : 20;
                var pidOffset = family == 2 ? 20 : 52;
                var row = buffer + 4;
                for (var i = 0; i < count; i++)
                {
                    if (Marshal.ReadInt32(row, stateOffset) == 2)   // MIB_TCP_STATE_LISTEN
                    {
                        var port = IPAddress.NetworkToHostOrder((short)(Marshal.ReadInt32(row, portOffset) & 0xFFFF)) & 0xFFFF;
                        result.Add(new Listener(port, Marshal.ReadInt32(row, pidOffset)));
                    }
                    row += stride;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }

    public static int? ListenerPid(int port)
    {
        foreach (var listener in Listeners())
            if (listener.Port == port) return listener.Pid;
        return null;
    }

    // 端口是否被系统级监听占用（不区分归属）；与 FreePort 的判断方式保持一致。
    public static bool PortBusy(int port)=>IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint=>endpoint.Port==port);

    // 进程的可读描述：名称 + PID；能取到路径时附上。提权进程可能拒绝访问，仍然返回名称。
    public static string Describe(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            var path = "";
            try { path = process.MainModule?.FileName ?? ""; } catch { /* 系统进程无法读取路径。 */ }
            return path.Length > 0 ? $"{process.ProcessName}.exe (PID {pid})\n{path}" : $"{process.ProcessName}.exe (PID {pid})";
        }
        catch (ArgumentException) { return $"PID {pid}"; }
        catch (InvalidOperationException) { return $"PID {pid}"; }
    }
}
