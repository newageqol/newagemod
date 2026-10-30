using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

internal static class ArenaHome
{
    private static GameObject _canvas;
    private static RectTransform _button;

    internal static void Show(System.Action reset)
    {
        Hide();
        _canvas = new GameObject("NewAge2D.ArenaHome");
        UnityEngine.Object.DontDestroyOnLoad(_canvas);
        var canvas = _canvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        _canvas.AddComponent<GraphicRaycaster>();
        _canvas.AddComponent<Follow>();

        var go = new GameObject("Reset", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_canvas.transform, false);
        _button = (RectTransform)go.transform;
        _button.anchorMin = _button.anchorMax = Vector2.zero;
        _button.pivot = Vector2.zero;
        _button.sizeDelta = new Vector2(Round, Round);
        _button.anchoredPosition = Spot();
        var image = go.GetComponent<Image>();
        image.sprite = Icon();
        image.preserveAspect = true;
        var button = go.GetComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => reset());
    }

    private const float Round = 56f, Margin = 8f;
    private static System.Func<int> _taken;
    private static bool _looked;

    private static Vector2 Spot()
    {
        if (!_looked)
        {
            _looked = true;
            try
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var prop = asm.GetType("NewAgeQoL.CornerRow", false)?.GetProperty("Taken", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop == null) continue;
                    _taken = (System.Func<int>)System.Delegate.CreateDelegate(typeof(System.Func<int>), prop.GetGetMethod());
                    break;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[arena3d] corner row: " + e.Message); }
        }
        int n = 2;
        try { if (_taken != null) n = _taken(); }
        catch { }
        return new Vector2(Margin + n * (Round + Margin), Margin);
    }

    private sealed class Follow : MonoBehaviour
    {
        private void LateUpdate()
        {
            if (_button == null) return;
            var spot = Spot();
            if (_button.anchoredPosition != spot) _button.anchoredPosition = spot;
        }
    }

    private static Sprite _icon;

    private static Sprite Icon()
    {
        if (_icon != null) return _icon;
        const int N = 128;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "ArenaHomeIcon", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        var face = new Color(0.16f, 0.13f, 0.1f);
        var rim = new Color(0.62f, 0.5f, 0.3f);
        var glyph = new Color(0.98f, 0.82f, 0.3f);
        float c = (N - 1) / 2f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float outer = Mathf.Clamp01(c - d + 0.5f);
                if (outer <= 0f) { px[y * N + x] = new Color32(0, 0, 0, 0); continue; }
                float ring = Mathf.Clamp01(d - (c - 4f) + 0.5f);
                var col = Color.Lerp(face * (1.15f - 0.25f * y / N), rim, ring);
                float g = Glyph(x, y);
                col = Color.Lerp(col, glyph, g * (1f - ring));
                col.a = outer;
                px[y * N + x] = col;
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        _icon = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _icon;
    }

    private static float Box(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        float qx = Mathf.Max(x0 + r - x, 0f, x - (x1 - r));
        float qy = Mathf.Max(y0 + r - y, 0f, y - (y1 - r));
        float d = Mathf.Sqrt(qx * qx + qy * qy) - r;
        return Mathf.Clamp01(0.5f - d);
    }

    private static float Disc(float x, float y, float cx, float cy, float r) =>
        Mathf.Clamp01(r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) + 0.5f);

    private static float Glyph(int x, int y)
    {
        float body = Box(x, y, 28f, 36f, 80f, 66f, 5f);
        float reels = Mathf.Max(Disc(x, y, 42f, 80f, 13f), Disc(x, y, 68f, 82f, 15f));
        float hubs = Mathf.Max(Disc(x, y, 42f, 80f, 4f), Disc(x, y, 68f, 82f, 4.5f));
        float lens = 0f;
        if (x >= 80f && x <= 102f)
        {
            float k = (x - 80f) / 22f;
            float half = 7f + k * 9f;
            lens = Mathf.Clamp01(half - Mathf.Abs(y - 51f) + 0.5f) * Mathf.Clamp01(102.5f - x);
        }
        return Mathf.Clamp01(Mathf.Max(Mathf.Max(body, reels - hubs), lens));
    }

    internal static bool Over()
    {
        if (_button == null) return false;
        var canvas = _canvas.GetComponent<Canvas>();
        return RectTransformUtility.RectangleContainsScreenPoint(_button, Input.mousePosition, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera);
    }

    internal static void Hide()
    {
        if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
        _canvas = null;
        _button = null;
    }
}
