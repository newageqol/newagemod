using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class OnlineCharm
    {
        internal int Id;
        internal string Picture = "";
        internal string Title = "";
        internal DateTime At;
        internal readonly List<CharmPart> Parts = new List<CharmPart>();
    }

    internal sealed class CharmPart
    {
        internal long Span = -1;
        internal List<CharmAct> Acts = new List<CharmAct>();
    }

    internal sealed class CharmAct
    {
        internal string Text = "";
        internal long Value;
        internal bool Counted;
        internal bool Percent;
    }

    internal static class OnlineCharms
    {
        private const int Premium = 8;
        private const float Side = 24f;
        private const float TipWide = 340f;
        private const string Site = "https://files.nura.biz/assets/enchantments/";
        private const float Again = 600f;

        private sealed class Badge
        {
            internal Image Image;
            internal int Id;
            internal string Picture;
            internal OnlineCharm Charm;
            internal Text Count;
        }

        private static readonly List<Badge> Live = new List<Badge>();
        private static readonly Dictionary<string, Sprite> Got = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Loading = new HashSet<string>();
        private static readonly Dictionary<string, float> Failed = new Dictionary<string, float>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly FieldInfo CrownField = AccessTools.Field(typeof(UserRowWidget), "PremiumImage");

        private static RectTransform _panel;
        private static CharmHover _hover;
        private static GameObject _tipGo;
        private static Text _tipText;
        private static LayoutElement _tipSize;
        private static float _paintAt;

        internal static void Carry(List<OnlinePlayer> old, List<OnlinePlayer> fresh)
        {
            if (old == null || fresh == null) return;
            var known = new Dictionary<int, List<OnlineCharm>>();
            foreach (var p in old)
                if (p != null && p.Charms != null) known[p.Id] = p.Charms;
            foreach (var p in fresh)
            {
                List<OnlineCharm> had;
                if (p != null && known.TryGetValue(p.Id, out had)) p.Charms = had;
            }
        }

        internal static void Apply(List<string> records, List<OnlinePlayer> list, long now)
        {
            try
            {
                var byId = new Dictionary<int, OnlinePlayer>();
                foreach (var p in list)
                    if (p != null && p.Id != 0) byId[p.Id] = p;
                var lists = new List<string>();
                foreach (var r in records)
                    if (r.StartsWith("379\u001f", StringComparison.Ordinal)) lists.Add(r.Substring(4));
                var found = new Dictionary<int, List<OnlineCharm>>();
                Lists(lists, found);
                bool changed = false;
                foreach (int key in new List<int>(found.Keys))
                {
                    OnlinePlayer owner;
                    if (byId.TryGetValue(key, out owner) && Same(owner.Charms, found[key])) found[key] = owner.Charms;
                    else changed = true;
                }

                int heard = 0;
                foreach (var r in records)
                {
                    if (!r.StartsWith("377\u001f", StringComparison.Ordinal)) continue;
                    var bits = r.Split(new[] { '\u001f' }, 4);
                    int id, u;
                    long at;
                    if (bits.Length < 4 || !int.TryParse(bits[1], out id) || !long.TryParse(bits[2], out at)) continue;
                    var head = Regex.Match(bits[3], "<g\\b([^>]*?)/?>");
                    List<OnlineCharm> charms;
                    if (!head.Success || !int.TryParse(Attr(head.Groups[1].Value, "u"), out u) || !found.TryGetValue(u, out charms)) continue;
                    var charm = charms.Find(c => c.Id == id);
                    if (charm == null) continue;
                    Read(bits[3], head.Groups[1].Value, charm);
                    charm.At = DateTime.UtcNow.AddSeconds(-Math.Max(0L, now - at));
                    heard++;
                }

                foreach (var pair in found)
                {
                    OnlinePlayer p;
                    if (byId.TryGetValue(pair.Key, out p)) p.Charms = pair.Value;
                }
                if (changed) Version++;
                Plugin.Trace("[online] player states: lists " + found.Count + " of " + byId.Count + ", durations " + heard);
            }
            catch (Exception e) { Plugin.Trace("[online] player states: " + e.Message); }
        }

        internal static int Version;
        internal static bool Gathering;

        private static bool Same(List<OnlineCharm> had, List<OnlineCharm> got)
        {
            if (had == null || got == null || had.Count != got.Count) return false;
            for (int i = 0; i < had.Count; i++)
                if (had[i] == null || got[i] == null || had[i].Id != got[i].Id) return false;
            return true;
        }

        internal static string Shape(OnlinePlayer p)
        {
            var charms = p != null ? p.Charms : null;
            if (charms == null) return "";
            var sb = new StringBuilder();
            foreach (var c in charms) if (c != null) sb.Append(c.Id).Append(',');
            return sb.ToString();
        }

        internal static void Redress(UserRowWidget widget, OnlinePlayer p)
        {
            try
            {
                var crown = CrownField?.GetValue(widget) as Image;
                if (crown == null) return;
                var host = crown.transform.parent;
                for (int i = host.childCount - 1; i >= 0; i--)
                {
                    var child = host.GetChild(i);
                    if (child.name != "QoLCharm") continue;
                    child.SetParent(null, false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                Badges(crown, p);
            }
            catch (Exception e) { Plugin.Trace("[online] state icons again " + p?.Login + ": " + e.Message); }
        }

        private static void Lists(List<string> inbox, Dictionary<int, List<OnlineCharm>> found)
        {
            foreach (var m in inbox)
            {
                if (m.IndexOf("type=\"379\"", StringComparison.Ordinal) < 0) continue;
                var who = Regex.Match(m, "<enchlist\\b[^>]*\\bu=\"(-?\\d+)\"");
                int u;
                if (!who.Success || !int.TryParse(who.Groups[1].Value, out u)) continue;
                var charms = new List<OnlineCharm>();
                var seen = new HashSet<int>();
                foreach (Match e in Regex.Matches(m, "<e\\b([^>]*)/>"))
                {
                    int id;
                    if (!int.TryParse(Attr(e.Groups[1].Value, "id"), out id) || !seen.Add(id)) continue;
                    charms.Add(new OnlineCharm { Id = id, Picture = Attr(e.Groups[1].Value, "img") });
                }
                found[u] = charms;
            }
            inbox.Clear();
        }

        private static void Read(string xml, string head, OnlineCharm charm)
        {
            charm.At = DateTime.UtcNow;
            charm.Parts.Clear();
            charm.Title = Clean(Attr(head, "h"));
            foreach (Match hi in Regex.Matches(xml, "<hi\\b([^>]*)>(.*?)</hi>", RegexOptions.Singleline))
            {
                if (charm.Title.Length == 0) charm.Title = Clean(Attr(hi.Groups[1].Value, "name"));
                string inner = hi.Groups[2].Value;
                var acts = new List<CharmAct>();
                foreach (Match a in Regex.Matches(inner, "<a\\b([^>]*)/>"))
                {
                    string text = Clean(Attr(a.Groups[1].Value, "s"));
                    if (text.Length == 0) continue;
                    long v;
                    bool counted = long.TryParse(Attr(a.Groups[1].Value, "v"), out v);
                    acts.Add(new CharmAct { Text = text, Value = counted ? v : 0, Counted = counted, Percent = Attr(a.Groups[1].Value, "p") == "1" });
                }
                var times = Regex.Matches(inner, "<t\\b[^>]*\\bv=\"(-?\\d+)\"");
                if (times.Count == 0)
                {
                    charm.Parts.Add(new CharmPart { Acts = acts });
                    continue;
                }
                foreach (Match t in times)
                {
                    long span;
                    charm.Parts.Add(new CharmPart { Span = long.TryParse(t.Groups[1].Value, out span) ? Math.Max(0L, span) : -1, Acts = acts });
                }
            }
        }

        private static string Attr(string attrs, string key)
        {
            var m = Regex.Match(attrs ?? "", "\\b" + key + "=\"([^\"]*)\"");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }

        private static string Clean(string s) => (s ?? "").Replace('<', '‹').Replace('>', '›').Trim();

        internal static void Decorate(UserRowWidget widget, OnlinePlayer p, RectTransform panel)
        {
            try
            {
                _panel = panel;
                var crown = CrownField?.GetValue(widget) as Image;
                if (crown == null || p == null) return;
                crown.raycastTarget = true;
                Hover(crown.gameObject, p, Premium);
                Badges(crown, p);
            }
            catch (Exception e) { Plugin.Trace("[online] state icons " + p?.Login + ": " + e.Message); }
        }

        private static void Badges(Image crown, OnlinePlayer p)
        {
            var charms = p != null ? p.Charms : null;
            if (charms == null || charms.Count == 0) return;
            var host = crown.transform.parent;
            int at = crown.transform.GetSiblingIndex() + 1;
            var size = Size(crown);
            foreach (var charm in charms)
            {
                if (charm == null || charm.Id == Premium) continue;
                var go = new GameObject("QoLCharm", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(host, false);
                go.transform.SetSiblingIndex(at++);
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = size;
                var le = go.GetComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = size.x;
                le.minHeight = le.preferredHeight = size.y;
                le.flexibleWidth = 0f;
                var image = go.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = true;
                var sprite = Picture(charm.Id, charm.Picture);
                image.sprite = sprite;
                image.enabled = sprite != null;
                var count = Count(go.transform);
                Mark(count, Living(charm));
                Hover(go, p, charm.Id);
                Live.Add(new Badge { Image = image, Id = charm.Id, Picture = charm.Picture, Charm = charm, Count = count });
            }
        }

        private static void Mark(Text label, int copies)
        {
            if (label == null) return;
            bool show = copies > 1;
            if (show)
            {
                string text = Short(copies);
                if (label.text != text) label.text = text;
            }
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        }

        private static Text Count(Transform host)
        {
            var label = OnlineWindow.Label(host, "", 10, FontStyle.Bold, Color.white);
            label.raycastTarget = false;
            label.alignment = TextAnchor.LowerRight;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var edge = label.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.95f);
            edge.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(1f, 0f), new Vector2(-4f, -3f), new Vector2(3f, 0f));
            return label;
        }

        private static string Short(long n)
        {
            if (n < 1000) return n.ToString();
            long tenths = n / 100;
            if (n < 10000 && tenths % 10 != 0) return tenths / 10 + "," + tenths % 10 + "к";
            return n / 1000 + "к";
        }

        private static int Living(OnlineCharm charm)
        {
            if (charm == null) return 0;
            long gone = (long)(DateTime.UtcNow - charm.At).TotalSeconds;
            int n = 0;
            foreach (var part in charm.Parts)
                if (part.Span < 0 || part.Span - gone > 0) n++;
            return n;
        }

        private static List<CharmPart> Alive(OnlineCharm charm)
        {
            var alive = new List<CharmPart>();
            if (charm == null) return alive;
            long gone = (long)(DateTime.UtcNow - charm.At).TotalSeconds;
            foreach (var part in charm.Parts)
                if (part.Span < 0 || part.Span - gone > 0) alive.Add(part);
            return alive;
        }

        private static void Hover(GameObject go, OnlinePlayer p, int id)
        {
            var hover = go.GetComponent<CharmHover>() ?? go.AddComponent<CharmHover>();
            hover.Player = p;
            hover.Id = id;
        }

        private static Vector2 Size(Image crown)
        {
            var le = crown.GetComponent<LayoutElement>();
            if (le != null && le.preferredWidth > 4f)
                return new Vector2(le.preferredWidth, le.preferredHeight > 4f ? le.preferredHeight : le.preferredWidth);
            var r = crown.rectTransform.rect;
            if (r.width > 4f && r.height > 4f) return new Vector2(r.width, r.height);
            return new Vector2(Side, Side);
        }

        private static Sprite Picture(int id, string picture)
        {
            try
            {
                var names = IconNamesDatabase.Instance;
                if (names != null && names.GetData("ench" + id) != null)
                {
                    var s = AtlasUtils.GetStateSprite(EStateType.GlobalEnchantment, id);
                    if (!Quickslots.Faded(s)) return s;
                }
            }
            catch { }
            return Flash(picture);
        }

        private static Sprite Flash(string picture)
        {
            if (string.IsNullOrEmpty(picture) || !Regex.IsMatch(picture, "^[A-Za-z0-9_]+$")) return null;
            Sprite have;
            if (Got.TryGetValue(picture, out have) && have != null && have.texture != null) return have;
            float at;
            if (Failed.TryGetValue(picture, out at) && Time.unscaledTime - at < Again) return null;
            if (Loading.Contains(picture) || Plugin.Instance == null) return null;
            Loading.Add(picture);
            Plugin.Instance.StartCoroutine(Load(picture));
            return null;
        }

        private static IEnumerator Load(string picture)
        {
            var req = UnityWebRequest.Get(Site + picture + "s.jpg");
            req.timeout = 15;
            yield return req.SendWebRequest();
            byte[] data = req.responseCode == 200 && req.downloadHandler != null ? req.downloadHandler.data : null;
            string error = req.error;
            req.Dispose();
            Loading.Remove(picture);
            Sprite sprite = null;
            try
            {
                if (data != null)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(texture, data))
                    {
                        texture.name = "charm_" + picture;
                        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = picture;
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[online] state picture " + picture + ": " + e.Message); }
            if (sprite == null)
            {
                Failed[picture] = Time.unscaledTime;
                Plugin.Trace("[online] no state picture " + picture + ": " + (error ?? "failed to parse"));
                yield break;
            }
            Got[picture] = sprite;
            Retry();
        }

        internal static void Retry()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var badge = Live[i];
                if (badge.Image == null) { Live.RemoveAt(i); continue; }
                Mark(badge.Count, Living(badge.Charm));
                if (!Quickslots.Faded(badge.Image.sprite)) continue;
                var sprite = Picture(badge.Id, badge.Picture);
                if (sprite == null) continue;
                badge.Image.sprite = sprite;
                badge.Image.enabled = true;
            }
        }

        internal static void Clear()
        {
            Live.Clear();
            _hover = null;
            Hide();
        }

        internal static void Forget()
        {
            Clear();
            _panel = null;
            _tipGo = null;
            _tipText = null;
            _tipSize = null;
        }

        internal static void Enter(CharmHover hover)
        {
            _hover = hover;
            _paintAt = 0f;
            Tick();
        }

        internal static void Leave(CharmHover hover)
        {
            if (_hover != hover) return;
            _hover = null;
            Hide();
        }

        private static void Hide()
        {
            if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
        }

        internal static void Tick()
        {
            try
            {
                if (_hover == null || !_hover.isActiveAndEnabled || _panel == null) { _hover = null; Hide(); return; }
                if (Time.unscaledTime < _paintAt) return;
                _paintAt = Time.unscaledTime + 0.5f;
                Paint();
            }
            catch (Exception e) { Plugin.Trace("[online] state hint: " + e.Message); _hover = null; Hide(); }
        }

        private static void Paint()
        {
            if (_tipGo == null) BuildTip();
            string text = Describe(_hover.Player, _hover.Id);
            if (_tipText.text != text)
            {
                _tipText.text = text;
                _tipSize.preferredWidth = Mathf.Min(TipWide, _tipText.preferredWidth + 1f);
            }
            if (!_tipGo.activeSelf) _tipGo.SetActive(true);
            _tipGo.transform.SetAsLastSibling();
            var tip = (RectTransform)_tipGo.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip);

            var corners = new Vector3[4];
            ((RectTransform)_hover.transform).GetWorldCorners(corners);
            Vector2 low = _panel.InverseTransformPoint(corners[0]);
            Vector2 top = _panel.InverseTransformPoint(corners[1]);
            float w = tip.rect.width, h = tip.rect.height;
            var place = new Vector2(low.x, low.y - 6f);

            var box = _panel.rect;
            Vector2 screenMin = box.min, screenMax = box.max;
            var canvas = _panel.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            if (canvas != null)
            {
                ((RectTransform)canvas.transform).GetWorldCorners(corners);
                screenMin = _panel.InverseTransformPoint(corners[0]);
                screenMax = _panel.InverseTransformPoint(corners[2]);
            }
            float floor = Mathf.Max(box.yMin, screenMin.y) + 8f, roof = Mathf.Min(box.yMax, screenMax.y) - 8f;
            float left = Mathf.Max(box.xMin, screenMin.x) + 8f, right = Mathf.Min(box.xMax, screenMax.x) - 8f;
            if (h > roof - floor) { floor = screenMin.y + 8f; roof = screenMax.y - 8f; }
            if (w > right - left) { left = screenMin.x + 8f; right = screenMax.x - 8f; }
            if (place.y - h < floor)
                place.y = top.y + 6f + h <= roof ? top.y + 6f + h : Mathf.Min(roof, floor + h);
            place.x = Mathf.Clamp(place.x, left, Mathf.Max(left, right - w));
            if (tip.anchoredPosition != place) tip.anchoredPosition = place;
        }

        private static string Describe(OnlinePlayer p, int id)
        {
            OnlineCharm charm = null;
            var charms = p != null ? p.Charms : null;
            if (charms != null)
                foreach (var one in charms)
                    if (one != null && one.Id == id) { charm = one; break; }

            var sb = new StringBuilder();
            string title = charm != null && charm.Title.Length > 0 ? charm.Title : Name(id);
            sb.Append("<size=13><b><color=#eceef1>").Append(title).Append("</color></b></size>");
            if (charm == null || charm.Parts.Count == 0)
            {
                sb.Append("\n<color=#acb3bd>").Append(charms == null ? "срок придёт с обновлением списка" : "срок сервер не сообщил").Append("</color>");
                return sb.ToString();
            }
            var alive = Alive(charm);
            if (alive.Count == 0)
            {
                sb.Append("\n<color=#acb3bd>срок вышел</color>");
                return sb.ToString();
            }
            long gone = (long)(DateTime.UtcNow - charm.At).TotalSeconds;
            if (alive.Count == 1) sb.Append('\n').Append(Term(alive[0].Span, gone));
            else
            {
                long first = long.MaxValue, last = 0;
                bool endless = false;
                foreach (var part in alive)
                {
                    if (part.Span < 0) { endless = true; continue; }
                    long left = part.Span - gone;
                    if (left < first) first = left;
                    if (left > last) last = left;
                }
                sb.Append("\n<color=#eceef1>копий: ").Append(alive.Count).Append(", всё сложено</color>");
                if (first != long.MaxValue)
                {
                    sb.Append("\n<color=#8fd8ff>ближайшая закончится через ").Append(Left(first)).Append("</color>");
                    if (last > first && !endless) sb.Append("\n<color=#8fd8ff>последняя — через ").Append(Left(last)).Append("</color>");
                }
                if (endless) sb.Append("\n<color=#acb3bd>").Append(first != long.MaxValue ? "часть без срока" : "без срока").Append("</color>");
            }
            foreach (var line in Sum(alive)) sb.Append("\n<color=#d6dae0>· ").Append(line).Append("</color>");
            return sb.ToString();
        }

        private sealed class Tally
        {
            internal string Text;
            internal long Value;
            internal bool Counted;
            internal bool Percent;
        }

        private static List<string> Sum(List<CharmPart> alive)
        {
            var order = new List<Tally>();
            var byKey = new Dictionary<string, Tally>();
            foreach (var part in alive)
                foreach (var act in part.Acts)
                {
                    string key = act.Text + (act.Counted ? (act.Percent ? "\u0001%" : "\u0001") : "");
                    Tally tally;
                    if (!byKey.TryGetValue(key, out tally))
                    {
                        tally = new Tally { Text = act.Text, Counted = act.Counted, Percent = act.Percent };
                        byKey[key] = tally;
                        order.Add(tally);
                    }
                    tally.Value += act.Value;
                }
            var lines = new List<string>();
            foreach (var tally in order)
            {
                if (!tally.Counted || tally.Value == 0) { lines.Add(tally.Text); continue; }
                lines.Add(tally.Text + " <color=" + (tally.Value > 0 ? "#7ed68a" : "#f07a6e") + ">" + (tally.Value > 0 ? "+" : "")
                          + tally.Value + (tally.Percent ? "%" : "") + "</color>");
            }
            return lines;
        }

        private static string Term(long span, long gone)
        {
            if (span < 0) return "<color=#acb3bd>без срока</color>";
            long left = span - gone;
            if (left <= 0) return "<color=#acb3bd>срок вышел</color>";
            return "<color=#8fd8ff>осталось " + Left(left) + "</color>";
        }

        private static string Left(long sec)
        {
            long d = sec / 86400;
            long h = sec % 86400 / 3600;
            long m = sec % 3600 / 60;
            if (d > 0) return d + " д " + h + " ч";
            if (h > 0) return h + " ч " + m + " мин";
            if (m > 0) return m + " мин";
            return "меньше минуты";
        }

        private static string Name(int id)
        {
            string name;
            if (Names.TryGetValue(id, out name)) return name;
            name = null;
            try { name = ResourceStrings.GetGlobalEnchantmentGroupName(id); }
            catch { }
            if (string.IsNullOrEmpty(name) || name == "enchantment.group." + id) name = "состояние " + id;
            Names[id] = name;
            return name;
        }

        private static void BuildTip()
        {
            _tipGo = new GameObject("QoLCharmTip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _tipGo.transform.SetParent(_panel, false);
            var rt = (RectTransform)_tipGo.transform;
            rt.anchorMin = rt.anchorMax = _panel.pivot;
            rt.pivot = new Vector2(0f, 1f);
            var back = _tipGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var group = _tipGo.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(10, 10, 6, 7);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _tipText = OnlineWindow.Label(_tipGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Body);
            _tipText.alignment = TextAnchor.UpperLeft;
            _tipText.raycastTarget = false;
            _tipText.supportRichText = true;
            _tipText.lineSpacing = 1.05f;
            _tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            _tipSize = _tipText.gameObject.AddComponent<LayoutElement>();
            _tipGo.SetActive(false);
        }
    }

    internal sealed class CharmHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal OnlinePlayer Player;
        internal int Id;

        public void OnPointerEnter(PointerEventData e) => OnlineCharms.Enter(this);

        public void OnPointerExit(PointerEventData e) => OnlineCharms.Leave(this);

        private void OnDisable() => OnlineCharms.Leave(this);
    }
}
