using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Combat.States;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class EffectsWindow
    {
        private const float PanelW = 420f;
        private const float PanelH = 360f;
        private const float TopH = 38f;
        private const float RowH = 17f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Text _title, _bars, _branch;
        private static Transform _rows;
        private static ScrollRect _scroll;
        private static string _sig = "";
        private static int _shownId;
        private static int _headId = -1, _headLevel = -1;
        private static bool _headMe, _headFresh;
        private static int _barLife, _barMaxLife, _barMana, _barMaxMana, _barStamina, _barStaminaTop, _barFar;
        private static bool _barFriend, _barBars, _barMe;
        private static float _pollAt;
        private static int _rowIndex;
        private static readonly StringBuilder Sig = new StringBuilder();


        internal static void Tick()
        {
            try
            {
                bool combat = SideButtons.InCombat();
                if (!combat)
                {
                    Column(false);
                    if (_canvasGo != null) Close();
                    return;
                }
                Column(true);
                if (_canvasGo == null) return;
                if (_panelGo == null) { Close(); return; }
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.25f;
                Refresh();
            }
            catch (Exception e) { Plugin.Trace("[эффекты] " + e.Message); }
        }

        private static GameObject _theirs;

        private static void Column(bool hide)
        {
            try
            {
                if (_theirs == null)
                {
                    if (!hide) return;
                    var ctrl = Controllers.Get<EnchantmentPanelsController>();
                    if (ctrl == null) return;
                    var panel = AccessTools.Property(typeof(EnchantmentPanelsController), "SelectedCharacterPanel")?.GetValue(ctrl) as AbstractCharacterPanel;
                    if (panel == null) return;
                    var grid = AccessTools.Field(typeof(AbstractCharacterPanel), "enchantmentsPanel")?.GetValue(panel) as MonoBehaviour;
                    if (grid == null) return;
                    _theirs = grid.gameObject;
                }
                if (_theirs.activeSelf == !hide) return;
                _theirs.SetActive(!hide);
            }
            catch (Exception e) { Plugin.Trace("[эффекты] колонка состояний: " + e.Message); }
        }

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Open();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        private static void Open()
        {
            try
            {
                Build();
                _sig = "";
                _shownId = 0;
                Refresh();
            }
            catch (Exception e) { Plugin.Fault("[эффекты] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); }
            _canvasGo = null;
            _panelGo = null;
            _rows = null;
            _scroll = null;
            _title = null;
            _bars = null;
            _branch = null;
            _shownId = 0;
            _headFresh = false;
        }

        private static AbstractCharacter Target(ICombatData cd)
        {
            if (cd == null) return null;
            var sel = cd.SelectedCharacter;
            if (sel != null && sel.UserId != 0) return sel;
            return cd.MyCharacter;
        }

        private static void Refresh()
        {
            var cd = FighterHint.Cd();
            var ch = Target(cd);
            if (ch == null)
            {
                if (_title != null) _title.text = "Эффекты";
                _headFresh = false;
                return;
            }
            if (ch.UserId != _shownId)
            {
                _shownId = ch.UserId;
                _sig = "";
                FighterHint.Ask(ch.UserId);
            }
            else
            {
                float at;
                if (!FighterHint.AskedAt.TryGetValue(ch.UserId, out at) || Time.unscaledTime - at > 3f) FighterHint.Ask(ch.UserId);
            }

            bool me = cd.MyCharacter != null && cd.MyCharacter.UserId == ch.UserId;
            bool friend = me || (cd.MyCharacter != null && ch.Team == cd.MyCharacter.Team);
            bool fresh = !_headFresh;
            _headFresh = true;
            if (fresh || _headId != ch.UserId || _headMe != me || _headLevel != ch.Level)
            {
                _headId = ch.UserId;
                _headMe = me;
                _headLevel = ch.Level;
                _title.text = "Эффекты: " + (ch.Login ?? "?") + (me ? " (ты)" : "") + (ch.Level > 0 ? "   " + ch.Level + " ур." : "");
            }
            var ind = ch.Indicators;
            bool bars = ind != null;
            int far = me ? 0 : FighterHint.Far(ch, cd);
            int spNow = 0, spTop = 0;
            if (bars)
            {
                spNow = friend ? ind.CurrentStamina : 0;
                spTop = friend ? ind.MaxStamina : 0;
            }
            if (fresh || _barBars != bars || _barFriend != friend || _barFar != far || _barMe != me
                || _barStamina != spNow || _barStaminaTop != spTop
                || (bars && (_barLife != ind.CurrentLife || _barMaxLife != ind.MaxLife
                             || _barMana != ind.CurrentMana || _barMaxMana != ind.MaxMana)))
            {
                _barBars = bars;
                _barFriend = friend;
                _barFar = far;
                _barMe = me;
                _barLife = bars ? ind.CurrentLife : 0;
                _barMaxLife = bars ? ind.MaxLife : 0;
                _barMana = bars ? ind.CurrentMana : 0;
                _barMaxMana = bars ? ind.MaxMana : 0;
                _barStamina = spNow;
                _barStaminaTop = spTop;
                string steps = FighterHint.Steps(ch, cd, me);
                _bars.text = (ind == null ? "" :
                    "<color=#ff6a5a>" + ind.CurrentLife + " / " + ind.MaxLife + "</color>   "
                    + "<color=#6db3ff>" + ind.CurrentMana + " / " + ind.MaxMana + "</color>"
                    + (friend ? "   <color=#ffd257>" + spNow + " / " + spTop + "</color>" : ""))
                    + (steps.Length > 0 ? (ind == null ? "" : "   ") + "<color=#82e1ff>" + steps + "</color>" : "");
            }

            if (_branch != null)
            {
                _branch.text = ch.UserId > 0 && !me ? Branches.Text(ch.UserId) : "";
                _branch.gameObject.SetActive(_branch.text.Length > 0);
            }

            List<UserEnchantmentsResponseItem> items;
            bool known = FighterHint.Effects(ch.UserId, out items);
            var sig = Sig;
            sig.Length = 0;
            sig.Append(ch.UserId).Append('|').Append(FighterHint.NamesVersion).Append('|');
            if (known)
                foreach (var it in items)
                {
                    sig.Append(it.StateType).Append(':').Append(it.StateId).Append(':').Append(it.Duration).Append(':').Append(it.Highlighting).Append(':').Append(FighterHint.Power(it)).Append(':');
                    if (it.Sources != null) foreach (var s in it.Sources) sig.Append(s.SourceUserId).Append('/');
                    sig.Append(';');
                }
            else sig.Append(FighterHint.Silent(ch.UserId) ? "?!" : "?");
            string now = sig.ToString();
            if (now == _sig) return;
            _sig = now;

            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _rowIndex = 0;
            if (!known) { AddRow(FighterHint.Status(ch, cd, false), "", "", "", WardrobeLook.Body); return; }
            if (items.Count == 0) { AddRow("нет эффектов", "", "", "", WardrobeLook.Body); return; }
            foreach (var it in items)
            {
                string key = "states.state_" + it.StateType + "_" + it.StateId;
                string name = ResourceStrings.GetString(key + ".name");
                if (name == key + ".name") name = "состояние " + it.StateType + "/" + it.StateId;
                string src = FighterHint.Sources(it, cd, ch.UserId);
                int power = FighterHint.Power(it);
                string dur = it.Duration > 1000 ? "до конца боя" : it.Duration > 0 ? it.Duration + " " + FighterHint.Turns(it.Duration) : "";
                Color32 col = it.Highlighting == (int)EHighlightingType.Positive ? WardrobeLook.Good
                            : it.Highlighting == (int)EHighlightingType.Negative ? WardrobeLook.Bad
                            : WardrobeLook.Body;
                AddRow(name, src, power != 0 ? power.ToString() : "", dur, col);
            }
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLEffectsWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = LoadPos();
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = WardrobeLook.Window;
            pimg.sprite = OnlineWindow.Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopH), new Vector2(0f, 0f));
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;
            mover.OnDone = SavePos;

            _title = Label(_panelGo.transform, "Эффекты", 15, FontStyle.Bold, WardrobeLook.Bright);
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -34f), new Vector2(-52f, -6f));
            _title.alignment = TextAnchor.MiddleLeft;
            _title.raycastTarget = false;

            MakeCloseButton();

            _bars = Label(_panelGo.transform, "", 10, FontStyle.Bold, Color.white);
            _bars.supportRichText = true;
            Place(_bars.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -56f), new Vector2(-12f, -38f));

            _branch = Label(_panelGo.transform, "", 10, FontStyle.Bold, WardrobeLook.Label);
            Place(_branch.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -74f), new Vector2(-12f, -56f));
            _branch.alignment = TextAnchor.MiddleCenter;
            _branch.raycastTarget = false;

            var headGo = new GameObject("head", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)headGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -94f), new Vector2(-26f, -76f));
            FillRow(headGo, "Название", "Источник", "Эффект", "Длительность", WardrobeLook.Label, FontStyle.Bold);
            var ruleGo = new GameObject("rule", typeof(RectTransform), typeof(Image));
            ruleGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)ruleGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -96f), new Vector2(-26f, -95f));
            ruleGo.GetComponent<Image>().color = WardrobeLook.Edge;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 10f), new Vector2(-26f, -98f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(8);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 30f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = cont;
            _scroll.viewport = srt;
            _rows = contentGo.transform;

            var sbGo = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)sbGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-22f, 10f), new Vector2(-12f, -80f));
            sbGo.GetComponent<Image>().color = WardrobeLook.Field;
            var sb = sbGo.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(sbGo.transform, false);
            Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            var hrt = (RectTransform)handle.transform;
            Place(hrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handle.GetComponent<Image>().color = WardrobeLook.FieldEdge;
            sb.handleRect = hrt;
            sb.targetGraphic = handle.GetComponent<Image>();
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static void AddRow(string name, string src, string power, string dur, Color32 color)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(_rows, false);
            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            bg.color = (_rowIndex++ & 1) == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowH; le.minHeight = RowH;
            FillRow(go, name, src, power, dur, color, FontStyle.Normal);
        }

        private static void FillRow(GameObject go, string name, string src, string power, string dur, Color32 color, FontStyle style)
        {
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(4, 4, 0, 0);
            h.spacing = 6f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            Cell(go.transform, name, 132f, style, color, TextAnchor.MiddleLeft, false);
            Cell(go.transform, src, 110f, style, color, TextAnchor.MiddleLeft, true);
            Cell(go.transform, power, 38f, style, color, TextAnchor.MiddleRight, false);
            Cell(go.transform, dur, 84f, style, color, TextAnchor.MiddleLeft, false);
        }

        private static void Cell(Transform row, string text, float width, FontStyle style, Color32 color, TextAnchor align, bool shrink)
        {
            var t = Label(row, text, 10, style, color);
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            if (shrink) { t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = 10; }
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = RowH - 2f; le.minHeight = RowH - 2f;
        }









        private static void MakeCloseButton()
        {
            GameObject go = null;
            try
            {
                var proto = VisualPrefabsHolder.Instance.SummonPlayerDialog?.GetComponent<SummonPlayerDialog>()?.CloseButton;
                if (proto != null)
                {
                    go = UnityEngine.Object.Instantiate(proto.gameObject, _panelGo.transform, false);
                    go.name = "QoLClose";
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(14f, 14f);
                    rt.localScale = Vector3.one * 0.75f;
                    var b = go.GetComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(Close);
                    go.SetActive(true);
                    return;
                }
            }
            catch { if (go != null) UnityEngine.Object.Destroy(go); }
            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(_panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(24f, 24f); crt.anchoredPosition = new Vector2(-6f, -6f);
            closeGo.GetComponent<Image>().color = WardrobeLook.Danger;
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var x = Label(closeGo.transform, "X", 14, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static Vector2 LoadPos()
        {
            var pos = new Vector2(0f, PanelH * 0.5f + 60f);
            try
            {
                var parts = (Plugin.CfgEffectsWindow?.Value ?? "").Split(';');
                float x, y;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out y)) pos = new Vector2(x, y);
            }
            catch (Exception e) { Plugin.Trace("[эффекты] позиция: " + e.Message); }
            return pos;
        }

        private static void SavePos()
        {
            try
            {
                if (_panelGo == null || Plugin.CfgEffectsWindow == null) return;
                var rt = (RectTransform)_panelGo.transform;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Plugin.CfgEffectsWindow.Value = rt.anchoredPosition.x.ToString("0", ci) + ";" + rt.anchoredPosition.y.ToString("0", ci);
            }
            catch (Exception e) { Plugin.Trace("[эффекты] позиция: " + e.Message); }
        }

        private static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        private static Text Label(Transform host, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = text;
            return t;
        }
    }
}
