using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HarborDrive.Core;

public static class LocalDirectoryPath
{
    // AppData can be redirected for a process launched by a packaged app.
    // Explorer runs outside that file-system view and needs the physical path.
    public static string Resolve(string path)
    {
        using var directory = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "로컬 폴더의 실제 위치를 확인하지 못했습니다.");

        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(directory, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "로컬 폴더의 실제 위치를 확인하지 못했습니다.");
        if (length >= buffer.Capacity) throw new PathTooLongException("로컬 폴더 경로가 너무 깁니다.");

        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved[8..];
        return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved[4..] : resolved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
