using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class PlayerCard
    {
        private const string Site = "https://nura.biz/community/profile/";
        private const string RootName = "QoLPlayerCard";
        private const float Pad = 16f;
        private const float Height = 19.7f;
        private const float Wider = 140f;

        private sealed class Profile
        {
            internal readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
            internal bool Done;
            internal bool Failed;
        }

        private sealed class Waiting
        {
            internal int UserId;
            internal GameObject Root;
            internal readonly List<KeyValuePair<Text, Func<Profile, string>>> Cells = new List<KeyValuePair<Text, Func<Profile, string>>>();
        }

        private static readonly Dictionary<int, Profile> Profiles = new Dictionary<int, Profile>();
        private static readonly List<Waiting> Live = new List<Waiting>();
        private static readonly Dictionary<int, float> Base = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> Grown = new Dictionary<int, float>();
        private static readonly Dictionary<string, float> Plain = new Dictionary<string, float>();
        private static readonly Regex Item = new Regex("<li><span>([^<:]+):</span>(.*?)</li>", RegexOptions.Singleline);
        private static readonly Regex Tag = new Regex("<[^>]+>");
        private static readonly Regex Space = new Regex("\\s+");
        private static readonly string[][] Months =
        {
            new[] { "січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня" },
            new[] { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" },
            new[] { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" }
        };

        internal static void Show(GeneralUserInfoPanelContent panel, Unity3DUserInfoResponseMessage m)
        {
            if (panel == null || m == null || Plugin.Instance == null) return;
            var rt = panel.transform as RectTransform;
            if (rt == null) return;
            var hidden = new StringBuilder();
            for (int i = rt.childCount - 1; i >= 0; i--)
            {
                var child = rt.GetChild(i);
                if (child == null) continue;
                if (child.name == RootName) { UnityEngine.Object.DestroyImmediate(child.gameObject); continue; }
                child.gameObject.SetActive(false);
                hidden.Append(hidden.Length > 0 ? ", " : "").Append(child.name);
            }
            Plugin.Trace("[карточка] " + (m.Login ?? m.UserId.ToString()) + ": игровая вкладка " + Mathf.RoundToInt(rt.rect.width) + "x"
                         + Mathf.RoundToInt(rt.rect.height) + ", спрятано: " + hidden);
            Widen(rt);
            Plugin.Instance.StartCoroutine(Lay(panel, m));
        }

        private static void Widen(RectTransform panel)
        {
            try
            {
                var chain = new List<RectTransform>();
                var rt = panel.parent as RectTransform;
                for (int step = 0; step < 6 && rt != null; step++)
                {
                    if (rt.GetComponent<Canvas>() != null) break;
                    var holder = rt.parent != null ? rt.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
                    bool driven = holder != null && holder.childControlWidth;
                    if (!driven && Mathf.Abs(rt.anchorMin.x - rt.anchorMax.x) < 0.001f && rt.sizeDelta.x > 1f) chain.Add(rt);
                    rt = rt.parent as RectTransform;
                }
                for (int i = 0; i < chain.Count; i++)
                {
                    var one = chain[i];
                    float plain = Once(one.name + "#w", one.sizeDelta.x);
                    float wide = plain + Wider;
                    if (Mathf.Abs(one.sizeDelta.x - wide) < 0.5f) continue;
                    float pull = i + 1 < chain.Count ? one.pivot.x - one.anchorMin.x : one.pivot.x - 1f;
                    float at = Once(one.name + "#x", one.anchoredPosition.x - (one.sizeDelta.x - plain) * pull);
                    one.sizeDelta = new Vector2(wide, one.sizeDelta.y);
                    one.anchoredPosition = new Vector2(at + Wider * pull, one.anchoredPosition.y);
                }
                LayoutRebuilder.MarkLayoutForRebuild(panel);
            }
            catch (Exception e) { Plugin.Trace("[карточка] ширина окна: " + e.Message); }
        }

        private static IEnumerator Lay(GeneralUserInfoPanelContent panel, Unity3DUserInfoResponseMessage m)
        {
            yield return null;
            if (panel == null) yield break;
            var rt = (RectTransform)panel.transform;
            float width = rt.rect.width > 200f ? rt.rect.width : 760f;
            float height = rt.rect.height > 100f ? rt.rect.height : 480f;
            float need = Pad * 2f + Height * 24f;
            height = Fit(rt, height, need);
            try { Build(panel, m, width, height); }
            catch (Exception e) { Plugin.Warn("[карточка] не построилась: " + e.Message); }
        }

        private static float Fit(RectTransform panel, float height, float need)
        {
            RectTransform top = null;
            var rt = panel.parent as RectTransform;
            for (int step = 0; step < 6 && rt != null; step++)
            {
                if (Mathf.Abs(rt.anchorMin.y - rt.anchorMax.y) < 0.001f && rt.sizeDelta.y > 1f) { top = rt; break; }
                if (rt.GetComponent<Canvas>() != null) break;
                rt = rt.parent as RectTransform;
            }
            if (top == null) return height;
            int key = top.GetInstanceID();
            float before;
            Grown.TryGetValue(key, out before);
            float plain = height - before;
            float extra = Mathf.Max(0f, need - plain);
            if (Mathf.Abs(extra - before) < 1f || extra > 1200f) return height;
            rt = panel.parent as RectTransform;
            for (int step = 0; step < 6 && rt != null; step++)
            {
                if (Mathf.Abs(rt.anchorMin.y - rt.anchorMax.y) < 0.001f && rt.sizeDelta.y > 1f)
                {
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, Kept(rt, 0, rt.sizeDelta.y - before) + extra);
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                        Kept(rt, 1, rt.anchoredPosition.y + before * (1f - rt.pivot.y)) - extra * (1f - rt.pivot.y));
                }
                if (rt.GetComponent<Canvas>() != null) break;
                rt = rt.parent as RectTransform;
            }
            Grown[key] = extra;
            LayoutRebuilder.MarkLayoutForRebuild(panel);
            return plain + extra;
        }

        private static float Once(string key, float now)
        {
            float was;
            if (!Plain.TryGetValue(key, out was)) { was = now; Plain[key] = was; }
            return was;
        }

        private static float Kept(RectTransform rt, int slot, float now)
        {
            int key = rt.GetInstanceID() * 4 + slot;
            float was;
            if (!Base.TryGetValue(key, out was)) { was = now; Base[key] = was; }
            return was;
        }

        private static T Part<T>(GeneralUserInfoPanelContent panel, string name) where T : class
        {
            try { return AccessTools.Field(typeof(GeneralUserInfoPanelContent), name)?.GetValue(panel) as T; }
            catch { return null; }
        }

        private static void Build(GeneralUserInfoPanelContent panel, Unity3DUserInfoResponseMessage m, float width, float height)
        {
            var host = (RectTransform)panel.transform;
            var login = Part<Text>(panel, "LoginText");
            var caption = Part<Text>(panel, "KarmaCaption");
            Font font = caption != null ? caption.font : login != null ? login.font : null;
            Color ink = caption != null ? caption.color : new Color32(58, 36, 16, 255);
            ink.a = 1f;
            Color faint = new Color(ink.r, ink.g, ink.b, 0.28f);

            var go = new GameObject(RootName, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var root = (RectTransform)go.transform;
            OnlineWindow.Place(root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var mirror = go.AddComponent<CardMirror>();
            for (int i = 0; i < host.childCount; i++)
            {
                var child = host.GetChild(i);
                if (child != null && child != go.transform) mirror.Hide(child.gameObject);
            }

            float r = Mathf.Clamp((height - Pad * 2f) / Height, 24f, 34f);
            int size = Mathf.Clamp(Mathf.RoundToInt(r * 0.5f), 13, 17);

            float y = Pad;
            var loginCaption = Label(root, "Логин", size, FontStyle.Normal, ink, font);
            float captionW = loginCaption.preferredWidth + 12f;
            At(loginCaption.rectTransform, Pad, y, captionW, r * 1.3f);
            var loginValue = Label(root, m.Login ?? "", size + 4, FontStyle.Bold, ink, font);
            At(loginValue.rectTransform, Pad + captionW, y, width - Pad * 2f - captionW, r * 1.3f);
            y += r * 1.5f;

            float left = Mathf.Clamp(width * 0.27f, 140f, 280f);
            float x = Pad + left + 18f;
            float wide = width - x - Pad;
            float block = r * 16.4f;

            float buttonH = Mathf.Max(34f, r * 1.3f);
            float faceH = Mathf.Min(left * 4f / 3f, block - buttonH - 10f);
            var frame = Box(root, "face", Pad, y, left, faceH, faint, 10);
            var face = new GameObject("avatar", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)face.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(5f, 5f), new Vector2(-5f, -5f));
            var faceImage = face.GetComponent<Image>();
            faceImage.preserveAspect = true;
            faceImage.enabled = false;
            faceImage.raycastTarget = false;
            mirror.Add(Part<Image>(panel, "AvatarImage"), faceImage);

            int id = m.UserId;
            var site = Wardrobe.GameButton(root, "Информация на сайте", () => Application.OpenURL(Site + id), false);
            At(site, Pad, y + faceH + 10f, left, buttonH);

            var wait = new Waiting { UserId = m.UserId, Root = go };
            string race = RaceName(m.Race);
            string klass = ClassName(m.ClassId);
            string relation = Relation(m.ClanRelation);

            string[] names = { "Уровень", "Раса", "Характер", "Подкласс", "Клан", "Дата создания", "Победы", "Турнирный рейтинг", "Рейтинг", "Ранг" };
            float lw = Mathf.Min(Measure(root, names, size, font) + 14f, wide * 0.55f);
            int row = 0;
            Row(root, x, y + r * row++, wide, lw, r, "Уровень", m.Level.ToString(), size, ink, font);
            Pending(wait, Row(root, x, y + r * row++, wide, lw, r, "Раса", race ?? "…", size, ink, font), p => race ?? Field(p, "Раса"));
            Pending(wait, Row(root, x, y + r * row++, wide, lw, r, "Характер", "…", size, ink, font), p => Field(p, "Характер"));
            Pending(wait, Row(root, x, y + r * row++, wide, lw, r, "Подкласс", "…", size, ink, font), p => Field(p, "Подкласс") ?? Field(p, "Класс") ?? klass);
            bool icon = !string.IsNullOrEmpty(m.ClanName) && Part<Image>(panel, "ClanIconImage") != null && Part<Image>(panel, "ClanIconImage").sprite != null;
            var clan = Row(root, x, y + r * row++, wide, lw, r, "Клан",
                string.IsNullOrEmpty(m.ClanName) ? "нет клана" : m.ClanName + (relation != null ? " · " + relation : ""), size, ink, font);
            if (icon)
            {
                var box = (RectTransform)clan.transform.parent;
                clan.rectTransform.offsetMax = new Vector2(-(r - 2f), clan.rectTransform.offsetMax.y);
                var pic = new GameObject("clan", typeof(RectTransform), typeof(Image));
                pic.transform.SetParent(box, false);
                var irt = (RectTransform)pic.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(1f, 0.5f);
                irt.pivot = new Vector2(1f, 0.5f);
                irt.sizeDelta = new Vector2(r - 10f, r - 10f);
                irt.anchoredPosition = new Vector2(-5f, 0f);
                var image = pic.GetComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                mirror.Add(Part<Image>(panel, "ClanIconImage"), image);
            }
            Pending(wait, Row(root, x, y + r * row++, wide, lw, r, "Дата создания", "…", size, ink, font), p => Date(Field(p, "Дата регистрации")));
            Row(root, x, y + r * row++, wide, lw, r, "Победы", m.Wins.ToString(), size, ink, font);
            Row(root, x, y + r * row++, wide, lw, r, "Турнирный рейтинг", m.TournamentRank.ToString(), size, ink, font);
            Row(root, x, y + r * row++, wide, lw, r, "Рейтинг", m.Rank.ToString(), size, ink, font);
            if (!string.IsNullOrEmpty(m.Karma)) Row(root, x, y + r * row++, wide, lw, r, "Ранг", m.Karma, size, ink, font);
            else Pending(wait, Row(root, x, y + r * row++, wide, lw, r, "Ранг", "…", size, ink, font), p => Field(p, "Ранг"));

            float ya = y + r * 10.3f;
            float half = (wide - 20f) / 2f;
            float xm = x + half + 20f;
            var armorHead = Label(root, "Броня", size + 1, FontStyle.Bold, ink, font);
            At(armorHead.rectTransform, x, ya, half, r);
            var magicHead = Label(root, "Защита от магии", size + 1, FontStyle.Bold, ink, font);
            At(magicHead.rectTransform, xm, ya, half, r);
            string[] zones = { "Голова", "Туловище", "Правая рука", "Левая рука", "Ноги" };
            string[] schools = { "Рассвет", "Полнолуние", "Астрал" };
            float zw = Mathf.Min(Measure(root, zones, size, font) + 12f, half - 40f);
            float mw = Mathf.Min(Measure(root, schools, size, font) + 12f, half - 40f);
            int[] armor = { m.HeadArmor, m.BodyArmor, m.RightHandArmor, m.LeftHandArmor, m.LagsArmor };
            if (Armor.On)
                for (int i = 0; i < 5; i++)
                    Row(root, x, ya + r * (i + 1), half, zw, r, zones[i], armor[i].ToString(), size, ink, font);
            else
                Row(root, x, ya + r, half, zw, r, "Средняя", ((armor[0] + armor[1] + armor[2] + armor[3] + armor[4]) / 5).ToString(), size, ink, font);
            int[] magic = { m.WhiteMagicProtection, m.BlackMagicProtection, m.AstralMagicProtection };
            for (int i = 0; i < 3; i++)
                Row(root, xm, ya + r * (i + 1), half, mw, r, schools[i], magic[i].ToString(), size, ink, font);

            float yn = ya + r * 6.4f;
            var note = Notes.Box(root, m.UserId, m.Login, size, font);
            if (note != null) At(note, Pad, yn, width - Pad * 2f, r * 1.4f);

            if (wait.Cells.Count > 0) Ask(wait);
        }

        private static float Measure(Transform root, string[] names, int size, Font font)
        {
            var probe = Label(root, "", size, FontStyle.Normal, Color.clear, font);
            float most = 0f;
            foreach (var name in names)
            {
                probe.text = name;
                most = Mathf.Max(most, probe.preferredWidth);
            }
            UnityEngine.Object.DestroyImmediate(probe.gameObject);
            return most;
        }

        private static Text Row(RectTransform root, float x, float y, float wide, float lw, float r, string name, string value, int size, Color ink, Font font)
        {
            var label = Label(root, name, size, FontStyle.Normal, ink, font);
            At(label.rectTransform, x, y, lw, r);
            var line = Box(root, "dots", x + 2f, y + r - 5f, lw - 6f, 1f, new Color(ink.r, ink.g, ink.b, 0.3f), 1);
            line.GetComponent<Image>().raycastTarget = false;
            var box = Box(root, "value", x + lw + 4f, y + 2f, wide - lw - 4f, r - 4f, new Color(1f, 0.98f, 0.93f, 0.55f), 8);
            var edge = box.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(ink.r, ink.g, ink.b, 0.35f);
            edge.effectDistance = new Vector2(1f, -1f);
            var text = Label(box, value, size, FontStyle.Bold, ink, font);
            OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 1f), new Vector2(-8f, -1f));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = size;
            return text;
        }

        private static Text Label(Transform parent, string text, int size, FontStyle style, Color color, Font font)
        {
            var label = OnlineWindow.Label(parent, text, size, style, color);
            if (font != null) label.font = font;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color color, int radius)
        {
            var box = Wardrobe.Box(parent, name, x, y, w, h, color, radius);
            box.GetComponent<Image>().raycastTarget = false;
            return box;
        }

        private static void At(RectTransform rt, float x, float y, float w, float h) => Wardrobe.At(rt, x, y, w, h);

        private static void Pending(Waiting wait, Text cell, Func<Profile, string> pick)
        {
            wait.Cells.Add(new KeyValuePair<Text, Func<Profile, string>>(cell, pick));
        }

        private static string RaceName(int id)
        {
            WardrobeData.HasRules();
            foreach (var race in WardrobeData.Races) if (race.Id == id) return race.Name;
            return null;
        }

        private static string ClassName(int id)
        {
            WardrobeData.HasRules();
            foreach (var klass in WardrobeData.Classes) if (klass.Id == id) return klass.Name;
            return null;
        }

        private static string Relation(int? code)
        {
            if (code == null) return null;
            try
            {
                string key = "userinfo.relation.text." + code.Value;
                string text = ResourceStrings.GetString(key);
                return string.IsNullOrEmpty(text) || text == key ? null : text.ToLowerInvariant();
            }
            catch { return null; }
        }

        private static string Field(Profile profile, string name)
        {
            string value;
            return profile != null && profile.Fields.TryGetValue(name, out value) && value.Length > 0 ? value : null;
        }

        private static string Date(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int day, year;
            if (parts.Length != 3 || !int.TryParse(parts[0], out day) || !int.TryParse(parts[2], out year)) return raw;
            string word = parts[1].ToLowerInvariant();
            foreach (var names in Months)
            {
                int month = Array.IndexOf(names, word);
                if (month >= 0) return day.ToString("00") + "." + (month + 1).ToString("00") + "." + year;
            }
            return raw;
        }

        private static void Ask(Waiting wait)
        {
            Live.RemoveAll(w => w.Root == null);
            Live.Add(wait);
            Profile profile;
            if (Profiles.TryGetValue(wait.UserId, out profile) && !profile.Done) return;
            Plugin.Instance.StartCoroutine(Fetch(wait.UserId));
        }

        private static IEnumerator Fetch(int userId)
        {
            var profile = new Profile();
            Profiles[userId] = profile;
            var req = UnityWebRequest.Get(Site + userId);
            req.timeout = 15;
            yield return req.SendWebRequest();
            string body = req.responseCode == 200 && req.downloadHandler != null ? Encoding.UTF8.GetString(req.downloadHandler.data ?? new byte[0]) : null;
            if (body == null) Plugin.Trace("[карточка] профиль " + userId + " с сайта не пришёл: " + (req.error ?? "код " + req.responseCode));
            req.Dispose();
            try
            {
                if (body != null)
                    foreach (Match match in Item.Matches(body))
                    {
                        string value = WebUtility.HtmlDecode(Tag.Replace(match.Groups[2].Value, ""));
                        profile.Fields[match.Groups[1].Value.Trim()] = Space.Replace(value, " ").Trim();
                    }
            }
            catch (Exception e) { Plugin.Trace("[карточка] профиль " + userId + " не разобран: " + e.Message); }
            profile.Failed = profile.Fields.Count == 0;
            profile.Done = true;
            foreach (var wait in Live.ToArray())
                if (wait.UserId == userId && wait.Root != null) Fill(wait, profile);
            Live.RemoveAll(w => w.Root == null || w.UserId == userId);
        }

        private static void Fill(Waiting wait, Profile profile)
        {
            foreach (var cell in wait.Cells)
            {
                if (cell.Key == null) continue;
                string value = null;
                try { value = cell.Value(profile); }
                catch { }
                cell.Key.text = string.IsNullOrEmpty(value) ? "—" : value;
            }
        }
    }

    internal sealed class CardMirror : MonoBehaviour
    {
        private readonly List<KeyValuePair<Image, Image>> _pairs = new List<KeyValuePair<Image, Image>>();
        private readonly List<GameObject> _hidden = new List<GameObject>();

        internal void Add(Image from, Image to)
        {
            if (from == null || to == null) return;
            _pairs.Add(new KeyValuePair<Image, Image>(from, to));
            Copy(from, to);
        }

        internal void Hide(GameObject go)
        {
            if (go != null) _hidden.Add(go);
        }

        private void Update()
        {
            foreach (var pair in _pairs) Copy(pair.Key, pair.Value);
            foreach (var go in _hidden)
                if (go != null && go.activeSelf && go.transform.localScale != Vector3.zero) go.transform.localScale = Vector3.zero;
        }

        private static void Copy(Image from, Image to)
        {
            if (from == null || to == null || to.sprite == from.sprite) return;
            to.sprite = from.sprite;
            to.enabled = from.sprite != null;
        }
    }
}
