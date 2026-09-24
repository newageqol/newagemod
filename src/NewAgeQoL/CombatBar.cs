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
        private const float Pad = 10f;
        private const float Space = 5f;
        private const float Split = 12f;
        internal const float CellSide = 36f;
        internal const float SmallSide = 32f;
        private const float Grow = CellSide * 0.21f;
        private const float ClockHigh = 40f;
        private const float Widest = 420f;

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
        private static float _next;
        private static float _since;
        private static PhaseTimerScript _clock;
        private static readonly EHotkeyActions[] Silenced = { EHotkeyActions.Tricks, EHotkeyActions.Things, EHotkeyActions.Magic };
        private static readonly Dictionary<EHotkeyActions, HotkeyHandler> Kept = new Dictionary<EHotkeyActions, HotkeyHandler>();
        private static bool _muted;

        internal static bool On => ChatDock.Active && SideButtons.InCombat();


        internal static float Height => _strip == null ? 0f : High + (Top.Count > 0 ? ClockHigh : 0f);

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

        private static float PairHalf => CellSide + Space * 0.5f;

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
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + (Seats.Count < 2 || Held.Count == 0 ? 0.02f : 0.15f);
            try
            {
                if (!On) { Drop(); return; }
                if (_strip != null && Stale()) { Plugin.Trace("[полоса] бой сменился без выхода, собираю заново"); Drop(); }
                if (_strip == null) Build();
                if (_strip == null) return;
                Grab();
                Faces();
                Mute();
                Fit(_top, Top);
                Effects();
                Place();
                Shade();
            }
            catch (Exception e) { Plugin.Trace("[полоса] " + e.Message); Drop(); }
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
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = _stripGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            _top = Zone("clock", new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), Vector2.zero, ClockHigh);
            _mid = Zone("center", new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-PairHalf, 0f), High);
            Plugin.Trace("[полоса] собрана");
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
                if (count <= 0) return _need;
                float row = count * (SmallSide + SkillList.Gap) - SkillList.Gap;
                float want = (row + Pad + Space + Split + PairHalf) * 2f;
                if (want > _need) _need = want;
                return _need;
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

            Take(timer, _top, Top);
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
                if (who == null) { Plugin.Trace("[полоса] информация: боец не выбран"); return; }

                if (who.UserId > 0)
                {
                    var ctrl = Controllers.Get<UserInfoWindowController>();
                    if (ctrl == null) return;
                    ctrl.ShowUserInfoDialog(who.UserId, who.Login);
                    Plugin.Trace("[полоса] информация по " + (who.Login ?? who.UserId.ToString()));
                    return;
                }

                var beast = Controllers.Get<MonsterInfoWindowController>();
                if (beast == null) return;
                beast.ShowMonsterInfoDialog(who.Race);
                Plugin.Trace("[полоса] информация по мобу, раса " + who.Race);
            }
            catch (Exception e) { Plugin.Trace("[полоса] информация: " + e.Message); }
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
                catch (Exception e) { Plugin.Trace("[полоса] часы " + Pieces[i] + ": " + e.Message); }
            }
            _clockFields = found;
            try { _phaseField = AccessTools.Field(typeof(PhaseTimerScript), "_endPhaseButton"); }
            catch (Exception e) { Plugin.Trace("[полоса] кнопка фазы: " + e.Message); }
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
                catch (Exception e) { Plugin.Trace("[полоса] часы " + Pieces[i] + ": " + e.Message); }
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
            if (!Held.ContainsKey(node)) Plugin.Trace("[полоса] часы фазы: " + Chain(node));
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
            catch (Exception e) { Plugin.Trace("[полоса] кнопка фазы: " + e.Message); return null; }
        }

        private static Component Part(CombatButtonsController ctrl, string name)
        {
            if (ctrl == null) return null;
            try { return AccessTools.Property(typeof(CombatButtonsController), name)?.GetValue(ctrl) as Component; }
            catch (Exception e) { Plugin.Trace("[полоса] " + name + ": " + e.Message); return null; }
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
            Plugin.Trace("[полоса] взял " + rt.name + " " + Mathf.RoundToInt(box.width) + "x" + Mathf.RoundToInt(box.height));
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
            Picture(frame, still, still != null ? null : face, rt.gameObject);
            var shown = still != null ? still : face != null ? face.sprite : null;
            Plugin.Trace("[полоса] посадил " + rt.name + " " + Mathf.RoundToInt(box.width) + "x" + Mathf.RoundToInt(box.height)
                + (shown != null ? ", картинка " + shown.name + (still != null ? " своя" : " живая") : ", картинки нет"));
        }

        private static float Wide(int count)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += (i < 2 ? CellSide : SmallSide) + Space;
                if (i == 1) sum += Split;
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
                back.color = WardrobeLook.Card;
                back.sprite = round ? OnlineWindow.Disc() : OnlineWindow.Rounded(6);
                back.type = round ? Image.Type.Simple : Image.Type.Sliced;
                if (!round) back.pixelsPerUnitMultiplier = 4f;
                back.raycastTarget = false;

                var edge = go.GetComponent<Outline>();
                edge.enabled = !round;
                edge.effectColor = WardrobeLook.FieldEdge;
                edge.effectDistance = new Vector2(2f, -2f);
                var mask = go.GetComponent<RectMask2D>();
                mask.enabled = !round;
                mask.padding = new Vector4(1f, 1f, 1f, 1f);

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
            catch (Exception e) { Plugin.Trace("[полоса] своя картинка: " + e.Message); }
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
            catch (Exception e) { Plugin.Trace("[полоса] части кнопки: " + e.Message); }

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
                told.Append(tag).Append('=').Append(art.sprite.name).Append(art == own ? "(своя) " : skip ? "(мимо) " : " ");
                if (skip) continue;
                best = art;
                if (top == null && (tag.IndexOf("TopImage", StringComparison.OrdinalIgnoreCase) >= 0
                                    || tag.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)) top = art;
                if (clean == null && art.sprite.name.IndexOf("fon", StringComparison.OrdinalIgnoreCase) < 0) clean = art;
            }
            Plugin.Trace("[полоса] картинки " + rt.name + ": " + told);
            if (own != null && own.sprite != null) return own;
            return top != null ? top : clean != null ? clean : best;
        }

        private static void Picture(RectTransform frame, Sprite still, Image face, GameObject source)
        {
            var go = new GameObject("pic", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-Grow, -Grow), new Vector2(Grow, Grow));
            var pic = go.GetComponent<Image>();
            pic.sprite = still != null ? still : face != null ? face.sprite : null;
            pic.enabled = pic.sprite != null;
            pic.preserveAspect = false;
            var press = go.GetComponent<Button>();
            press.targetGraphic = pic;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() => Click(source));
            Rim(frame);
            Faces2.Add(new Seated { Source = source, Pic = pic, Press = press, Mirror = face });
        }

        private static void Rim(RectTransform frame)
        {
            var go = new GameObject("rim", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var art = go.GetComponent<Image>();
            art.sprite = OnlineWindow.Rounded(6);
            art.type = Image.Type.Sliced;
            art.pixelsPerUnitMultiplier = 4f;
            art.fillCenter = false;
            art.color = WardrobeLook.FieldEdge;
            art.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        private static readonly GameObject[] Extra = new GameObject[4];

        private static void Effects()
        {
            Make(0, "chase", ChaseFace, Chase);
            Make(1, "effects", () => Icons.Flash("effects"), EffectsWindow.Toggle);
            Make(2, "team", () => Icons.Flash("team"), ChatDock.TeamAim);
            Make(3, "info", InfoFace, Card);
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
            Plugin.Trace("[полоса] кнопка «" + name + "» встала справа от боевых");
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
            if (one == null) { Plugin.Trace("[полоса] преследования среди приёмов нет"); return; }
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
            catch (Exception e) { Plugin.Trace("[полоса] кнопка игры: " + e.Message); }
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
            if (Stuck(source)) { Spectate.Exit(); Plugin.Trace("[полоса] выход из просмотра"); return; }
            if (source == null) return;
            if (!Switch(source)) Pressed[source] = Time.unscaledTime;
            try
            {
                var phase = source.GetComponent<EndPhaseButton>();
                if (phase != null)
                {
                    phase.OnClick();
                    Plugin.Trace("[полоса] конец фазы через кнопку игры");
                    return;
                }
                var own = Knob(source);
                if (own != null && own.interactable)
                {
                    own.onClick.Invoke();
                    Plugin.Trace("[полоса] нажал " + source.name + " через его кнопку");
                    return;
                }
                if (!source.activeInHierarchy) return;
                var pointer = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(source, pointer, ExecuteEvents.pointerClickHandler);
                Plugin.Trace("[полоса] нажал " + source.name);
            }
            catch (Exception e) { Plugin.Trace("[полоса] нажатие " + source.name + ": " + e.Message); }
        }

        private static void Shade()
        {
            foreach (var seat in Faces2)
            {
                if (seat.Pic == null) continue;
                var mirror = seat.Mirror;
                var look = mirror != null ? mirror.sprite : null;
                if (look != null && look.texture != null && seat.Pic.sprite != look) seat.Pic.sprite = look;
                bool drawn = seat.Pic.sprite != null;
                if (seat.Pic.enabled != drawn) seat.Pic.enabled = drawn;
                var source = seat.Source;
                bool live = Alive(seat);
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
                if (seat.Press != null && seat.Press.interactable != usable) seat.Press.interactable = usable;
            }
        }

        private static void Fit(RectTransform host, List<RectTransform> row)
        {
            if (host == null) return;
            for (int i = row.Count - 1; i >= 0; i--) if (row[i] == null) row.RemoveAt(i);

            float tallest = host.sizeDelta.y;
            float x = 0f;
            foreach (var rt in row)
            {
                float tall = rt.rect.height;
                float wide = rt.rect.width;
                float k = tall > 1f ? Mathf.Min(1f, (tallest - 4f) / tall) : 1f;
                if (wide * k > Widest) k = Widest / wide;
                if (Mathf.Abs(rt.localScale.x - k) > 0.001f) rt.localScale = new Vector3(k, k, 1f);
                var spot = new Vector2(x, 0f);
                if (rt.anchoredPosition != spot) rt.anchoredPosition = spot;
                x += wide * k + Space;
            }

            var size = new Vector2(Mathf.Max(0f, x - Space), tallest);
            if (host.sizeDelta != size) host.sizeDelta = size;
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
            catch (Exception e) { Plugin.Trace("[полоса] панели бойцов: " + e.Message); }
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
                Plugin.Trace("[полоса] панель своего бойца спрятана, столбец эффектов оставлен");
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
                Plugin.Trace("[полоса] клавиши приёмов и предметов больше не слушаем");
            }
            catch (Exception e) { Plugin.Trace("[полоса] клавиша приёмов: " + e.Message); }
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
                Plugin.Trace("[полоса] клавиши приёмов и предметов вернулись игре");
            }
            catch (Exception e) { Plugin.Trace("[полоса] возврат клавиши: " + e.Message); }
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
                catch (Exception e) { Plugin.Trace("[полоса] возврат кнопки: " + e.Message); }
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
