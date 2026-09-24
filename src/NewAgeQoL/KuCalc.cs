using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class KuCalc
    {
        private const float PanelW = 1240f;
        private const float PanelH = 780f;
        private const float BodyY = 172f;
        private const float BodyH = PanelH - BodyY - 52f;
        private const float TreeW = 820f;
        private const float DeskW = PanelW - TreeW - 44f;
        private const float Col = TreeW / 7f;
        private const float Icon = 60f;
        private const float NodeW = 110f;
        private const float NodeH = 122f;
        private const float TabW = 148f;
        private static readonly float[] Rows = { 18f, 152f, 286f, 420f };
        private static readonly int[] Cols = { 1, 1, 0, 1, 2, 0, 1, 2 };
        private static readonly int[] Tiers = { 0, 1, 2, 2, 2, 3, 3, 3 };

        private sealed class Node
        {
            internal int Id;
            internal Image Back;
            internal Outline Edge;
            internal Image Pic;
            internal Text Name;
            internal GameObject BadgeGo;
            internal Text Badge;
            internal readonly Image[] Pips = new Image[KuData.Top];
        }

        private sealed class Link
        {
            internal int Parent;
            internal int Child;
            internal Image Line;
        }

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static RectTransform _tree;
        private static Text _subText;
        private static Text _points;
        private static Text _leftTitle;
        private static Text _rightTitle;
        private static Image _deskIcon;
        private static Text _deskName;
        private static Text _deskKind;
        private static Text _deskNow;
        private static RectTransform _deskRows;
        private static ScrollRect _deskScroll;
        private static readonly List<RectTransform> Tabs = new List<RectTransform>();
        private static readonly List<Node> Nodes = new List<Node>();
        private static readonly List<Link> Links = new List<Link>();

        private static readonly Dictionary<int, Dictionary<int, int>> Plans = new Dictionary<int, Dictionary<int, int>>();
        private static int _class;
        private static readonly Dictionary<int, int> SubsByClass = new Dictionary<int, int>();
        private static int _shown;
        private static int _seenData;
        private static float _iconsAt;

        internal static bool IsOpen => _canvasGo != null;

        internal static void Open()
        {
            try
            {
                if (!KuData.Ready)
                {
                    Notice.Show("Калькулятор КУ: в сборке нет данных об умениях", 6f);
                    return;
                }
                Plans.Clear();
                _class = KuData.Classes[0].Id;
                SubsByClass.Clear();
                _shown = 0;
                _seenData = KuData.Version;
                KuData.Fetch();
                Build();
                Grow();
                Refresh();
                Plugin.Trace("[калькулятор ку] открыт: " + Describe());
            }
            catch (Exception e) { Plugin.Fault("[калькулятор ку] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panelGo = null;
            _tree = null;
            _subText = null;
            _points = null;
            _leftTitle = null;
            _rightTitle = null;
            _deskIcon = null;
            _deskName = null;
            _deskKind = null;
            _deskNow = null;
            _deskRows = null;
            _deskScroll = null;
            Tabs.Clear();
            Nodes.Clear();
            Links.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_seenData != KuData.Version) Reload();
            if (Time.unscaledTime < _iconsAt) return;
            _iconsAt = Time.unscaledTime + 0.5f;
            Icons();
        }

        private static void Reload()
        {
            _seenData = KuData.Version;
            if (!KuData.Ready) { Close(); return; }
            if (KuData.Class(_class) == null) _class = KuData.Classes[0].Id;
            Plans.Clear();
            _shown = 0;
            Grow();
            Refresh();
        }

        private static KuClass Klass => KuData.Class(_class);

        private static Dictionary<int, int> Plan
        {
            get
            {
                Dictionary<int, int> plan;
                if (!Plans.TryGetValue(_class, out plan))
                {
                    plan = new Dictionary<int, int>();
                    Plans[_class] = plan;
                }
                return plan;
            }
        }

        private static int Level(int id)
        {
            int level;
            return Plan.TryGetValue(id, out level) ? level : 0;
        }

        private static void Set(int id, int level)
        {
            if (level <= 0) Plan.Remove(id);
            else Plan[id] = Mathf.Min(level, KuData.Top);
        }

        private static int Spent
        {
            get
            {
                int sum = 0;
                foreach (var pair in Plan) sum += KuData.Cost(pair.Value);
                return sum;
            }
        }

        private static int Subs
        {
            get
            {
                int n;
                return SubsByClass.TryGetValue(_class, out n) ? n : 1;
            }
            set { SubsByClass[_class] = Mathf.Clamp(value, 1, KuData.Gain.Length); }
        }

        private static int Total => KuData.Points(Subs);

        private static int Branch(KuClass klass)
        {
            for (int i = 1; i < klass.Left.Length; i++) if (Level(klass.Left[i]) > 0) return 1;
            for (int i = 1; i < klass.Right.Length; i++) if (Level(klass.Right[i]) > 0) return 2;
            return 0;
        }

        private static bool CanRaise(KuClass klass, int id)
        {
            int level = Level(id);
            if (level >= KuData.Top) return false;
            int side = klass.Side(id);
            int branch = Branch(klass);
            if (side > 0 && branch > 0 && side != branch) return false;
            var skill = KuData.Skill(id);
            if (skill != null && skill.Parent != 0 && Level(skill.Parent) < level + 1) return false;
            return Total - Spent >= level + 1;
        }

        private static void Raise(int id)
        {
            var klass = Klass;
            if (klass != null && CanRaise(klass, id)) Set(id, Level(id) + 1);
        }

        private static void Lower(int id)
        {
            var klass = Klass;
            int level = Level(id);
            if (klass == null || level == 0) return;
            Set(id, level - 1);
            Trim(klass, id);
        }

        private static void Trim(KuClass klass, int id)
        {
            int cap = Level(id);
            foreach (var child in Family(klass))
            {
                var skill = KuData.Skill(child);
                if (skill == null || skill.Parent != id || Level(child) <= cap) continue;
                Set(child, cap);
                Trim(klass, child);
            }
        }

        private static IEnumerable<int> Family(KuClass klass)
        {
            yield return klass.Root;
            for (int i = 1; i < klass.Left.Length; i++) yield return klass.Left[i];
            for (int i = 1; i < klass.Right.Length; i++) yield return klass.Right[i];
        }

        private static void Click(int id, PointerEventData data)
        {
            bool right = data != null && data.button == PointerEventData.InputButton.Right;
            bool left = data == null || data.button == PointerEventData.InputButton.Left;
            if (right) Lower(id);
            else if (left) Raise(id);
            else if (!left) return;
            bool moved = id != _shown;
            _shown = id;
            Refresh();
            if (moved && _deskRows != null) _deskRows.anchoredPosition = Vector2.zero;
        }

        private static void Pick(int id)
        {
            if (id == _class) return;
            _class = id;
            _shown = 0;
            Grow();
            Refresh();
        }

        private static void Step(int by)
        {
            int next = Mathf.Clamp(Subs + by, 1, KuData.Gain.Length);
            if (next == Subs) return;
            Subs = next;
            if (Spent > Total)
            {
                Plan.Clear();
                Notice.Show("Калькулятор КУ: на этом подклассе очков меньше — дерево сброшено", 4f);
            }
            Refresh();
        }

        private static void Reset()
        {
            Plans.Clear();
            Refresh();
        }

        private static string Describe()
        {
            var klass = Klass;
            return (klass != null ? klass.Name : "?") + ", подклассов " + Subs + ", очков " + Total + ", осталось " + (Total - Spent);
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLKu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 831;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);
            go.AddComponent<KuTicker>();

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            _panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            var back = _panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            WardrobeLook.Frame(_panelGo, WardrobeLook.Edge);
            var panel = _panelGo.transform;

            var title = Say(panel, "Калькулятор КУ", 22, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            Wardrobe.At(title.rectTransform, 20f, 12f, 260f, 36f);
            OnlineWindow.MakeCloseButton(panel, Close);

            Tabs.Clear();
            float x = 16f;
            foreach (var klass in KuData.Classes)
            {
                int id = klass.Id;
                var tab = Wardrobe.GameButton(panel, klass.Name, () => Pick(id), false);
                Wardrobe.At(tab, x, 60f, TabW, 44f);
                Tabs.Add(tab);
                x += TabW + 8f;
            }

            var subLabel = Say(panel, "Подкласс", 15, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(subLabel.rectTransform, 18f, 116f, 96f, 40f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(panel, "‹", () => Step(-1)).transform, 112f, 118f, 36f, 36f);
            var field = Box(panel, "sub", 152f, 118f, 400f, 36f, WardrobeLook.Field, 8);
            _subText = Say(field, "", 15, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleCenter);
            OnlineWindow.Place(_subText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
            _subText.resizeTextForBestFit = true;
            _subText.resizeTextMinSize = 10;
            _subText.resizeTextMaxSize = 15;
            Wardrobe.At((RectTransform)Wardrobe.Arrow(panel, "›", () => Step(1)).transform, 556f, 118f, 36f, 36f);
            _points = Say(panel, "", 17, FontStyle.Normal, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            _points.supportRichText = true;
            Wardrobe.At(_points.rectTransform, 616f, 116f, 390f, 40f);
            Wardrobe.At(Wardrobe.GameButton(panel, "Сбросить", Reset, true), PanelW - 216f, 118f, 200f, 38f);

            _tree = Box(panel, "tree", 16f, BodyY, TreeW, BodyH, WardrobeLook.Card, 12);
            BuildDesk(Box(panel, "desk", 16f + TreeW + 12f, BodyY, DeskW, BodyH, WardrobeLook.Card, 12));

            var left = Say(panel, "ЛКМ — поднять ступень", 13, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(left.rectTransform, 20f, PanelH - 46f, PanelW - 40f, 20f);
            var right = Say(panel, "ПКМ — опустить ступень", 13, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(right.rectTransform, 20f, PanelH - 26f, PanelW - 40f, 20f);
        }

        private static void BuildDesk(RectTransform desk)
        {
            var frame = Box(desk, "icon", 16f, 16f, 68f, 68f, WardrobeLook.Field, 8);
            var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image));
            picGo.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)picGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            _deskIcon = picGo.GetComponent<Image>();
            _deskIcon.preserveAspect = true;
            _deskIcon.raycastTarget = false;
            _deskName = Say(desk, "", 19, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            Wardrobe.At(_deskName.rectTransform, 96f, 14f, DeskW - 110f, 28f);
            _deskName.resizeTextForBestFit = true;
            _deskName.resizeTextMinSize = 12;
            _deskName.resizeTextMaxSize = 19;
            _deskKind = Say(desk, "", 13, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(_deskKind.rectTransform, 96f, 42f, DeskW - 110f, 22f);
            _deskKind.resizeTextForBestFit = true;
            _deskKind.resizeTextMinSize = 10;
            _deskKind.resizeTextMaxSize = 13;
            _deskNow = Say(desk, "", 14, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(_deskNow.rectTransform, 96f, 64f, DeskW - 110f, 22f);

            var scrollGo = new GameObject("levels", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(desk, false);
            var catcher = scrollGo.GetComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            var srt = (RectTransform)scrollGo.transform;
            Wardrobe.At(srt, 12f, 100f, DeskW - 24f, BodyH - 100f - 12f);
            _deskScroll = scrollGo.GetComponent<ScrollRect>();
            _deskScroll.horizontal = false;
            _deskScroll.vertical = true;
            _deskScroll.scrollSensitivity = 30f;
            _deskScroll.movementType = ScrollRect.MovementType.Clamped;
            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            _deskRows = (RectTransform)contentGo.transform;
            _deskRows.anchorMin = new Vector2(0f, 1f);
            _deskRows.anchorMax = new Vector2(1f, 1f);
            _deskRows.pivot = new Vector2(0.5f, 1f);
            _deskRows.offsetMin = Vector2.zero;
            _deskRows.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _deskScroll.content = _deskRows;
            _deskScroll.viewport = srt;
        }

        private static void Grow()
        {
            if (_tree == null) return;
            for (int i = _tree.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_tree.GetChild(i).gameObject);
            Nodes.Clear();
            Links.Clear();
            var klass = Klass;
            if (klass == null) return;

            _leftTitle = Say(_tree, klass.LeftName, 15, FontStyle.Bold, WardrobeLook.Label, TextAnchor.UpperLeft);
            Wardrobe.At(_leftTitle.rectTransform, 16f, 16f, Col * 3f - 40f, 70f);
            _leftTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _rightTitle = Say(_tree, klass.RightName, 15, FontStyle.Bold, WardrobeLook.Label, TextAnchor.UpperRight);
            Wardrobe.At(_rightTitle.rectTransform, Col * 4f + 24f, 16f, Col * 3f - 40f, 70f);
            _rightTitle.horizontalOverflow = HorizontalWrapMode.Wrap;

            var spots = new Dictionary<int, Vector2>();
            spots[klass.Root] = Spot(3, 0);
            for (int i = 1; i < 8; i++)
            {
                spots[klass.Left[i]] = Spot(Cols[i], Tiers[i]);
                spots[klass.Right[i]] = Spot(4 + Cols[i], Tiers[i]);
            }
            foreach (var pair in spots)
            {
                var skill = KuData.Skill(pair.Key);
                Vector2 from;
                if (skill != null && skill.Parent != 0 && spots.TryGetValue(skill.Parent, out from)) AddLink(skill.Parent, pair.Key, from, pair.Value);
            }
            foreach (var pair in spots) AddNode(pair.Key, pair.Value);
        }

        private static Vector2 Spot(int col, int tier) => new Vector2(Col * (col + 0.5f), Rows[tier] + 8f + Icon / 2f);

        private static void AddLink(int parent, int child, Vector2 from, Vector2 to)
        {
            var go = new GameObject("link", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_tree, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            var a = new Vector2(from.x, -from.y);
            var d = new Vector2(to.x, -to.y) - a;
            rt.anchoredPosition = a;
            rt.sizeDelta = new Vector2(d.magnitude, 3f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var line = go.GetComponent<Image>();
            line.raycastTarget = false;
            Links.Add(new Link { Parent = parent, Child = child, Line = line });
        }

        private static void AddNode(int id, Vector2 center)
        {
            var skill = KuData.Skill(id);
            var go = new GameObject("skill" + id, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(EventTrigger));
            go.transform.SetParent(_tree, false);
            var box = (RectTransform)go.transform;
            Wardrobe.At(box, center.x - NodeW / 2f, center.y - Icon / 2f - 8f, NodeW, NodeH);
            var node = new Node { Id = id, Back = go.GetComponent<Image>(), Edge = go.GetComponent<Outline>() };
            node.Back.sprite = OnlineWindow.Rounded(8);
            node.Back.type = Image.Type.Sliced;

            var frame = Box(box, "icon", (NodeW - Icon) / 2f, 8f, Icon, Icon, WardrobeLook.Field, 6);
            var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image));
            picGo.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)picGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(1f, 1f), new Vector2(-1f, -1f));
            node.Pic = picGo.GetComponent<Image>();
            node.Pic.preserveAspect = true;
            node.Pic.raycastTarget = false;

            var badge = Box(box, "badge", (NodeW + Icon) / 2f - 20f, 8f + Icon - 18f, 26f, 22f, new Color(0.05f, 0.06f, 0.07f, 0.92f), 8);
            node.BadgeGo = badge.gameObject;
            node.Badge = Say(badge, "", 14, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleCenter);
            OnlineWindow.Place(node.Badge.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            float pipW = 14f;
            float start = (NodeW - (KuData.Top * pipW + (KuData.Top - 1) * 4f)) / 2f;
            for (int i = 0; i < KuData.Top; i++)
            {
                var pipGo = new GameObject("pip", typeof(RectTransform), typeof(Image));
                pipGo.transform.SetParent(box, false);
                Wardrobe.At((RectTransform)pipGo.transform, start + i * (pipW + 4f), 8f + Icon + 7f, pipW, 5f);
                node.Pips[i] = pipGo.GetComponent<Image>();
                node.Pips[i].raycastTarget = false;
            }

            node.Name = Say(box, skill != null ? skill.Name : "?", 12, FontStyle.Normal, WardrobeLook.Label, TextAnchor.UpperCenter);
            Wardrobe.At(node.Name.rectTransform, 4f, 8f + Icon + 16f, NodeW - 8f, NodeH - Icon - 26f);
            node.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
            node.Name.verticalOverflow = VerticalWrapMode.Truncate;
            node.Name.resizeTextForBestFit = true;
            node.Name.resizeTextMinSize = 9;
            node.Name.resizeTextMaxSize = 12;

            var trigger = go.GetComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data => Click(id, data as PointerEventData));
            trigger.triggers.Add(click);
            Nodes.Add(node);
        }

        private static void Refresh()
        {
            var klass = Klass;
            if (_canvasGo == null || klass == null) return;
            PaintTabs();
            var sub = klass.Sub(Subs);
            if (_subText != null)
                _subText.text = Subs + " · " + (sub != null ? sub.Name + " · с " + sub.Level + " ур." : "подкласс");
            if (_points != null)
                _points.text = "Очков: <color=#e2b85c><b>" + Math.Max(0, Total - Spent) + "</b></color>";
            int branch = Branch(klass);
            if (_leftTitle != null) _leftTitle.color = branch == 1 ? WardrobeLook.Accent : branch == 2 ? WardrobeLook.Faint : WardrobeLook.Label;
            if (_rightTitle != null) _rightTitle.color = branch == 2 ? WardrobeLook.Accent : branch == 1 ? WardrobeLook.Faint : WardrobeLook.Label;
            foreach (var link in Links)
                link.Line.color = Level(link.Parent) > 0 && Level(link.Child) > 0
                    ? new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.8f)
                    : WardrobeLook.Edge;
            foreach (var node in Nodes) Paint(klass, node);
            Icons();
            Show(_shown != 0 ? _shown : klass.Root);
        }

        private static void PaintTabs()
        {
            var classes = KuData.Classes;
            for (int i = 0; i < Tabs.Count && i < classes.Count; i++)
            {
                bool on = classes[i].Id == _class;
                var image = Tabs[i].GetComponent<Image>();
                if (image != null) image.color = on ? WardrobeLook.Accent : WardrobeLook.Button;
                var label = Tabs[i].GetComponentInChildren<Text>();
                if (label != null) label.color = on ? WardrobeLook.OnAccent : WardrobeLook.Bright;
            }
        }

        private static void Paint(KuClass klass, Node node)
        {
            int level = Level(node.Id);
            bool open = CanRaise(klass, node.Id);
            bool dim = level == 0 && !open;
            bool shown = node.Id == _shown;
            node.Back.color = level > 0 ? WardrobeLook.Stage : dim ? WardrobeLook.Field : WardrobeLook.Tab;
            node.Edge.effectColor = shown ? WardrobeLook.Bright
                : level > 0 ? WardrobeLook.Accent
                : open ? WardrobeLook.FieldEdge
                : new Color(1f, 1f, 1f, 0.05f);
            node.Edge.effectDistance = shown ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            node.Pic.color = dim ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;
            node.Name.color = level > 0 ? WardrobeLook.Bright : dim ? WardrobeLook.Faint : WardrobeLook.Label;
            node.BadgeGo.SetActive(level > 0);
            node.Badge.text = level.ToString();
            for (int i = 0; i < node.Pips.Length; i++)
                node.Pips[i].color = i < level ? WardrobeLook.Accent : new Color(1f, 1f, 1f, dim ? 0.06f : 0.14f);
        }

        private static void Icons()
        {
            foreach (var node in Nodes)
            {
                if (node.Pic == null || !Quickslots.Faded(node.Pic.sprite)) continue;
                var sprite = IconOf(node.Id);
                node.Pic.sprite = sprite;
                node.Pic.enabled = sprite != null;
            }
            if (_deskIcon != null && _shown != 0 && Quickslots.Faded(_deskIcon.sprite))
            {
                var sprite = IconOf(_shown);
                _deskIcon.sprite = sprite;
                _deskIcon.enabled = sprite != null;
            }
        }

        private static Sprite IconOf(int id)
        {
            try
            {
                var sprite = AtlasUtils.GetSquareSkillIcon(id);
                return Quickslots.Faded(sprite) ? null : sprite;
            }
            catch { return null; }
        }

        private static void Show(int id)
        {
            var klass = Klass;
            var skill = KuData.Skill(id);
            if (klass == null || skill == null || _deskRows == null) return;
            bool moved = id != _shown;
            _shown = id;
            foreach (var node in Nodes) Paint(klass, node);
            var sprite = IconOf(id);
            _deskIcon.sprite = sprite;
            _deskIcon.enabled = sprite != null;
            _deskName.text = skill.Name;
            int side = klass.Side(id);
            _deskKind.text = skill.Kind + " · " + (side == 0 ? "корень обеих веток" : "ветка «" + (side == 1 ? klass.LeftName : klass.RightName) + "»");
            int level = Level(id);
            _deskNow.text = level > 0 ? "Изучено: " + KuData.Step(level) + " (" + level + " из " + KuData.Top + ")" : "Не изучено";
            _deskNow.color = level > 0 ? WardrobeLook.Accent : WardrobeLook.Faint;

            for (int i = _deskRows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_deskRows.GetChild(i).gameObject);
            for (int i = 0; i < KuData.Top; i++) AddStep(skill, i + 1, level);
            if (moved && _deskScroll != null) _deskScroll.verticalNormalizedPosition = 1f;
        }

        private static void AddStep(KuSkill skill, int step, int level)
        {
            bool have = step <= level;
            bool next = step == level + 1;
            var go = new GameObject("step" + step, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(_deskRows, false);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = have ? WardrobeLook.Mix(WardrobeLook.Card, WardrobeLook.Accent, 0.14f) : next ? WardrobeLook.Tab : new Color(0f, 0f, 0f, 0f);
            back.raycastTarget = false;
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 8, 9);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var head = new StringBuilder();
            head.Append("<b>").Append(step).Append(". ").Append(KuData.Step(step)).Append("</b>");
            head.Append("   <size=12><color=#747b85>всего ").Append(KuData.Cost(step)).Append(' ').Append(Wardrobe.Plural(KuData.Cost(step), "очко", "очка", "очков")).Append("</color></size>");
            var cost = skill.Costs[step - 1];
            if (cost != null && cost.Length >= 3 && (cost[0] > 0 || cost[1] > 0 || cost[2] > 0))
            {
                var parts = new List<string>();
                if (cost[0] > 0) parts.Add("энергия " + cost[0]);
                if (cost[1] > 0) parts.Add("действует " + cost[1]);
                if (cost[2] > 0) parts.Add("перезарядка " + cost[2]);
                head.Append("\n<size=12><color=#acb3bd>").Append(string.Join(" · ", parts.ToArray())).Append("</color></size>");
            }
            var title = Say(go.transform, head.ToString(), 15, FontStyle.Normal, have ? WardrobeLook.Accent : next ? WardrobeLook.Bright : WardrobeLook.Label, TextAnchor.UpperLeft);
            title.supportRichText = true;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            var body = Say(go.transform, skill.Texts[step - 1] ?? "", 14, FontStyle.Normal, have || next ? WardrobeLook.Body : WardrobeLook.Faint, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static Text Say(Transform host, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var label = OnlineWindow.Label(host, text, size, style, color);
            label.alignment = anchor;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Box(Transform host, string name, float x, float y, float w, float h, Color color, int radius)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var image = go.GetComponent<Image>();
            image.sprite = OnlineWindow.Rounded(radius);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            Wardrobe.At(rt, x, y, w, h);
            return rt;
        }
    }

    internal sealed class KuTicker : MonoBehaviour
    {
        private void Update()
        {
            try { KuCalc.Tick(); }
            catch (Exception e) { Plugin.Warn("[калькулятор ку] такт: " + e.Message); }
        }
    }
}
