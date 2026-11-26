using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class CombatBar
    {
        private const float High = 46f;
        private const float Reserve = 86f;
        private const float Pad = 10f;
        private const float Space = 5f;
        private const float Split = 12f;
        internal const float CellSide = 38f;
        internal const float SmallSide = 32f;
        private const float ClockHigh = 28f;
        private const float TimerW = 58f;
        private const float DigitW = 15f;
        private const float DotW = 8f;
        private const float Roomy = 1100f;
        private const int ShapeSide = 96;
        private const float PhaseTrim = 0.22f;
        private const float AttackTrim = 0.15f;

        private sealed class Taken
        {
            internal Transform Home;
            internal int Index;
            internal Vector3 Scale;
            internal Vector2 Min, Max, Pivot, Pos, Size;
        }

        private static readonly Dictionary<Transform, Taken> Held = new Dictionary<Transform, Taken>();
        private static readonly List<RectTransform> Top = new List<RectTransform>();
        private static readonly List<RectTransform> Seats = new List<RectTransform>();

        private sealed class Seated
        {
            internal GameObject Source;
            internal Image Pic;
            internal Image Mirror;
            internal Sprite Raw;
            internal Image Glow;
            internal bool Ready;
            internal bool Left;
            internal Button Press;
            internal Button Game;
        }

        private static readonly List<Seated> Faces2 = new List<Seated>();
        private static readonly Dictionary<GameObject, float> Pressed = new Dictionary<GameObject, float>();
        private const float Wink = 0.15f;
        private const float Flash = 0.34f;
        private static readonly Dictionary<GameObject, bool> Veiled = new Dictionary<GameObject, bool>();

        private const float StatesGap = 6f;
        private const float StatesTop = 12f;

        private sealed class WasOn
        {
            internal float Alpha;
            internal bool Blocks;
            internal bool Ours;
        }

        private sealed class Bare
        {
            internal RectTransform Root;
            internal RectTransform Grid;
            internal Vector2 Home;
            internal readonly Dictionary<CanvasGroup, WasOn> Groups = new Dictionary<CanvasGroup, WasOn>();
            internal readonly Dictionary<Graphic, bool> Paint = new Dictionary<Graphic, bool>();
        }

        private static Bare _bare;
        private static readonly Vector3[] StateCorners = new Vector3[4];
        private static readonly string[] Pieces = { "_timerBar", "_timerText" };

        private static GameObject _stripGo;
        private static RectTransform _strip;
        private static RectTransform _top;
        private static RectTransform _mid;
        private static RectTransform _hold;
        private static Text _round;
        private static RectTransform _time;
        private static readonly List<Text> Digits = new List<Text>();
        private static string _countText;
        private static string _roundSaid;
        private static int _countSaid = int.MinValue;
        private static float _tabWide;
        private static AccessTools.FieldRef<PhaseTimerScript, float> _leftRef;
        private static AccessTools.FieldRef<PhaseTimerScript, bool> _liveRef;
        private static bool _countWired;
        private static float _next;
        private static float _since;
        private static PhaseTimerScript _clock;
        private static readonly EHotkeyActions[] Silenced = { EHotkeyActions.Tricks, EHotkeyActions.Things, EHotkeyActions.Magic };
        private static readonly Dictionary<EHotkeyActions, HotkeyHandler> Kept = new Dictionary<EHotkeyActions, HotkeyHandler>();
        private static bool _muted;

        internal static bool On => ChatDock.Active && SideButtons.InCombat();


        internal static float Height => _strip == null ? 0f : High + ClockHigh;

        internal static float Spare => _strip == null ? 0f : Mathf.Max(0f, Reserve - Height);

        internal static float Row => _strip == null ? 0f : High;

        internal static float TabHigh => ClockHigh;

        internal static float TabWide => _strip == null || string.IsNullOrEmpty(_roundSaid) ? 0f : _tabWide;

        private static float Span => _strip != null ? _strip.sizeDelta.x : ChatDock.Span;

        private static float Left
        {
            get
            {
                var area = ChatDock.Area;
                float whole = area != null && area.rect.width > 100f ? area.rect.width : 1920f;
                return (whole - Span) * 0.5f;
            }
        }

        private static float PairHalf => CellSide + TimerW * 0.5f;

        private static float Middle => Left + Span * 0.5f;

        internal static float SkillsX
        {
            get
            {
                float row = SkillList.TrickCount * (SmallSide + SkillList.Gap) - SkillList.Gap;
                if (row < 0f) row = 0f;
                float x = Middle - PairHalf - Space - Split - row;
                return Mathf.Max(Left + Pad, x);
            }
        }

        internal static float SkillsY => ChatDock.PanelHeight + (High - SmallSide) * 0.5f;

        internal static float SkillsWide => Mathf.Max(SmallSide, Span * 0.5f - PairHalf - Space - Split - Pad);

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            Count();
            Pulse();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + (Seats.Count < 2 || Held.Count == 0 ? 0.02f : 0.15f);
            try
            {
                if (!On) { Drop(); return; }
                if (_strip != null && Stale()) { Plugin.Trace("[bar] fight changed without exit, rebuilding"); Drop(); }
                if (_strip == null) Build();
                if (_strip == null) return;
                Grab();
                Faces();
                Mute();
                Say();
                Effects();
                Place();
                Shade();
            }
            catch (Exception e) { Plugin.Caught("[bar] " + e.Message); Drop(); }
        }

        private static void Build()
        {
            var area = ChatDock.Area;
            if (area == null) return;

            _stripGo = new GameObject("QoLCombatBar", typeof(RectTransform), typeof(Image), typeof(Outline));
            Curtain.Stage(_stripGo);
            _stripGo.transform.SetParent(area, false);
            _strip = (RectTransform)_stripGo.transform;
            _since = Time.unscaledTime;

            var back = _stripGo.GetComponent<Image>();
            back.color = Color.clear;
            back.raycastTarget = false;
            _stripGo.GetComponent<Outline>().enabled = false;

            _top = Zone("clock", new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), Vector2.zero, ClockHigh);
            _top.sizeDelta = new Vector2(420f, ClockHigh);
            _mid = Zone("center", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-PairHalf, 0f), High);

            _hold = Zone("hidden clock", new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), Vector2.zero, ClockHigh);
            var veil = _hold.gameObject.AddComponent<CanvasGroup>();
            veil.alpha = 0f;
            veil.blocksRaycasts = false;
            veil.interactable = false;

            _round = OnlineWindow.Label(_top, "", 15, FontStyle.Normal, WardrobeLook.Label);
            OnlineWindow.Place((RectTransform)_round.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            _round.raycastTarget = false;
            _round.supportRichText = true;
            _round.horizontalOverflow = HorizontalWrapMode.Overflow;

            Capsule();
            Rule();

            var holder = new GameObject("time", typeof(RectTransform));
            holder.transform.SetParent(_mid, false);
            _time = (RectTransform)holder.transform;
            _time.anchorMin = _time.anchorMax = new Vector2(0f, 0.5f);
            _time.pivot = new Vector2(0.5f, 0.5f);
            _time.sizeDelta = new Vector2(TimerW, High);
            _time.anchoredPosition = new Vector2(CellSide + TimerW * 0.5f, 0f);
            _roundSaid = null;
            _countSaid = int.MinValue;
            _countText = null;
            _tabWide = 0f;
            Plugin.Trace("[bar] built");
        }

        private static void Capsule()
        {
            var go = new GameObject("capsule", typeof(RectTransform), typeof(Image), typeof(Outline));
            go.transform.SetParent(_mid, false);
            go.transform.SetAsFirstSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(CellSide * 2f + TimerW, CellSide);
            rt.anchoredPosition = Vector2.zero;
            var art = go.GetComponent<Image>();
            art.sprite = OnlineWindow.RoundedExact(Mathf.RoundToInt(CellSide * 0.5f));
            art.type = Image.Type.Sliced;
            art.color = WardrobeLook.Field;
            art.raycastTarget = false;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.FieldEdge;
            edge.effectDistance = new Vector2(1f, -1f);
            Seam(rt, CellSide);
            Seam(rt, CellSide + TimerW);
        }

        private static void Rule()
        {
            var go = new GameObject("rule", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_strip, false);
            var rt = (RectTransform)go.transform;
            OnlineWindow.Place(rt, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(18f, 0f), new Vector2(-18f, 1f));
            var art = go.GetComponent<Image>();
            art.color = WardrobeLook.Edge;
            art.raycastTarget = false;
        }

        private static void Seam(RectTransform host, float x)
        {
            var go = new GameObject("seam", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1f, CellSide);
            rt.anchoredPosition = new Vector2(x, 0f);
            var art = go.GetComponent<Image>();
            art.color = WardrobeLook.FieldEdge;
            art.raycastTarget = false;
        }

        private static readonly Dictionary<int, Sprite> Shapes = new Dictionary<int, Sprite>();

        private static Sprite Shaped(Sprite raw, bool left)
        {
            if (raw == null || raw.texture == null) return raw;
            int key = raw.GetInstanceID() * 2 + (left ? 0 : 1);
            Sprite made;
            if (Shapes.TryGetValue(key, out made) && made != null && made.texture != null) return made;
            try { made = Cut(raw, left); }
            catch (Exception e) { Plugin.Caught("[bar] shape " + raw.name + ": " + e.Message); made = null; }
            if (made == null) return raw;
            Shapes[key] = made;
            return made;
        }

        private static Sprite Cut(Sprite raw, bool left)
        {
            var source = raw.texture;
            Rect box = raw.textureRect;
            float edge = Mathf.Min(box.width, box.height) * (1f - (left ? PhaseTrim : AttackTrim) * 2f);
            float bx = box.x + (box.width - edge) * 0.5f, by = box.y + (box.height - edge) * 0.5f;
            int side = ShapeSide;
            var rt = RenderTexture.GetTemporary(side, side, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var was = RenderTexture.active;
            Texture2D copy;
            try
            {
                Graphics.Blit(source, rt, new Vector2(edge / source.width, edge / source.height), new Vector2(bx / source.width, by / source.height));
                RenderTexture.active = rt;
                copy = new Texture2D(side, side, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, side, side), 0, 0);
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
            var px = copy.GetPixels32();
            float r = side * 0.5f;
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    bool outer = left ? x < r : x >= r;
                    if (!outer) continue;
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float keep = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                    int at = y * side + x;
                    px[at].a = (byte)Mathf.RoundToInt(px[at].a * keep);
                }
            copy.SetPixels32(px);
            copy.Apply(false, true);
            copy.name = "QoLBarShape";
            copy.filterMode = FilterMode.Bilinear;
            copy.wrapMode = TextureWrapMode.Clamp;
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var art = Sprite.Create(copy, new Rect(0, 0, side, side), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            art.name = raw.name + (left ? "_left" : "_right");
            art.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return art;
        }

        private static Text Digit(int index)
        {
            while (Digits.Count <= index)
            {
                var one = OnlineWindow.Label(_time, "", 26, FontStyle.Bold, WardrobeLook.Bright);
                one.raycastTarget = false;
                one.horizontalOverflow = HorizontalWrapMode.Overflow;
                one.verticalOverflow = VerticalWrapMode.Overflow;
                var rt = one.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                Digits.Add(one);
            }
            return Digits[index];
        }

        private static void Draw(string text, Color tint)
        {
            float total = 0f;
            foreach (char c in text) total += c == '.' ? DotW : DigitW;
            float x = -total * 0.5f;
            int count = Math.Max(text.Length, Digits.Count);
            for (int i = 0; i < count; i++)
            {
                var cell = Digit(i);
                if (i >= text.Length) { if (cell.text.Length > 0) cell.text = ""; continue; }
                char c = text[i];
                float w = c == '.' ? DotW : DigitW;
                var rt = cell.rectTransform;
                var size = new Vector2(w, High);
                if (rt.sizeDelta != size) rt.sizeDelta = size;
                var spot = new Vector2(x + w * 0.5f, -0.7f);
                if (rt.anchoredPosition != spot) rt.anchoredPosition = spot;
                string one = c.ToString();
                if (cell.text != one) cell.text = one;
                if (cell.color != tint) cell.color = tint;
                x += w;
            }
        }

        private static void Say()
        {
            if (_round == null) return;
            string line = "";
            string widest = "";
            try
            {
                var cd = FighterHint.Cd();
                if (cd != null && cd.RoundNum > 0)
                {
                    line = RoundLine(cd.RoundNum.ToString(), cd.RoundType == RoundType.WALK_ROUND ? "Фаза ходьбы" : "Фаза боя");
                    widest = new string('8', Mathf.Max(2, cd.RoundNum.ToString().Length));
                }
            }
            catch (Exception e) { Plugin.Caught("[bar] round: " + e.Message); }
            if (line == _roundSaid) return;
            _roundSaid = line;
            float wide = 0f;
            if (line.Length > 0)
            {
                _round.text = RoundLine(widest, "Фаза ходьбы");
                wide = _round.preferredWidth;
                _round.text = RoundLine(widest, "Фаза боя");
                wide = Mathf.Max(wide, _round.preferredWidth);
            }
            _round.text = line;
            _tabWide = line.Length == 0 ? 0f : Mathf.Ceil(wide) + 44f;
        }

        private static string RoundLine(string num, string phase)
        {
            return Lang.T("Раунд") + " <b><color=#ECEEF1>" + num + "</color></b>   <color=#5A626E>|</color>   <color=#E2B85C>"
                   + Lang.T(phase) + "</color>";
        }

        private static void Count()
        {
            if (_time == null) return;
            int cs = -1;
            try
            {
                var script = _clock;
                if (script != null)
                {
                    if (!_countWired)
                    {
                        _countWired = true;
                        try
                        {
                            _leftRef = AccessTools.FieldRefAccess<PhaseTimerScript, float>("_roundTime");
                            _liveRef = AccessTools.FieldRefAccess<PhaseTimerScript, bool>("_initialized");
                        }
                        catch (Exception e) { Plugin.Caught("[bar] phase time: " + e.Message); }
                    }
                    if (_leftRef != null && (_liveRef == null || _liveRef(script)))
                        cs = Mathf.Max(0, Mathf.FloorToInt(_leftRef(script) * 100f));
                }
            }
            catch (Exception e) { Plugin.Caught("[bar] phase time: " + e.Message); _leftRef = null; }
            if (cs == _countSaid) return;
            _countSaid = cs;
            string text = cs < 0 ? "" : cs >= 100 ? (cs / 100).ToString() : "0." + cs.ToString("00");
            var tint = cs > 500 ? WardrobeLook.Bright : cs > 100 ? WardrobeLook.Accent : WardrobeLook.Bad;
            if (text == _countText && Digits.Count > 0 && Digits[0].color == tint) return;
            _countText = text;
            Draw(text, tint);
        }

        private static RectTransform Zone(string name, Vector2 anchor, Vector2 pivot, Vector2 spot, float tall)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_strip, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = spot;
            rt.sizeDelta = new Vector2(0f, tall);
            return rt;
        }

        private static float _need;

        internal static float Need
        {
            get
            {
                int count = SkillList.TrickCount;
                if (count <= 0) return Mathf.Max(_need, _strip != null ? Roomy : 0f);
                float row = count * (SmallSide + SkillList.Gap) - SkillList.Gap;
                float left = row + Pad + Space + Split + PairHalf;
                float right = PairHalf + Space + Split + Extra.Length * (SmallSide + Space) + Pad;
                float want = Mathf.Max(left, right) * 2f;
                if (want > _need) _need = want;
                return Mathf.Max(_need, Roomy);
            }
        }

        private static void Place()
        {
            _strip.anchorMin = _strip.anchorMax = new Vector2(0.5f, 0f);
            _strip.pivot = new Vector2(0.5f, 0f);
            var size = new Vector2(ChatDock.Span, High);
            if (_strip.sizeDelta != size) _strip.sizeDelta = size;
            var spot = new Vector2(0f, ChatDock.PanelHeight);
            if (_strip.anchoredPosition != spot) _strip.anchoredPosition = spot;
        }

        private static void Grab()
        {
            var ctrl = Controllers.Get<CombatButtonsController>();
            var timer = Clock();
            Component phase = Part(ctrl, "EndPhaseButton");
            if (phase == null) phase = Knob();
            Component attack = Part(ctrl, "AttackButton");

            Take(timer, _hold, Top);
            if (phase != null && (timer == null || !phase.transform.IsChildOf(timer.transform))) Seat(phase, 0);
            Seat(attack, 1);
        }

        private static Sprite InfoFace()
        {
            try
            {
                var holder = VisualPrefabsHolder.Instance;
                var own = holder != null ? holder.UserContextMenuInfoSprite : null;
                return Quickslots.Faded(own) ? Icons.Head() : own;
            }
            catch { return Icons.Head(); }
        }

        private static void Card()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null) return;
                int id = FighterHint.Under(cd, false);
                var who = id != 0 ? cd.GetCharacter(id) : cd.SelectedCharacter;
                if (who == null) { Plugin.Trace("[bar] info: no fighter selected"); return; }

                if (who.UserId > 0)
                {
                    var ctrl = Controllers.Get<UserInfoWindowController>();
                    if (ctrl == null) return;
                    ctrl.ShowUserInfoDialog(who.UserId, who.Login);
                    Plugin.Trace("[bar] info on " + (who.Login ?? who.UserId.ToString()));
                    return;
                }

                var beast = Controllers.Get<MonsterInfoWindowController>();
                if (beast == null) return;
                beast.ShowMonsterInfoDialog(who.Race);
                Plugin.Trace("[bar] info on mob, race " + who.Race);
            }
            catch (Exception e) { Plugin.Caught("[bar] info: " + e.Message); }
        }

        private static bool Stale()
        {
            if (!ReferenceEquals(_clock, null) && _clock == null) return true;
            foreach (var pair in Held)
                if (pair.Key == null || pair.Value.Home == null) return true;
            return false;
        }

        private static FieldInfo[] _clockFields;
        private static FieldInfo _phaseField;
        private static bool _clockWired;
        private static readonly List<RectTransform> Parts = new List<RectTransform>();

        private static void WireClock()
        {
            if (_clockWired) return;
            _clockWired = true;
            var found = new FieldInfo[Pieces.Length];
            for (int i = 0; i < Pieces.Length; i++)
            {
                try { found[i] = AccessTools.Field(typeof(PhaseTimerScript), Pieces[i]); }
                catch (Exception e) { Plugin.Caught("[bar] clock " + Pieces[i] + ": " + e.Message); }
            }
            _clockFields = found;
            try { _phaseField = AccessTools.Field(typeof(PhaseTimerScript), "_endPhaseButton"); }
            catch (Exception e) { Plugin.Caught("[bar] phase button: " + e.Message); }
        }

        private static PhaseTimerScript Script()
        {
            if (_clock != null) return _clock;
            var script = UnityEngine.Object.FindObjectOfType<PhaseTimerScript>();
            if (script == null)
                foreach (var one in Resources.FindObjectsOfTypeAll<PhaseTimerScript>())
                    if (one != null && one.gameObject.scene.IsValid()) { script = one; break; }
            _clock = script;
            return script;
        }

        private static Component Clock()
        {
            var script = Script();
            if (script == null) return null;
            WireClock();
            var parts = Parts;
            parts.Clear();
            for (int i = 0; i < _clockFields.Length; i++)
            {
                if (_clockFields[i] == null) continue;
                Component part = null;
                try { part = _clockFields[i].GetValue(script) as Component; }
                catch (Exception e) { Plugin.Caught("[bar] clock " + Pieces[i] + ": " + e.Message); }
                var rt = part != null ? part.transform as RectTransform : null;
                if (rt != null) parts.Add(rt);
            }
            if (parts.Count == 0) return script.transform is RectTransform ? script : null;
            if (_strip != null && parts[0].IsChildOf(_strip)) return null;

            var node = parts[0].parent as RectTransform;
            while (node != null)
            {
                bool all = true;
                foreach (var part in parts) if (!part.IsChildOf(node)) { all = false; break; }
                if (all) break;
                node = node.parent as RectTransform;
            }
            if (node == null || node.GetComponent<Canvas>() != null) return parts[0];

            while (true)
            {
                var up = node.parent as RectTransform;
                if (up == null || up.GetComponent<Canvas>() != null) break;
                if (up.rect.height <= 0f || up.rect.height >= 120f) break;
                node = up;
            }
            if (!Held.ContainsKey(node)) Plugin.Trace("[bar] phase clock: " + Chain(node));
            return node;
        }

        private static string Chain(RectTransform node)
        {
            var text = new StringBuilder();
            for (var at = node; at != null && text.Length < 400; at = at.parent as RectTransform)
            {
                if (text.Length > 0) text.Append(" < ");
                text.Append(at.name).Append(' ').Append(Mathf.RoundToInt(at.rect.width)).Append('x').Append(Mathf.RoundToInt(at.rect.height));
            }
            return text.ToString();
        }

        private static Component Knob()
        {
            var script = Script();
            if (script == null) return null;
            WireClock();
            try { return _phaseField?.GetValue(script) as Component; }
            catch (Exception e) { Plugin.Caught("[bar] phase button: " + e.Message); return null; }
        }

        private static Component Part(CombatButtonsController ctrl, string name)
        {
            if (ctrl == null) return null;
            try { return AccessTools.Property(typeof(CombatButtonsController), name)?.GetValue(ctrl) as Component; }
            catch (Exception e) { Plugin.Caught("[bar] " + name + ": " + e.Message); return null; }
        }

        private static bool Ours(RectTransform rt)
        {
            return _strip != null && (rt == _strip || rt.IsChildOf(_strip));
        }

        private static Taken Remember(RectTransform rt)
        {
            return new Taken
            {
                Home = rt.parent,
                Index = rt.GetSiblingIndex(),
                Scale = rt.localScale,
                Min = rt.anchorMin,
                Max = rt.anchorMax,
                Pivot = rt.pivot,
                Pos = rt.anchoredPosition,
                Size = rt.sizeDelta,
            };
        }

        private static void Take(Component part, RectTransform host, List<RectTransform> row)
        {
            if (part == null || host == null) return;
            var rt = part.transform as RectTransform;
            if (rt == null || Held.ContainsKey(rt) || Ours(rt)) return;

            var box = rt.rect;
            if (box.width < 2f || box.height < 2f) { Curtain.Hide(rt); return; }

            Curtain.Show(rt);
            Held[rt] = Remember(rt);
            rt.SetParent(host, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(box.width, box.height);
            row.Add(rt);
            Plugin.Trace("[bar] took " + rt.name + " " + Mathf.RoundToInt(box.width) + "x" + Mathf.RoundToInt(box.height));
        }

        private static void Seat(Component part, int index)
        {
            if (part == null || _mid == null) return;
            var rt = part.transform as RectTransform;
            if (rt == null || Held.ContainsKey(rt) || Ours(rt)) return;

            var box = rt.rect;
            if (box.width < 2f || box.height < 2f)
            {
                if (Time.unscaledTime < _since + 2f) { Curtain.Hide(rt); return; }
                box = new Rect(0f, 0f, 138f, 137f);
            }

            var frame = Frame(index);
            var face = Face(rt);
            var still = Still(rt);
            Held[rt] = Remember(rt);
            rt.SetParent(frame, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(box.width, box.height);
            rt.localScale = new Vector3(0.001f, 0.001f, 1f);
            Curtain.Show(rt);
            Picture(frame, still, still != null ? null : face, rt.gameObject, index == 0);
            var shown = still != null ? still : face != null ? face.sprite : null;
            Plugin.Trace("[bar] placed " + rt.name + " " + Mathf.RoundToInt(box.width) + "x" + Mathf.RoundToInt(box.height)
                + (shown != null ? ", image " + shown.name + (still != null ? " own" : " live") : ", no image"));
        }

        private static float Wide(int count)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i == 0) sum += CellSide + TimerW;
                else if (i == 1) sum += CellSide + Space + Split;
                else sum += SmallSide + Space;
            }
            return sum;
        }

        private static RectTransform Frame(int index)
        {
            while (Seats.Count <= index)
            {
                var go = new GameObject("seat", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(RectMask2D));
                go.transform.SetParent(_mid, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                float side = Seats.Count < 2 ? CellSide : SmallSide;
                rt.sizeDelta = new Vector2(side, side);
                rt.anchoredPosition = new Vector2(Wide(Seats.Count), 0f);

                bool round = Seats.Count >= 2;
                var back = go.GetComponent<Image>();
                back.color = round ? WardrobeLook.Card : Color.clear;
                back.sprite = OnlineWindow.Disc();
                back.type = Image.Type.Simple;
                back.raycastTarget = false;

                go.GetComponent<Outline>().enabled = false;
                go.GetComponent<RectMask2D>().enabled = false;

                if (round)
                {
                    var ringGo = new GameObject("ring", typeof(RectTransform), typeof(Image));
                    ringGo.transform.SetParent(go.transform, false);
                    OnlineWindow.Place((RectTransform)ringGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    var ring = ringGo.GetComponent<Image>();
                    ring.sprite = OnlineWindow.Ring();
                    ring.color = new Color32(150, 170, 190, 90);
                    ring.raycastTarget = false;
                    ringGo.transform.SetAsLastSibling();
                }

                Seats.Add(rt);
            }
            _mid.sizeDelta = new Vector2(Mathf.Max(0f, Wide(Seats.Count) - Space), High);
            return Seats[index];
        }

        private static Sprite Still(RectTransform rt)
        {
            try
            {
                var phase = rt.GetComponent<EndPhaseButton>();
                if (phase != null)
                {
                    var own = AccessTools.Field(typeof(EndPhaseButton), "EnabledSprite")?.GetValue(phase) as Sprite;
                    if (own != null) return own;
                }
                var state = rt.GetComponent<BaseStateButton>();
                if (state != null && state.NormalSprite != null) return state.NormalSprite;
            }
            catch (Exception e) { Plugin.Caught("[bar] own image: " + e.Message); }
            return null;
        }

        private static Image Face(RectTransform rt)
        {
            Image own = null;
            Component plate = null, badge = null;
            try
            {
                var phase = rt.GetComponent<EndPhaseButton>();
                if (phase != null) own = AccessTools.Field(typeof(EndPhaseButton), "BackgroundImage")?.GetValue(phase) as Image;
                var state = rt.GetComponent<BaseStateButton>();
                if (own == null && state != null) own = AccessTools.Field(typeof(BaseStateButton), "ButtonImage")?.GetValue(state) as Image;
                var command = rt.GetComponent<BaseCommandButton>();
                if (command != null) plate = AccessTools.Field(typeof(BaseCommandButton), "BottomImage")?.GetValue(command) as Component;
                var sector = rt.GetComponent<SimpleSectorButtonSelector>();
                if (sector != null) badge = AccessTools.Field(typeof(SimpleSectorButtonSelector), "RechargeImage")?.GetValue(sector) as Component;
            }
            catch (Exception e) { Plugin.Caught("[bar] button parts: " + e.Message); }

            Image best = null, clean = null, top = null;
            var told = new StringBuilder();
            foreach (var art in rt.GetComponentsInChildren<Image>(true))
            {
                if (art == null || art.sprite == null) continue;
                string tag = art.name;
                bool skip = (plate != null && art.transform.IsChildOf(plate.transform))
                    || (badge != null && art.transform.IsChildOf(badge.transform))
                    || tag.IndexOf("fon", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0
                    || art.color.a < 0.5f;
                told.Append(tag).Append('=').Append(art.sprite.name).Append(art == own ? "(own) " : skip ? "(skip) " : " ");
                if (skip) continue;
                best = art;
                if (top == null && (tag.IndexOf("TopImage", StringComparison.OrdinalIgnoreCase) >= 0
                                    || tag.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)) top = art;
                if (clean == null && art.sprite.name.IndexOf("fon", StringComparison.OrdinalIgnoreCase) < 0) clean = art;
            }
            Plugin.Trace("[bar] images " + rt.name + ": " + told);
            if (own != null && own.sprite != null) return own;
            return top != null ? top : clean != null ? clean : best;
        }

        private static void Picture(RectTransform frame, Sprite still, Image face, GameObject source, bool left)
        {
            var go = new GameObject("pic", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var pic = go.GetComponent<Image>();
            var raw = still != null ? still : face != null ? face.sprite : null;
            var own = Icons.Flash(left ? "bar_phase" : "bar_attack");
            pic.sprite = own != null ? own : Shaped(raw, left);
            pic.enabled = pic.sprite != null;
            pic.preserveAspect = false;
            var press = go.GetComponent<Button>();
            press.targetGraphic = pic;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() => Click(source));
            Image glow = null;
            var ring = own != null ? Icons.Flash(left ? "bar_glow_left" : "bar_glow_right") : null;
            if (ring != null)
            {
                var ringGo = new GameObject("glow", typeof(RectTransform), typeof(Image));
                ringGo.transform.SetParent(frame, false);
                OnlineWindow.Place((RectTransform)ringGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-Halo, -Halo), new Vector2(Halo, Halo));
                glow = ringGo.GetComponent<Image>();
                glow.sprite = ring;
                glow.raycastTarget = false;
                var tint = left ? new Color(1f, 0.86f, 0.45f, 0f) : new Color(1f, 0.55f, 0.45f, 0f);
                glow.color = tint;
                ringGo.transform.SetAsLastSibling();
            }
            Faces2.Add(new Seated { Source = source, Pic = pic, Press = press, Mirror = own != null ? null : face, Raw = raw, Left = left, Glow = glow });
        }

        private static readonly GameObject[] Extra = new GameObject[4];

        private static void Effects()
        {
            Make(0, "chase", ChaseFace, Chase);
            Make(1, "effects", () => Icons.Flash("effects"), EffectsWindow.Toggle);
            Make(2, "team", () => Icons.Flash("team"), ChatDock.TeamAim);
            Make(3, "info", InfoFace, Card);
            Lay(Spectate.Peeking);
        }

        private static void Lay(bool peeking)
        {
            if (_mid == null || Seats.Count < 3 || Seats[2] == null) return;
            var chase = Seats[2].gameObject;
            if (chase.activeSelf == peeking)
            {
                chase.SetActive(!peeking);
                Plugin.Trace("[bar] pursuit button " + (peeking ? "hidden while spectating" : "shown"));
            }
            float x = Wide(2);
            for (int i = 2; i < Seats.Count; i++)
            {
                var seat = Seats[i];
                if (seat == null || !seat.gameObject.activeSelf) continue;
                var spot = new Vector2(x, seat.anchoredPosition.y);
                if (seat.anchoredPosition != spot) seat.anchoredPosition = spot;
                x += SmallSide + Space;
            }
            var size = new Vector2(Mathf.Max(0f, x - Space), High);
            if (_mid.sizeDelta != size) _mid.sizeDelta = size;
        }

        private static void Make(int slot, string name, Func<Sprite> face, UnityEngine.Events.UnityAction act)
        {
            if (_mid == null) return;
            if (Extra[slot] != null)
            {
                var live = Extra[slot].GetComponentInChildren<Image>();
                if (live != null)
                {
                    var now = face();
                    if (!Quickslots.Faded(now) && live.sprite != now) live.sprite = now;
                    bool shown = !Quickslots.Faded(live.sprite);
                    if (live.enabled != shown) live.enabled = shown;
                }
                return;
            }

            var frame = Frame(2 + slot);
            if (frame == null) return;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(3f, 3f), new Vector2(-3f, -3f));
            var icon = go.GetComponent<Image>();
            icon.sprite = face();
            icon.enabled = !Quickslots.Faded(icon.sprite);
            icon.preserveAspect = true;
            var press = go.GetComponent<Button>();
            press.targetGraphic = icon;
            press.onClick.AddListener(act);
            Extra[slot] = go;
            Plugin.Trace("[bar] button \"" + name + "\" placed right of the combat buttons");
        }

        private static int _chaseId = int.MinValue;
        private static int _chaseAtlas;
        private static EQuickButtonType _chaseKind;
        private static Sprite _chaseArt;

        private static Sprite ChaseFace()
        {
            var one = ChaseButton();
            if (one == null) { _chaseId = int.MinValue; _chaseArt = null; return Icons.Blades(); }
            if (!Quickslots.Faded(_chaseArt) && _chaseId == one.Id && _chaseAtlas == one.Unity3DCombatIconAtlasId
                && _chaseKind == one.QuickButtonType) return _chaseArt;
            try
            {
                var sprite = AtlasUtils.GetQuickButtonSprite(one);
                if (Quickslots.Faded(sprite))
                {
                    _chaseId = int.MinValue;
                    _chaseArt = null;
                    return Icons.Blades();
                }
                _chaseId = one.Id;
                _chaseAtlas = one.Unity3DCombatIconAtlasId;
                _chaseKind = one.QuickButtonType;
                _chaseArt = sprite;
                return sprite;
            }
            catch { return Icons.Blades(); }
        }

        private static IQuickButton ChaseButton()
        {
            IQuickButton first = null;
            foreach (var one in SkillList.Of(SkillList.Kind.Tricks))
            {
                if (one == null) continue;
                if (one.Id != DodgesButtonStateHolder.HantingOnButtonId && one.Id != DodgesButtonStateHolder.HantingOffButtonId) continue;
                if (first == null) first = one;
                if (one.Enabled) return one;
            }
            return first;
        }

        private static void Chase()
        {
            var one = ChaseButton();
            if (one == null) { Plugin.Trace("[bar] no pursuit among dodges"); return; }
            SkillList.Use(one.Id);
        }

        private static FieldInfo _endField, _stateField;
        private static bool _knobWired;

        private static Button Knob(GameObject source)
        {
            if (source == null) return null;
            Button button = null;
            try
            {
                if (!_knobWired)
                {
                    _knobWired = true;
                    _endField = AccessTools.Field(typeof(EndPhaseButton), "EndButton");
                    _stateField = AccessTools.Field(typeof(BaseStateButton), "Button");
                }
                var phase = source.GetComponent<EndPhaseButton>();
                if (phase != null) button = _endField?.GetValue(phase) as Button;
                var state = source.GetComponent<BaseStateButton>();
                if (button == null && state != null) button = _stateField?.GetValue(state) as Button;
            }
            catch (Exception e) { Plugin.Caught("[bar] game button: " + e.Message); }
            if (button == null) button = source.GetComponent<Button>();
            if (button == null) button = source.GetComponentInChildren<Button>(true);
            return button;
        }

        private static bool Alive(Seated seat)
        {
            var source = seat.Source;
            if (source == null || !source.activeInHierarchy) return false;
            if (seat.Game == null) seat.Game = Knob(source);
            var button = seat.Game;
            return button == null || button.interactable;
        }

        private static bool Switch(GameObject source)
        {
            return source != null && source.GetComponent<EndPhaseButton>() == null
                   && source.GetComponent<BaseStateButton>() != null;
        }

        private static bool Stuck(GameObject source)
        {
            if (source == null || !Spectate.Peeking) return false;
            return source.name.IndexOf("Phase", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Click(GameObject source)
        {
            if (Stuck(source)) { Spectate.Exit(); Plugin.Trace("[bar] leaving spectate"); return; }
            if (source == null) return;
            if (!Switch(source)) Pressed[source] = Time.unscaledTime;
            try
            {
                var phase = source.GetComponent<EndPhaseButton>();
                if (phase != null)
                {
                    phase.OnClick();
                    Plugin.Trace("[bar] phase end via game button");
                    return;
                }
                var own = Knob(source);
                if (own != null && own.interactable)
                {
                    own.onClick.Invoke();
                    Plugin.Trace("[bar] pressed " + source.name + " via its button");
                    return;
                }
                if (!source.activeInHierarchy) return;
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerClickHandler);
                Plugin.Trace("[bar] pressed " + source.name);
            }
            catch (Exception e) { Plugin.Caught("[bar] press " + source.name + ": " + e.Message); }
        }

        private const float Beat = 1.4f;
        private const float Halo = 7f;

        private static void Pulse()
        {
            if (Faces2.Count == 0) return;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / Beat);
            foreach (var seat in Faces2)
            {
                var glow = seat.Glow;
                if (glow == null) continue;
                float want = seat.Ready ? Mathf.Lerp(0.25f, 1f, wave) : 0f;
                var c = glow.color;
                if (Mathf.Abs(c.a - want) < 0.01f) continue;
                c.a = want;
                glow.color = c;
            }
        }

        private static bool Settling()
        {
            try { return Strike.Calculating; }
            catch (Exception e) { Plugin.Caught("[bar] round state: " + e.Message); return false; }
        }

        private static void Shade()
        {
            bool settling = Settling();
            foreach (var seat in Faces2)
            {
                if (seat.Pic == null) continue;
                var mirror = seat.Mirror;
                var look = mirror != null ? mirror.sprite : null;
                if (look != null && look.texture != null && seat.Raw != look)
                {
                    seat.Raw = look;
                    seat.Pic.sprite = Shaped(look, seat.Left);
                }
                bool drawn = seat.Pic.sprite != null;
                if (seat.Pic.enabled != drawn) seat.Pic.enabled = drawn;
                var source = seat.Source;
                bool live = Alive(seat) && (!settling || (source != null && source.GetComponent<EndPhaseButton>() != null));
                if (!live && Stuck(source)) live = true;
                bool held = false;
                float age = -1f;
                float at;
                if (source != null && Pressed.TryGetValue(source, out at))
                {
                    age = Time.unscaledTime - at;
                    if (age < Wink) held = true;
                    if (age >= Flash && live) Pressed.Remove(source);
                }
                var calm = live ? Color.white : new Color(0.62f, 0.62f, 0.62f, 0.92f);
                var tint = calm;
                float pop = 1f;
                if (age >= 0f && age < Flash)
                {
                    float step = age / Flash;
                    tint = Color.Lerp(new Color(1.9f, 1.75f, 1.15f, 1f), calm, step * step);
                    pop = 0.82f + 0.18f * Mathf.Sqrt(step);
                }
                if (seat.Pic.color != tint) seat.Pic.color = tint;
                var swell = new Vector3(pop, pop, 1f);
                if ((seat.Pic.transform.localScale - swell).sqrMagnitude > 0.00005f) seat.Pic.transform.localScale = swell;
                bool usable = live && !held;
                seat.Ready = usable;
                if (seat.Press != null && seat.Press.interactable != usable) seat.Press.interactable = usable;
            }
        }

        private static void Faces()
        {
            try
            {
                var ctrl = Controllers.Get<EnchantmentPanelsController>();
                if (ctrl == null) return;
                Strip(Face(ctrl, "MyCharacterPanel"));
                Cover(Face(ctrl, "SelectedCharacterPanel"));
            }
            catch (Exception e) { Plugin.Caught("[bar] fighter panels: " + e.Message); }
        }

        private static void Strip(GameObject go)
        {
            if (go == null) return;
            if (_bare == null || _bare.Root == null || _bare.Root.gameObject != go)
            {
                Unstrip();
                var panel = go.GetComponent<AbstractCharacterPanel>();
                var grid = panel != null ? AccessTools.Field(typeof(AbstractCharacterPanel), "enchantmentsPanel")?.GetValue(panel) as Component : null;
                if (grid == null) { Cover(go); return; }
                var root = (RectTransform)go.transform;
                _bare = new Bare { Root = root, Grid = (RectTransform)grid.transform, Home = root.anchoredPosition };
                Plugin.Trace("[bar] own fighter panel hidden, effects column kept");
            }
            Hollow(_bare, _bare.Root, _bare.Grid);
            Shift(_bare);
        }

        private static void Hollow(Bare bare, Transform node, Transform grid)
        {
            foreach (var paint in node.GetComponents<Graphic>())
            {
                if (paint == null) continue;
                if (!bare.Paint.ContainsKey(paint)) bare.Paint[paint] = paint.enabled;
                if (paint.enabled) paint.enabled = false;
            }
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                if (child == grid) continue;
                if (grid.IsChildOf(child)) { Hollow(bare, child, grid); continue; }
                var group = child.GetComponent<CanvasGroup>();
                bool ours = group == null;
                if (ours) group = child.gameObject.AddComponent<CanvasGroup>();
                if (!bare.Groups.ContainsKey(group))
                    bare.Groups[group] = new WasOn { Alpha = group.alpha, Blocks = group.blocksRaycasts, Ours = ours };
                if (group.alpha != 0f) group.alpha = 0f;
                if (group.blocksRaycasts) group.blocksRaycasts = false;
            }
        }

        private static void Shift(Bare bare)
        {
            var parent = bare.Root.parent;
            if (parent == null || bare.Grid == null) return;
            float kx = parent.lossyScale.x, ky = parent.lossyScale.y;
            if (kx < 0.001f || ky < 0.001f) return;
            var canvas = bare.Root.GetComponentInParent<Canvas>();
            var top = canvas != null ? canvas.rootCanvas : null;
            Camera eye = top != null && top.renderMode != RenderMode.ScreenSpaceOverlay ? top.worldCamera : null;

            bare.Grid.GetWorldCorners(StateCorners);
            Vector2 low = RectTransformUtility.WorldToScreenPoint(eye, StateCorners[0]);
            Vector2 high = RectTransformUtility.WorldToScreenPoint(eye, StateCorners[2]);
            Vector2 moved = bare.Root.anchoredPosition - bare.Home;
            float left = low.x - moved.x * kx;
            float roof = high.y - moved.y * ky;

            float wantLeft = StatesGap * kx;
            float wantRoof = Screen.height - StatesTop * ky;
            var column = SideButtons.LeftPanel;
            if (column != null)
            {
                var own = column.GetComponentInParent<Canvas>();
                var ownTop = own != null ? own.rootCanvas : null;
                Camera ownEye = ownTop != null && ownTop.renderMode != RenderMode.ScreenSpaceOverlay ? ownTop.worldCamera : null;
                column.GetWorldCorners(StateCorners);
                Vector2 edge = RectTransformUtility.WorldToScreenPoint(ownEye, StateCorners[2]);
                wantLeft = edge.x + StatesGap * kx;
                wantRoof = edge.y;
            }

            var spot = bare.Home + new Vector2((wantLeft - left) / kx, (wantRoof - roof) / ky);
            if ((bare.Root.anchoredPosition - spot).sqrMagnitude > 1f) bare.Root.anchoredPosition = spot;
        }

        private static void Unstrip()
        {
            var bare = _bare;
            _bare = null;
            if (bare == null) return;
            foreach (var pair in bare.Groups)
            {
                if (pair.Key == null) continue;
                if (pair.Value.Ours) { UnityEngine.Object.Destroy(pair.Key); continue; }
                pair.Key.alpha = pair.Value.Alpha;
                pair.Key.blocksRaycasts = pair.Value.Blocks;
            }
            foreach (var pair in bare.Paint)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            if (bare.Root != null) bare.Root.anchoredPosition = bare.Home;
        }

        private static GameObject Face(EnchantmentPanelsController ctrl, string name)
        {
            try
            {
                var part = AccessTools.Property(typeof(EnchantmentPanelsController), name)?.GetValue(ctrl) as Component;
                return part != null ? part.gameObject : null;
            }
            catch { return null; }
        }

        private static void Cover(GameObject go)
        {
            if (go == null) return;
            if (!Veiled.ContainsKey(go)) Veiled[go] = go.activeSelf;
            if (go.activeSelf) go.SetActive(false);
        }

        internal static void Keep()
        {
            if (_strip == null) return;
            foreach (var pair in Veiled)
            {
                var go = pair.Key;
                if (go != null && go.activeSelf) go.SetActive(false);
            }
            Shade();
        }

        private static void Uncover()
        {
            foreach (var pair in Veiled)
            {
                var go = pair.Key;
                if (go == null) continue;
                if (go.activeSelf != pair.Value) go.SetActive(pair.Value);
            }
            Veiled.Clear();
            Unstrip();
        }

        private static void Mute()
        {
            if (_muted) return;
            try
            {
                var dispatcher = HotkeyDispatcher.Instance;
                if (dispatcher == null) return;
                foreach (var action in Silenced)
                {
                    Kept[action] = Handler(action);
                    dispatcher.ClearHandler(action);
                }
                _muted = true;
                Plugin.Trace("[bar] no longer listening to dodge and item keys");
            }
            catch (Exception e) { Plugin.Caught("[bar] dodge key: " + e.Message); }
        }

        private static void Unmute()
        {
            if (!_muted) return;
            _muted = false;
            try
            {
                var dispatcher = HotkeyDispatcher.Instance;
                if (dispatcher == null) { Kept.Clear(); return; }
                foreach (var action in Silenced)
                {
                    HotkeyHandler back;
                    if (!Kept.TryGetValue(action, out back) || back == null) back = Handler(action);
                    if (back != null) dispatcher.RegisterHandler(action, back);
                }
                Kept.Clear();
                Plugin.Trace("[bar] dodge and item keys returned to the game");
            }
            catch (Exception e) { Plugin.Caught("[bar] key return: " + e.Message); }
        }

        private static HotkeyHandler Handler(EHotkeyActions action)
        {
            foreach (var one in UnityEngine.Object.FindObjectsOfType<HotkeyHandler>())
                if (one != null && one.GetAction() == action) return one;
            return null;
        }

        internal static void Drop()
        {
            _need = 0f;
            if (_stripGo == null && Held.Count == 0 && Veiled.Count == 0 && _bare == null && !_muted) return;
            foreach (var pair in Held)
            {
                try
                {
                    var rt = pair.Key as RectTransform;
                    var was = pair.Value;
                    if (rt == null || was == null) continue;
                    if (was.Home != null)
                    {
                        rt.SetParent(was.Home, false);
                        rt.SetSiblingIndex(was.Index);
                    }
                    rt.localScale = was.Scale;
                    rt.anchorMin = was.Min;
                    rt.anchorMax = was.Max;
                    rt.pivot = was.Pivot;
                    rt.anchoredPosition = was.Pos;
                    rt.sizeDelta = was.Size;
                }
                catch (Exception e) { Plugin.Caught("[bar] button return: " + e.Message); }
            }
            Held.Clear();
            Top.Clear();
            Seats.Clear();
            Faces2.Clear();
            Pressed.Clear();
            Uncover();
            Unmute();
            for (int i = 0; i < Extra.Length; i++) Extra[i] = null;
            if (_stripGo != null) UnityEngine.Object.Destroy(_stripGo);
            _stripGo = null;
            _strip = null;
            _clock = null;
            _top = null;
            _mid = null;
            _hold = null;
            _round = null;
            _time = null;
            Digits.Clear();
            _roundSaid = null;
            _countSaid = int.MinValue;
            _countText = null;
            _tabWide = 0f;
        }
    }

    [HarmonyPatch(typeof(EnchantmentPanelsController), "OnCharacterSelection")]
    internal static class CombatBarSelectionPatch
    {
        private static void Postfix() => CombatBar.Keep();
    }

    [HarmonyPatch(typeof(EnchantmentPanelsController), "OnCharacterPanelClick")]
    internal static class CombatBarPanelClickPatch
    {
        private static void Postfix() => CombatBar.Keep();
    }
}
