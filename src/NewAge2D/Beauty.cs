using UnityEngine;

namespace NewAge2D;

internal static class Beauty
{
    private static int _original = -1;
    private static int _applied = -1;
    private static float _checkAt;

    internal static bool Weak => SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 3000;

    internal static int Smoothing => Weak ? 2 : 4;

    internal static int Aniso => Weak ? 8 : 16;

    internal static int MaxSide => Mathf.Min(SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : 2048, Weak ? 2048 : 4096);

    internal static long MaxBytes => Weak ? 24L << 20 : 64L << 20;

    internal static void Tick()
    {
        if (Time.unscaledTime < _checkAt) return;
        _checkAt = Time.unscaledTime + 1f;
        if (_original < 0) _original = QualitySettings.antiAliasing;
        int want = Plugin.FlashLook ? Mathf.Max(_original, Smoothing) : _original;
        if (QualitySettings.antiAliasing != want) QualitySettings.antiAliasing = want;
        if (_applied == want) return;
        _applied = want;
        var eye = Camera.main;
        Plugin.Log.LogInfo($"[красота] сглаживание ×{want} (у игры было ×{_original}), видеопамять {SystemInfo.graphicsMemorySize} МБ"
            + (eye != null ? $", камера: {eye.actualRenderingPath}, MSAA {(eye.allowMSAA ? "разрешено" : "запрещено")}, HDR {(eye.allowHDR ? "да" : "нет")}" : ""));
    }
}
