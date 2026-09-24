using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

internal static class SpellFx
{
    private static bool _warmed;

    internal const float FxScale = 1.75f;

    internal static float NominalPpu => FxScale * 150f / 1.7f;

    internal static float PlacePpu => Field.PxPerUnit / Field.DollSize;

    internal static float PpuFor(AbstractCharacter target)
    {
        return Field.PxPerUnit * FxScale / Field.DollSize;
    }

    private static string Look => $"fx/{FxScale:0.##}/{Doll.Vivid:0.##}";

    internal static void Prewarm()
    {
        if (_warmed || !Plugin.FlashFight || !Plugin.CfgFlashMagic.Value) return;
        _warmed = true;
        foreach (string prefix in new[] { "NM", "BM", "WM", "EF" })
        {
            Need(prefix + "up", 1);
            Need(prefix + "down", 1);
        }
    }

    internal static void Reset()
    {
        _warmed = false;
    }

    internal static void Play(AbstractCharacter target, string prefix, float since = -1f)
    {
        if (!Plugin.FlashFight || !Plugin.CfgFlashMagic.Value || target == null || !target.Initialized) return;
        try
        {
            Need(prefix + "up", 0);
            Need(prefix + "down", 0);
            var go = new GameObject("NewAge2D.Fx." + prefix);
            var player = go.AddComponent<FxPlayer>();
            player.Init(target, Look, prefix + "up", prefix + "down", since < 0f ? Time.time : since);
            if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[магия] эффект {prefix} у «{target.Login}»");
        }
        catch (Exception ex) { Plugin.Log.LogError("[магия] " + ex); }
    }

    private static bool _noClient;

    private static void Need(string symbol, int priority)
    {
        string look = Look;
        string sequence = FrameCache.SequenceKey(look, symbol);
        if (FrameCache.HasSequence(sequence) || !FrameCache.BeginSequence(sequence)) return;
        float scale = FxScale;
        int generation = FrameCache.Generation;
        DollWorker.Run(() =>
        {
            var movie = Plugin.Store.Get("client.swf", out string reason);
            if (movie == null) return new DollSequence { Label = symbol, Error = "нет client.swf: " + reason };
            return DollWorker.Packed(Doll.RenderClip(movie, symbol, scale, 1, false));
        }, result => MainThread.Post(() =>
        {
            if (FrameCache.Generation != generation) return;
            var sequence2 = result as DollSequence ?? new DollSequence { Label = symbol, Error = result is Exception ex ? ex.Message : "пусто" };
            bool ok = sequence2.Error == null && sequence2.Frames.Count > 0;
            if (!ok)
            {
                FrameCache.EndSequence(sequence, false);
                string trouble = sequence2.Error ?? "нет кадров";
                if (trouble.Contains("client.swf"))
                {
                    if (!_noClient)
                    {
                        _noClient = true;
                        Plugin.Log.LogInfo("[магия] картинок заклинаний пока нет: " + trouble + ". Ролик берётся с сервера игры, при следующем заходе попробую снова; остальное работает как обычно");
                    }
                    return;
                }
                Plugin.Log.LogWarning($"[магия] {symbol}: {trouble}");
                return;
            }
            FrameCache.Remember(look, sequence2.Labels, sequence2.FrameRate);
            FrameCache.Store(look, symbol, sequence, sequence2.Frames, NominalPpu, () =>
            {
                if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[магия] {symbol} готов, кадров {sequence2.Frames.Count}");
            });
        }), priority);
    }
}

internal sealed class FxPlayer : MonoBehaviour
{
    private AbstractCharacter _target;
    private string _look;
    private string _up;
    private string _down;
    private Image _upImage;
    private static Canvas _overlay;
    private SpriteRenderer _downView;
    private float _born;
    private float _since;
    private Vector3 _anchor;
    private float _start = -1f;
    private const float Handoff = 0.25f;
    private int _count;
    private int _countUp;
    private int _countDown;
    private double _rate = 20.0;

    public void Init(AbstractCharacter target, string look, string up, string down, float since)
    {
        _target = target;
        _look = look;
        _up = up;
        _down = down;
        _born = Time.time;
        _since = since;
        var doll = Fighters.DollOf(target);
        _anchor = doll != null && doll.Placed ? doll.Feet : target.position;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        _downView = Make("down", -1, shader);
        _upImage = MakeOverlay();
        var container = target.GetComponentInChildren<Transform>();
        if (container != null)
        {
            transform.SetParent(container.root == container ? container : container, false);
            gameObject.layer = container.gameObject.layer;
        }
    }

