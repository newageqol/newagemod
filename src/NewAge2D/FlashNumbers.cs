using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

[HarmonyPatch]
internal static class FlashNumbers
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CharacterIndicators, int[]> Versions = new();

    [HarmonyPostfix, HarmonyPatch(typeof(CharacterIndicators), "Assign")]
    private static void AfterAssign(CharacterIndicators __instance)
    {
        if (__instance != null) Versions.GetValue(__instance, _ => new int[1])[0]++;
    }

    internal static int VersionOf(CharacterIndicators indicators) =>
        indicators == null ? 0 : Versions.GetValue(indicators, _ => new int[1])[0];

    private const float Rise = 150f;
    private const float FontPixels = 15f;
    private const float Span = 0.9f;
    private const float Lifetime = 0.95f;
    private const float IconScale = 3f;
    private const int FontBase = 64;
    private const float MineBoost = 1.2f;
    private const float MineEdge = 3f;
    private const float MineSpan = 1.0f;
    private const float MineHold = 0.7f;
    private const float MineLifetime = 1.4f;
    private const float PushedLeft = 0.5f;
    private const float LineStep = 20f;
    private const float LiftSpeed = 10f;

    private sealed class Shown
    {
        public AbstractCharacter Target;
        public string Symbol;
        public bool Plus;
        public bool Mine;
        public float Born;
        public float FadeFrom;
        public float End;
        public float Lift;
        public float LiftShown;
        public GameObject Root;
        public Image Icon;
        public Text Label;
        public Color Tint;
    }

    private static readonly List<Shown> Live = new();
    private static readonly Dictionary<string, Sprite> Icons = new();
    private static readonly HashSet<string> Asked = new();
    private static Font _typeface;

    internal static void Prewarm()
    {
        foreach (string kind in new[] { "change_life", "change_mana", "change_stamina", "change_expower" })
        {
            Ask(kind + "_plus");
            Ask(kind + "_minus");
        }
    }

    private static string NameOf(AbstractCharacter character) =>
        character == null ? "-" : !string.IsNullOrEmpty(character.Login) ? character.Login : character.UserId.ToString();

    private static bool Mine(AbstractCharacter target, AbstractCharacter by)
    {
        var me = Fighters.Combat()?.MyCharacter;
        if (me == null) return false;
        return target.UserId == me.UserId || (by != null && by.UserId == me.UserId);
    }

    internal static void Show(AbstractCharacter target, AbstractCharacter by, string name, int value)
    {
        if (!Plugin.FlashFight || target == null) return;
        var location = CombatView.Get();
        if (location == null || location.CombatCamera == null) return;
        if (Trace.On) Trace.Write($"'{NameOf(target)}': {name} {(value > 0 ? "+" : "")}{value}, Flash number");
        if (name == "change_life" && value < 0) ArenaFx.Current?.Hit(target, false);

        bool plus = value > 0;
        string symbol = name + (plus ? "_plus" : "_minus");
        Ask(symbol);
        var root = new GameObject("NewAge2D.Number", typeof(RectTransform));
        BaseLocationView.SetMainCanvasAsParent(root.transform);
        root.transform.localScale = Vector3.one;
        root.transform.SetAsLastSibling();

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(root.transform, false);
        var icon = iconObject.GetComponent<Image>();
        icon.raycastTarget = false;
        icon.enabled = false;

        var labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(root.transform, false);
        var label = labelObject.GetComponent<Text>();
        label.raycastTarget = false;
        label.font = Typeface();
        label.fontStyle = FontStyle.Bold;
        label.fontSize = FontBase;
        label.alignment = TextAnchor.LowerLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.text = plus ? "+" + value : value.ToString();
        label.rectTransform.pivot = Vector2.zero;
        label.rectTransform.sizeDelta = new Vector2(FontBase * 8f, FontBase * 1.4f);
        bool mine = Mine(target, by);
        if (mine)
        {
            var edge = labelObject.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.85f);
            edge.effectDistance = new Vector2(MineEdge, -MineEdge);
        }

        float born = Time.time;
        foreach (var older in Live)
        {
            if (older.Target != target) continue;
            older.Lift += 1f;
            older.End = Mathf.Min(older.End, born + PushedLeft);
            older.FadeFrom = Mathf.Min(older.FadeFrom, born);
        }
        float end = born + (mine ? MineLifetime : Lifetime);
        var shown = new Shown { Target = target, Symbol = symbol, Plus = plus, Mine = mine, Born = born, FadeFrom = mine ? born + MineHold : end, End = end, Root = root, Icon = icon, Label = label, Tint = TintOf(name, plus) };
        Live.Add(shown);
        Place(shown, location.CombatCamera, 0f);
    }

    internal static void Tick()
    {
        if (Live.Count == 0) return;
        float now = Time.time;
        var location = CombatView.Get();
        var eye = location != null ? location.CombatCamera : null;
        for (int i = 0; i < Live.Count; i++)
        {
            var shown = Live[i];
            float age = now - shown.Born;
            if (shown.Root == null || eye == null || shown.Target == null || now >= shown.End)
            {
                if (shown.Root != null) UnityEngine.Object.Destroy(shown.Root);
                Live.RemoveAt(i--);
                continue;
            }
            Place(shown, eye, age);
        }
    }

    private static void Place(Shown shown, Camera eye, float age)
    {
        var doll = Fighters.DollOf(shown.Target);
        var feet = doll != null && doll.Placed ? doll.Feet : shown.Target.position;
        float unit = Field.DollSize / Field.PxPerUnit;
        var origin = feet + eye.transform.up * (Rise * unit);
        var at = eye.WorldToScreenPoint(origin);
        var next = eye.WorldToScreenPoint(origin + eye.transform.up * unit);
        float pixel = Mathf.Max(0.01f, ((Vector2)next - (Vector2)at).magnitude);
        var canvas = shown.Root.GetComponentInParent<Canvas>();
        float factor = canvas != null ? Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor) : 1f;

        float share = Mathf.Clamp01(age / (shown.Mine ? MineSpan : Span));
        float x = shown.Plus ? Mathf.Lerp(-36.95f, -19.95f, share) : Mathf.Lerp(-14.75f, -27.05f, share);
        float y = shown.Plus ? Mathf.Lerp(-102.5f, -32.85f, share) : Mathf.Lerp(-17.35f, -79.3f, share);
        float s = shown.Plus ? Mathf.Lerp(2f, 1.053f, share) : Mathf.Lerp(1f, 1.947f, share);
        float now = shown.Born + age;
        float alpha = (shown.Mine ? 1f : Mathf.Lerp(1f, 0.05f, share))
            * (now < shown.FadeFrom ? 1f : Mathf.Lerp(1f, 0.05f, Mathf.Clamp01((now - shown.FadeFrom) / Mathf.Max(0.01f, shown.End - shown.FadeFrom))));
        shown.LiftShown = Mathf.MoveTowards(shown.LiftShown, shown.Lift, Time.deltaTime * LiftSpeed);
        float lift = shown.LiftShown * LineStep * pixel;

        float glyph = FontPixels * s * pixel * (shown.Mine ? MineBoost : 1f);
        var labelRect = shown.Label.rectTransform;
        labelRect.localScale = Vector3.one * (glyph / (FontBase * factor));
        labelRect.position = new Vector3(at.x + (x + 2f * s) * pixel, at.y - (y + 15.6f * s) * pixel - glyph * 0.21f + lift, 0f);
        var color = shown.Tint;
        color.a *= alpha;
        shown.Label.color = color;

        if (!Icons.TryGetValue(shown.Symbol, out var sprite) || sprite == null) return;
        var icon = shown.Icon;
        if (!icon.enabled)
        {
            icon.sprite = sprite;
            icon.enabled = true;
        }
        var iconRect = icon.rectTransform;
        iconRect.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        iconRect.localScale = Vector3.one;
        iconRect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * (pixel / (IconScale * factor));
        iconRect.position = new Vector3(at.x, at.y + lift, 0f);
    }

    private static Color TintOf(string name, bool plus) => name switch
    {
        "change_mana" => (Color)new Color32(0x00, 0x99, 0xFF, 0xFF),
        "change_stamina" => plus ? (Color)new Color32(0xFF, 0xFF, 0x00, 0xFF) : (Color)new Color32(0xFF, 0xFF, 0x66, 0xFF),
        "change_expower" => plus ? (Color)new Color32(0x00, 0xFF, 0x00, 0xE6) : (Color)new Color32(0x00, 0xFF, 0x33, 0xE6),
        _ => (Color)new Color32(0xFF, 0x00, 0x00, 0xFF),
    };

    private static Font Typeface()
    {
        if (_typeface != null) return _typeface;
        try { _typeface = Font.CreateDynamicFontFromOSFont(new[] { "Arial" }, FontBase); }
        catch { }
        if (_typeface == null) _typeface = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _typeface;
    }

    private static void Ask(string symbol)
    {
        if (Icons.ContainsKey(symbol) || Plugin.Store == null || !Asked.Add(symbol)) return;
        DollWorker.Run(() =>
        {
            var movie = Plugin.Store.Get("effects.swf", out string reason);
            if (movie == null) return new DollSequence { Label = symbol, Error = "no effects.swf: " + reason };
            return Doll.RenderClip(movie, symbol, IconScale, 1);
        }, result => MainThread.Post(() =>
        {
            var sequence = result as DollSequence;
            var picture = sequence != null && sequence.Error == null && sequence.Frames.Count > 0 ? sequence.Frames[0] : null;
            if (picture == null || picture.Rgba == null)
            {
                Plugin.Log.LogWarning($"[numbers] icon {symbol}: {sequence?.Error ?? (result as Exception)?.Message ?? "no frame"}");
                return;
            }
            var texture = new Texture2D(picture.Width, picture.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.LoadRawTextureData(picture.Rgba);
            texture.Apply(false, true);
            Icons[symbol] = Sprite.Create(texture, new Rect(0, 0, picture.Width, picture.Height), new Vector2(picture.PivotX, picture.PivotY), 100f, 0, SpriteMeshType.FullRect);
        }), 1);
    }
}
