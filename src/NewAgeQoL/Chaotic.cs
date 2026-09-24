using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Chaotic
    {
        private const float PanelW = 1120f;
        private const float PanelH = 520f;
        private const float HeadH = 28f;
        private const float RowH = 30f;
        private const float MedalW = 26f;

        private static readonly float[] Columns = { 300f, 120f, 110f, 110f, 120f };

        private static readonly string[] Titles =
        {
            "Создатель", "Уровни", "Раунд", "Игроков", "До старта",
        };

        private static readonly string[] Names =
        {
            "Бой", "Уровни", "Раунд", "Игроков", "До старта",
        };

        private static float _next;
        internal static int Room = -1;
        private static BaseEnterfightView _view;
        private static GameObject _panel;
        private static Transform _rows;
        private static CanvasGroup _hidden;
        private static Behaviour _autoBar;
        private static Image _paper;
        private static string _shape = "";

        private static readonly System.Reflection.FieldInfo AnnounceField = AccessTools.Field(typeof(BaseEnterfightWidget), "_announce");
        private static readonly Dictionary<int, Text> _clocks = new Dictionary<int, Text>();
        private static readonly Dictionary<int, Text> _sources = new Dictionary<int, Text>();


        internal static void Keep()
        {
            if (_panel == null) return;
            try
            {
                foreach (var go in Folded)
                    if (go != null && go.activeSelf) go.SetActive(false);
                if (_hidden != null)
                {
                    if (_hidden.alpha > 0.01f) _hidden.alpha = 0f;
                    if (_hidden.blocksRaycasts) _hidden.blocksRaycasts = false;
                }
                if (_paper != null && _paper.enabled) _paper.enabled = false;
                if (_autoBar != null && _autoBar.enabled) _autoBar.enabled = false;
            }
            catch (Exception e) { Plugin.Trace("[заявки] поздний кадр: " + e.Message); }
        }

        internal static void Wake()
        {
            _next = 0f;
            _noRail = false;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            try
            {
                var view = Seek();
                Strip(view);
                if (view != null) Room = Loc();
                if (view == null)
                {
                    if (Dozing()) return;
                    Restore();
                    _view = view;
                    return;
                }
                if (_view != view || _panel == null)
                {
                    if (_view != null && _view != view) Restore();
                    _view = view;
                    _noRail = false;
                    var main = Main();
                    Plugin.Trace("[заявки] окно " + view.GetType().Name + " переделываю в список; MainContainer "
                        + (main != null ? "есть" : "НЕ НАЙДЕН") + ", ScrollContainerMask "
                        + (main != null && main.Find("ScrollContainerMask") != null ? "есть" : "нет"));
                    Build();
                    if (_panel == null) return;
                }
                if (!_panel.activeSelf) _panel.SetActive(true);
                Fold();
                Lift(true);
                Shape();
                Fill();
                Clocks();
            }
            catch (Exception e) { Plugin.Fault("[заявки] " + e.Message); Restore(); }
        }

        private static float _seekAt;

        private static bool Dozing()
        {
            if (_view == null || _panel == null || _view.gameObject.activeInHierarchy) return false;
            var parent = _panel.transform.parent;
            if (parent != null && parent.gameObject.activeInHierarchy && _panel.activeSelf) _panel.SetActive(false);
            return true;
        }

        private static BaseEnterfightView Seek()
        {
            if (_view != null && _view.gameObject.activeInHierarchy) return _view;
            if (SideButtons.InCombat()) return null;
            bool hot = SideButtons.WindowsHot || SideButtons.SceneFresh;
            if (!hot && Time.unscaledTime < _seekAt) return null;
            _seekAt = Time.unscaledTime + 0.5f;
            return UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
        }

        private static int Loc()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null ? ud.CurrentLocationId : -1;
            }
            catch { return -1; }
        }

        private static ScrollRect _rail;
        private static bool _noRail;
        private static Scrollbar _railAcross, _railDown;
        private static RectTransform _head;
        private static Vector2 _headHome;
        private static bool _headUp;

        private static void Strip(BaseEnterfightView any)
        {
            if (_rail != null)
            {
                if (_railAcross != null && _railAcross.gameObject.activeSelf) _railAcross.gameObject.SetActive(false);
                if (_railDown != null && _railDown.gameObject.activeSelf) _railDown.gameObject.SetActive(false);
                return;
            }
            if (_noRail || any == null) return;
            var mask = any.transform.parent;
            var rail = mask != null ? mask.GetComponent<ScrollRect>() : null;
            if (rail == null) { _noRail = true; return; }
            _rail = rail;
            _railAcross = rail.horizontalScrollbar;
            _railDown = rail.verticalScrollbar;
            rail.horizontalScrollbar = null;
            rail.verticalScrollbar = null;
            var hider = mask.GetComponent<AutoHideUIScrollbar>();
            if (hider != null) hider.enabled = false;
            if (_railAcross != null) _railAcross.gameObject.SetActive(false);
            if (_railDown != null) _railDown.gameObject.SetActive(false);
            Plugin.Trace("[заявки] полоса прокрутки списка убрана");
        }

        private static void Lift(bool up)
        {
            if (_head == null)
            {
                var main = Main();
                _head = main != null ? main.Find("HeaderGroup") as RectTransform : null;
                if (_head == null) return;
                _headHome = _head.anchoredPosition;
                _headUp = false;
            }
            var want = up ? _headHome + new Vector2(0f, 60f) : _headHome;
            if ((_head.anchoredPosition - want).sqrMagnitude > 0.25f) _head.anchoredPosition = want;
            _headUp = up;
        }

        private static Transform _main;
        private static BaseEnterfightView _mainOf;

        private static Transform Main()
        {
            if (_main != null && ReferenceEquals(_mainOf, _view)) return _main;
            _mainOf = _view;
            var canvas = _view != null ? _view.GetComponentInParent<Canvas>() : null;
            _main = canvas != null ? canvas.transform.Find("MainContainer") : null;
            return _main;
        }

        private const int FoldTries = 5;

        private static readonly List<GameObject> Folded = new List<GameObject>();
        private static readonly List<Image> Bars = new List<Image>();
        private static int _foldTries;
        private static bool _swept;

        private static void Sweep(Transform main)
        {
            if (main == null) return;
            foreach (var art in main.GetComponentsInChildren<Image>(true))
            {
                if (art == null || !art.enabled) continue;
                var rt = art.rectTransform;
                if (rt.rect.width > 600f && rt.rect.height < 30f) { art.enabled = false; Bars.Add(art); }
            }
        }

        private static void Fold()
        {
            var main = Main();
            if (main == null) return;

            if (Folded.Count == 0 && _foldTries < FoldTries)
            {
                _foldTries++;
                var back = main.Find("Background");
                if (back != null) Folded.Add(back.gameObject);
                var canvas = _view != null ? _view.GetComponentInParent<Canvas>() : null;
                var host = canvas != null ? canvas.transform : main;
                foreach (var bar in host.GetComponentsInChildren<Scrollbar>(true))
                    if (bar != null) Folded.Add(bar.gameObject);
            }
            foreach (var go in Folded)
                if (go != null && go.activeSelf) go.SetActive(false);

            if (!_swept) { _swept = true; Sweep(main); }

            if (_hidden == null || _autoBar == null || _paper == null)
            {
                var mask = main.Find("ScrollContainerMask");
                if (_hidden == null)
                {
                    var host = mask != null ? mask : Cards();
                    if (host != null)
                    {
                        _hidden = host.GetComponent<CanvasGroup>();
                        if (_hidden == null) _hidden = host.gameObject.AddComponent<CanvasGroup>();
                    }
                }
                if (_autoBar == null && mask != null) _autoBar = mask.GetComponent<AutoHideUIScrollbar>();
                if (_paper == null && mask != null) _paper = mask.GetComponent<Image>();
            }
            if (_autoBar != null && _autoBar.enabled) _autoBar.enabled = false;
            if (_paper != null && _paper.enabled) _paper.enabled = false;

            if (_hidden != null)
            {
                if (_hidden.alpha > 0.01f) _hidden.alpha = 0f;
                if (_hidden.blocksRaycasts) _hidden.blocksRaycasts = false;
            }
        }

        private static Transform Cards()
        {
            if (_view == null) return null;
            var caption = AccessTools.Field(typeof(BaseEnterfightView), "Caption")?.GetValue(_view) as Text;
            if (caption != null && caption.transform.IsChildOf(_view.transform)) return null;
            Plugin.Trace("[заявки] карточки прячу по самому окну: ScrollContainerMask не найден");
            return _view.transform;
        }

        private static void Shape()
        {
            if (_panel == null || _view == null) return;
            var canvas = _view.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var area = canvas.transform as RectTransform;
            if (area == null || area.rect.height < 100f) return;

            float top = Header(area);
            var maker = Maker();
            Dress(maker);
            float makerHigh = maker != null ? maker.rect.height * maker.lossyScale.y / Mathf.Max(0.001f, area.lossyScale.y) : 0f;
            float bottom = area.rect.yMin + ChatDock.PanelHeight + 12f + (makerHigh > 0f ? makerHigh + 10f : 34f);
            float high = Mathf.Max(180f, top - bottom);
            var rt = (RectTransform)_panel.transform;
            var size = new Vector2(Mathf.Min(PanelW, area.rect.width - 260f), high);
            if ((rt.sizeDelta - size).sqrMagnitude > 1f) rt.sizeDelta = size;
            var want = new Vector2(0f, (top + bottom) * 0.5f);
            if ((rt.anchoredPosition - want).sqrMagnitude > 1f) rt.anchoredPosition = want;
            Seat(maker, rt);
            Under();
        }

        private static RectTransform _maker;
        private static Vector3 _makerHome;
        private static bool _makerMoved;
        private static bool _dressed;

        private static readonly List<Graphic> Trims = new List<Graphic>();

        internal static bool EscapeClose()
        {
            try
            {
                var view = _view as ChaoticEnterfightView;
                if (view == null) view = UnityEngine.Object.FindObjectOfType<ChaoticEnterfightView>();
                if (view == null || view.CreateAnnouncePanel == null || !view.CreateAnnouncePanel.activeInHierarchy) return false;
                view.CreateAnnounceCancelClick();
                Plugin.Trace("[заявки] окно создания заявки закрыто по Escape");
                return true;
            }
            catch (Exception e) { Plugin.Trace("[заявки] Escape: " + e.Message); return false; }
        }

        private static void Dress(RectTransform maker)
        {
            if (_dressed || maker == null || maker.rect.width < 10f) return;
            _dressed = true;
            Strip(maker);
        }

        private static void Strip(RectTransform maker)
        {
            try
            {
                var canvas = _view != null ? _view.GetComponentInParent<Canvas>() : maker.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.transform : maker.parent;
                if (root == null) return;
                var box = new Vector3[4];
                maker.GetWorldCorners(box);
                float left = box[0].x, right = box[2].x, low = box[0].y, top = box[2].y;
                float wide = right - left, tall = top - low, middle = (left + right) * 0.5f;
                if (wide <= 1f || tall <= 1f) return;
                var own = maker.GetComponent<Graphic>();
                var mine = maker.GetComponent<Button>();
                var told = new StringBuilder();
                foreach (var art in root.GetComponentsInChildren<Graphic>(true))
                {
                    if (art == null || art == own || !art.enabled || !art.gameObject.activeInHierarchy) continue;
                    if (art is Text || art is TMPro.TMP_Text) continue;
                    if (_panel != null && art.transform.IsChildOf(_panel.transform)) continue;
                    var owner = art.GetComponentInParent<Button>();
                    if (owner != null && owner != mine) continue;
                    art.rectTransform.GetWorldCorners(box);
                    float l = box[0].x, r = box[2].x, b = box[0].y, t = box[2].y;
                    float w = r - l, h = t - b, cy = (b + t) * 0.5f, cx = (l + r) * 0.5f;
                    if (w <= 1f || h <= 1f || cy < low - tall * 0.5f || cy > top + tall * 0.5f) continue;
                    bool nearLeft = r > left - wide * 0.25f && l < left + wide * 0.2f;
                    bool nearRight = l < right + wide * 0.25f && r > right - wide * 0.2f;
                    bool piece = w < wide * 0.4f && h < tall * 2.5f && (nearLeft || nearRight);
                    bool frame = w >= wide * 0.9f && w <= wide * 1.5f && h < tall * 2.5f && Mathf.Abs(cx - middle) < wide * 0.1f;
                    if (!piece && !frame) continue;
                    var image = art as Image;
                    told.Append(art.GetType().Name).Append(' ').Append(art.name)
                        .Append(image != null && image.sprite != null ? "=" + image.sprite.name : "")
                        .Append(' ').Append(Mathf.RoundToInt(w / wide * 100f)).Append('%').Append(frame ? " рамка; " : " у края; ");
                    art.enabled = false;
                    Trims.Add(art);
                }
                Plugin.Trace("[заявки] украшения у кнопки создания: " + (told.Length > 0 ? told.ToString() : "не найдены"));
            }
            catch (Exception e) { Plugin.Trace("[заявки] украшения кнопки: " + e.Message); }
        }

        private static RectTransform Maker()
        {
            if (_maker != null) return _maker;
            var main = Main();
            if (main == null) return null;
            var buttons = main.GetComponentsInChildren<Button>(true);
            bool wired = true;
            var found = Creator(buttons, true);
            if (found == null) { wired = false; found = Creator(buttons, false); }
            if (found == null) return null;
            _maker = found.transform as RectTransform;
            if (_maker != null) { _makerHome = _maker.position; _makerMoved = false; }
            Plugin.Trace("[заявки] кнопка создания: " + found.gameObject.name + (wired ? ", по обработчику" : ", по имени объекта"));
            return _maker;
        }

        private static Button Creator(Button[] buttons, bool byHandler)
        {
            foreach (var button in buttons)
            {
                if (button == null || !button.gameObject.activeInHierarchy) continue;
                if (button.GetComponentInParent<BaseEnterfightWidget>() != null) continue;
                bool fits = byHandler
                    ? Opens(button)
                    : button.gameObject.name.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0;
                if (fits) return button;
            }
            return null;
        }

        private static bool Opens(Button button)
        {
            var click = button.onClick;
            if (click == null) return false;
            int count = click.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
                if (click.GetPersistentTarget(i) is BaseEnterfightView
                    && click.GetPersistentMethodName(i) == nameof(BaseEnterfightView.OnCreateButtonClick))
                    return true;
            return false;
        }

        private static void Seat(RectTransform maker, RectTransform panel)
        {
            if (maker == null || panel == null) return;
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float scale = maker.lossyScale.y;
            float x = (corners[0].x + corners[3].x) * 0.5f;
            float y = corners[0].y - 8f * scale - maker.rect.height * scale * (1f - maker.pivot.y);
            var want = new Vector3(x, y, maker.position.z);
            if ((maker.position - want).sqrMagnitude > 0.01f) maker.position = want;
            _makerMoved = true;
        }

        private static void Under()
        {
            var main = Main();
            if (main == null || _panel == null || _panel.transform.parent != main.parent) return;
            int want = main.GetSiblingIndex() + 1;
            if (_panel.transform.GetSiblingIndex() != want) _panel.transform.SetSiblingIndex(want);
        }

        private static void Restore()
        {
            if (!_dressed && _panel == null && _hidden == null && _autoBar == null && _paper == null && Folded.Count == 0) return;
            try
            {
                if (_hidden != null) { _hidden.alpha = 1f; _hidden.blocksRaycasts = true; }
                if (_autoBar != null) _autoBar.enabled = true;
                if (_paper != null) _paper.enabled = true;
                foreach (var go in Folded) if (go != null) go.SetActive(true);
                foreach (var art in Bars) if (art != null) art.enabled = true;
                foreach (var art in Trims) if (art != null) art.enabled = true;
                if (_head != null && _headUp) Lift(false);
                if (_maker != null && _makerMoved) _maker.position = _makerHome;
                if (_panel != null) UnityEngine.Object.Destroy(_panel);
            }
            catch (Exception e) { Plugin.Trace("[заявки] возврат: " + e.Message); }
            Folded.Clear();
            Bars.Clear();
            Trims.Clear();
            Waiting.Clear();
            Going.Clear();
            Stamp.Length = 0;
            _foldTries = 0;
            _swept = false;
            _main = null;
            _mainOf = null;
            _dressed = false;
            _head = null;
            _headUp = false;
            _autoBar = null;
            _paper = null;
            _panel = null;
            _rows = null;
            _hidden = null;
            _maker = null;
            _makerMoved = false;
            _shape = "";
            _clocks.Clear();
            _sources.Clear();
        }

        private static void Build()
        {
            var canvas = _view != null ? _view.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return;

            _shape = "";
            _foldTries = 0;
            _swept = false;
            _clocks.Clear();
            _sources.Clear();

            _panel = new GameObject("QoLFightList", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panel.transform.SetParent(canvas.transform, false);
            Under();
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelW, PanelH);
            rt.anchoredPosition = new Vector2(0f, 0f);
            var back = _panel.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var edge = _panel.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var head = new GameObject("head", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            head.transform.SetParent(_panel.transform, false);
            OnlineWindow.Place((RectTransform)head.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(10f, -(HeadH + 8f)), new Vector2(-10f, -8f));
            var headRow = head.GetComponent<HorizontalLayoutGroup>();
            headRow.spacing = 6f;
            headRow.childAlignment = TextAnchor.MiddleLeft;
            headRow.childControlWidth = true;
            headRow.childControlHeight = true;
            headRow.childForceExpandWidth = false;
            headRow.childForceExpandHeight = true;
            Cell(head.transform, "", MedalW, 13, WardrobeLook.Label);
            var titles = _view is ChaoticEnterfightView ? Titles : Names;
            for (int i = 0; i < titles.Length; i++) Cell(head.transform, titles[i], Columns[i], 13, WardrobeLook.Label);

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panel.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(10f, 10f), new Vector2(-10f, -(HeadH + 12f)));
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 32f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var list = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            list.transform.SetParent(scrollGo.transform, false);
            var lrt = (RectTransform)list.transform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var group = list.GetComponent<VerticalLayoutGroup>();
            group.spacing = 2f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = list.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = lrt;
            scroll.viewport = srt;
            _rows = list.transform;
        }

        private static float Header(RectTransform area)
        {
            try
            {
                var main = Main();
                var head = main != null ? main.Find("HeaderGroup") : null;
                var rt = head as RectTransform;
                if (rt != null)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    float low = area.InverseTransformPoint(corners[0]).y;
                    if (low > area.rect.yMin && low < area.rect.yMax) return low + 26f;
                }
            }
            catch (Exception e) { Plugin.Trace("[заявки] шапка: " + e.Message); }
            return area.rect.yMax - 268f;
        }

        private static Text Cell(Transform host, string text, float width, int size, Color tint)
        {
            var label = OnlineWindow.Label(host, text, size, FontStyle.Bold, tint);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var size2 = label.gameObject.AddComponent<LayoutElement>();
            size2.preferredWidth = width;
            size2.minWidth = width;
            if (width <= 0f) { size2.flexibleWidth = 1f; size2.minWidth = 60f; }
            return label;
        }

        private static readonly StringBuilder Stamp = new StringBuilder();
        private static readonly List<BaseEnterfightWidget> Waiting = new List<BaseEnterfightWidget>();
        private static readonly List<BaseEnterfightWidget> Going = new List<BaseEnterfightWidget>();

        private static void Fill()
        {
            if (_view == null || _rows == null) return;
            Stamp.Length = 0;
            Waiting.Clear();
            Going.Clear();
            foreach (var widget in _view.Widgets)
            {
                if (widget == null) continue;
                bool started = Started(widget);
                Stamp.Append(widget.Id).Append(started ? 's' : 'o').Append(',');
                if (started) Going.Add(widget);
                else Waiting.Add(widget);
            }
            if (Same(Stamp, _shape)) return;
            _shape = Stamp.ToString();

            for (int i = _rows.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            _clocks.Clear();
            _sources.Clear();

            foreach (var widget in Waiting) Row(widget, false);
            if (Waiting.Count > 0 && Going.Count > 0) Divider();
            foreach (var widget in Going) Row(widget, true);
            _swept = false;
        }

        private static bool Same(StringBuilder made, string had)
        {
            if (had == null || made.Length != had.Length) return false;
            for (int i = 0; i < made.Length; i++)
                if (made[i] != had[i]) return false;
            return true;
        }

        private static bool Started(BaseEnterfightWidget widget)
        {
            return Unity3DHelper.FindInChild(widget.gameObject, "QoLStarted") != null;
        }

        private static bool Elite(BaseEnterfightWidget widget)
        {
            try
            {
                var announce = AnnounceField != null ? AnnounceField.GetValue(widget) as FightAnnounce : null;
                return announce != null && announce.ClaimType == (int)EClaimType.EliteChaotic;
            }
            catch { return false; }
        }

        private static string MedalName(int kind, int max)
        {
            switch ((EClaimType)kind)
            {
                case EClaimType.Chaotic: return max == 8 ? "chaoticSilver" : max == 10 ? "chaoticGold" : "chaoticBronze";
                case EClaimType.EliteChaotic: return "chaoticPlatinum";
                case EClaimType.Spades: return "four_of_spades";
                case EClaimType.Hashing: return "meatgrinder";
                case EClaimType.Dozen: return "dozen_blades";
                case EClaimType.Festival: return "festival_of_druids";
                default: return "chaoticBronze";
            }
        }

        private static Sprite MedalOf(BaseEnterfightWidget widget, bool started)
        {
            try
            {
                var announce = AnnounceField != null ? AnnounceField.GetValue(widget) as FightAnnounce : null;
                if (announce == null) return null;
                if (started)
                    return announce.ClaimType > 1 ? AtlasUtils.GetFightCreateAtlasSprite(MedalName(announce.ClaimType, Spectate.ClaimMax(widget.Id))) : null;
                var enter = Unity3DHelper.FindInChild(widget.gameObject, "EnterButton");
                var art = enter != null ? enter.GetComponent<Image>() : null;
                if (art != null && art.sprite != null) return art.sprite;
                return AtlasUtils.GetFightCreateAtlasSprite(MedalName(announce.ClaimType, announce.MaxCount));
            }
            catch { return null; }
        }

        private static void Medal(Transform host, BaseEnterfightWidget widget, bool started)
        {
            var go = new GameObject("medal", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var size = go.GetComponent<LayoutElement>();
            size.preferredWidth = MedalW;
            size.minWidth = MedalW;
            var sprite = MedalOf(widget, started);
            if (sprite == null) return;
            var pic = new GameObject("pic", typeof(RectTransform), typeof(Image));
            pic.transform.SetParent(go.transform, false);
            var rt = (RectTransform)pic.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(MedalW, MedalW);
            var image = pic.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static void Divider()
        {
            var go = new GameObject("divider", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_rows, false);
            var size = go.GetComponent<LayoutElement>();
            size.preferredHeight = 12f;
            size.minHeight = 12f;
            var line = new GameObject("line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)line.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(8f, -1f), new Vector2(-8f, 1f));
            var image = line.GetComponent<Image>();
            image.color = WardrobeLook.Edge;
            image.raycastTarget = false;
        }

        private static void Row(BaseEnterfightWidget widget, bool started)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(Button),
                                    typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(_rows, false);
            var size = go.GetComponent<LayoutElement>();
            size.preferredHeight = RowH;
            size.minHeight = RowH;
            var line = go.GetComponent<HorizontalLayoutGroup>();
            line.padding = new RectOffset(8, 8, 0, 0);
            line.spacing = 6f;
            line.childAlignment = TextAnchor.MiddleLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = true;

            bool elite = Elite(widget);

            var back = go.GetComponent<Image>();
            back.color = started
                ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Bad, 0.22f)
                : (elite ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.22f) : WardrobeLook.Tab);
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;

            Medal(go.transform, widget, started);
            var white = elite ? WardrobeLook.Accent : WardrobeLook.Body;
            Cell(go.transform, Words(widget, "HeaderText"), Columns[0], 14, started ? WardrobeLook.Bad : elite ? WardrobeLook.Accent : WardrobeLook.Bright);
            Cell(go.transform, Words(widget, "LevelsText"), Columns[1], 14, white);
            Cell(go.transform, Words(widget, "PhaseText"), Columns[2], 14, white);
            Cell(go.transform, Words(widget, "PlayersText"), Columns[3], 14, white);

            var clock = Cell(go.transform, "", Columns[4], 14, WardrobeLook.Accent);
            var timer = widget.GetComponent<CountdownTimer>();
            if (timer == null) timer = widget.GetComponentInChildren<CountdownTimer>(true);
            if (!started)
            {
                _clocks[widget.Id] = clock;
                if (timer != null && timer.TimerText != null)
                {
                    _sources[widget.Id] = timer.TimerText;
                    clock.text = timer.TimerText.text;
                }
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            var host = widget.gameObject;
            float lastClick = -10f;
            button.onClick.AddListener(() =>
            {
                float now = Time.unscaledTime;
                bool twice = now - lastClick < 0.45f;
                lastClick = twice ? -10f : now;
                if (!twice) return;
                try
                {
                    var enter = Unity3DHelper.FindInChild(host, "EnterButton");
                    var click = enter != null ? enter.GetComponent<Button>() : null;
                    if (click != null) click.onClick.Invoke();
                }
                catch (Exception e) { Plugin.Warn("[заявки] вход: " + e.Message); }
            });
        }

        private static void Clocks()
        {
            foreach (var pair in _clocks)
            {
                var label = pair.Value;
                if (label == null) continue;
                Text source;
                string text = _sources.TryGetValue(pair.Key, out source) && source != null ? source.text : "";
                if (text.Length > 0 && label.text != text) label.text = text;
            }
        }

        private static string Words(BaseEnterfightWidget widget, string field)
        {
            try
            {
                var text = AccessTools.Field(widget.GetType(), field)?.GetValue(widget) as Text;
                return text != null ? text.text : "";
            }
            catch { return ""; }
        }
    }
}