    private SpriteRenderer Make(string name, int order, Shader shader)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var view = go.AddComponent<SpriteRenderer>();
        view.sortingOrder = order;
        if (shader != null) view.sharedMaterial = new Material(shader);
        return view;
    }

    private static Canvas Overlay()
    {
        if (_overlay != null) return _overlay;
        var go = new GameObject("NewAge2D.FxCanvas", typeof(RectTransform), typeof(Canvas));
        _overlay = go.GetComponent<Canvas>();
        _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlay.sortingOrder = -1;
        return _overlay;
    }

    private Image MakeOverlay()
    {
        var go = new GameObject("NewAge2D.Fx.up", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(Overlay().transform, false);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.enabled = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        return image;
    }

    private void PlaceOverlay(Camera eye, Sprite sprite, float ppu)
    {
        if (_upImage == null || sprite == null) return;
        float place = SpellFx.PlacePpu;
        var origin = _anchor + eye.transform.right * (-55f / place) + eye.transform.up * (33f / place);
        var at = eye.WorldToScreenPoint(origin);
        if (at.z <= 0f)
        {
            _upImage.enabled = false;
            return;
        }
        var step = eye.WorldToScreenPoint(origin + eye.transform.up * (1f / ppu));
        float pixel = ((Vector2)step - (Vector2)at).magnitude;
        var rect = _upImage.rectTransform;
        rect.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height) * pixel;
        rect.anchoredPosition = new Vector2(at.x, at.y);
        _upImage.sprite = sprite;
        _upImage.enabled = true;
    }

    private void OnDestroy()
    {
        if (_downView != null && _downView.sharedMaterial != null) Destroy(_downView.sharedMaterial);
        if (_upImage != null) Destroy(_upImage.gameObject);
    }

    private void LateUpdate()
    {
        if (_target == null || !_target.Initialized)
        {
            Destroy(gameObject);
            return;
        }
        var view = CombatView.Get();
        var eye = view != null ? view.CombatCamera : Camera.main;
        if (eye == null) return;

        if (_start < 0f)
        {
            var standing = Fighters.DollOf(_target);
            _anchor = standing != null && standing.Placed ? standing.Feet : _target.position;
            string upSequence = FrameCache.SequenceKey(_look, _up);
            string downSequence = FrameCache.SequenceKey(_look, _down);
            if (!FrameCache.HasSequence(upSequence) || !FrameCache.HasSequence(downSequence))
            {
                if (Time.time - _born > 4f) Destroy(gameObject);
                return;
            }
            float began = -1f;
            var caster = Fighters.DollOf(_target);
            if (caster != null && !caster.Broken)
            {
                bool waiting = caster.CastWaiting(_since, out began);
                if (waiting && Time.time - _born <= 3.5f) return;
                if (!waiting && began < 0f && Time.time - _born <= Handoff) return;
            }
            _countUp = Mathf.Max(1, FrameCache.CountOf(upSequence));
            _countDown = Mathf.Max(1, FrameCache.CountOf(downSequence));
            _count = Mathf.Max(_countUp, _countDown);
            _rate = FrameCache.RateFor(_look);
            _start = began >= 0f ? began : Time.time;
            if (Trace.On && caster != null) Trace.Write($"«{_target.Login}» аура {_up} стартует {(began >= 0f ? $"вместе с действием куклы, разница {(Time.time - began) * 1000f:0} мс" : "без действия куклы")}, ждала {(Time.time - _born) * 1000f:0} мс");
        }

        int frame = (int)((Time.time - _start) * _rate);
        if (frame >= _count)
        {
            Destroy(gameObject);
            return;
        }

        float ppu = SpellFx.PpuFor(_target);
        float nominal = SpellFx.NominalPpu;
        Fighters.TowardEye(eye, _anchor, 1f, out var shift, out float near);
        float scale = nominal / ppu * near;
        transform.position = _anchor + shift;
        transform.rotation = eye.transform.rotation;
        transform.localScale = new Vector3(scale, scale, scale);
        float sin = Mathf.Clamp(-eye.transform.forward.y, 0.05f, 1f);
        var flat = eye.transform.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        flat.Normalize();
        float place = SpellFx.PlacePpu;
        _downView.transform.position = _anchor + eye.transform.right * (-55f / place) + flat * (33f / (place * sin)) + Vector3.up * 0.08f;
        _downView.transform.rotation = Quaternion.LookRotation(-Vector3.up, flat);
        _downView.transform.localScale = new Vector3(1f / near, 1f / (sin * near), 1f / near);
        var doll = Fighters.DollOf(_target);
        if (doll != null) _downView.sortingOrder = doll.SortingOrder - 1;
        int up = FrameCache.Source(FrameCache.SequenceKey(_look, _up), Mathf.Min(frame, _countUp - 1));
        int down = FrameCache.Source(FrameCache.SequenceKey(_look, _down), Mathf.Min(frame, _countDown - 1));
        if (FrameCache.TryGet(FrameCache.FrameKey(_look, _up, up), out var upSprite)) PlaceOverlay(eye, upSprite, ppu);
        if (FrameCache.TryGet(FrameCache.FrameKey(_look, _down, down), out var downSprite)) _downView.sprite = downSprite;
    }
}
