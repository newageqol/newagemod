using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace NewAge2D;

internal static class Native
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string path);

    public static void Preload(ManualLogSource log)
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(Native).Assembly.Location) ?? "";
            string path = Path.Combine(dir, "libSkiaSharp.dll");
            if (!File.Exists(path))
            {
                log.LogError("нет файла " + path);
                return;
            }
            var handle = LoadLibraryW(path);
            if (handle == IntPtr.Zero) log.LogError($"libSkiaSharp.dll не загрузилась, код {Marshal.GetLastWin32Error()}");
            else log.LogInfo("libSkiaSharp.dll загружена из " + dir);
        }
        catch (Exception ex)
        {
            log.LogError("libSkiaSharp: " + ex.Message);
        }
    }
}
