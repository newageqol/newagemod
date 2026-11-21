using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class QuickExit
    {
        private const string LastLine = "Input System module state changed to: Shutdown";
        private static bool _armed;
        private static bool _fired;

        private static bool Enabled => Plugin.CfgQuickExit == null || Plugin.CfgQuickExit.Value;

        internal static void Arm()
        {
            if (_armed) return;
            if (Application.platform != RuntimePlatform.WindowsPlayer) return;
            _armed = true;
            Application.quitting += Fire;
        }

        private static void Fire()
        {
            if (_fired || !Enabled) return;
            _fired = true;
            try
            {
                string log = Application.consoleLogPath;
                int id = Process.GetCurrentProcess().Id;
                string script =
                    "$id=" + id + ";$log='" + (log ?? "").Replace("'", "''") + "';$start=Get-Date;" +
                    "while($true){" +
                    "$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if(-not $p){break};" +
                    "$done=$false;" +
                    "if($log -and (Test-Path -LiteralPath $log)){try{$t=(Get-Content -LiteralPath $log -Tail 4 -ErrorAction Stop) -join ' ';if($t.Contains('" + LastLine + "')){$done=$true}}catch{}};" +
                    "if($done){Start-Sleep -Milliseconds 500;Stop-Process -Id $id -Force -ErrorAction SilentlyContinue;break};" +
                    "if(((Get-Date)-$start).TotalSeconds -gt 20){Stop-Process -Id $id -Force -ErrorAction SilentlyContinue;break};" +
                    "Start-Sleep -Milliseconds 200}";
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var info = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(info);
                Plugin.Log?.LogInfo("[exit] quick exit armed: the process ends right after Unity finishes shutting down");
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[exit] quick exit: " + e.Message); }
        }
    }
}
