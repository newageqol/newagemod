using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class WardrobeSkill
    {
        internal int Id;
        internal string Name;
        internal int Side;
        internal int Excl;
        internal readonly int[] Min = new int[5];
        internal readonly int[][] Req = { new int[7], new int[7], new int[7], new int[7], new int[7] };
        internal readonly string[] Text = new string[5];
    }

    internal sealed class WardrobeSkillRule
    {
        internal int Class;
        internal int Sub;
        internal char Kind;
        internal readonly List<KeyValuePair<int, int>> Pairs = new List<KeyValuePair<int, int>>();
    }

    internal static class WardrobeSkills
    {
        internal const int Size = 25;
        internal const int Fortress = 5;
        internal static readonly string[] Steps = { "Новичок", "Продвинутый", "Эксперт", "Мастер", "Грандмастер" };

        private static readonly List<WardrobeSkill> Skills = new List<WardrobeSkill>();
        private static readonly List<WardrobeSkillRule> Rules = new List<WardrobeSkillRule>();
        private static readonly Dictionary<int, int> Starts = new Dictionary<int, int>();
        private static readonly HashSet<int> DarkRaces = new HashSet<int>();
        private static bool _loaded;

        private const float W = 940f;
        private const float H = 810f;
        private const float RowH = 26f;
        private const float ListW = 500f;
        private const float Top = 112f;
        private const float Dot = 20f;
        private const float DotX = 250f;
        private static readonly Color Green = new Color32(52, 128, 66, 255);

        private static GameObject _go;
        private static Text _info, _status, _detail;
        private static readonly List<Row> RowList = new List<Row>();
        private static readonly int[] KeptMast = new int[Size];
        private static readonly int[] KeptDist = new int[7];
        private static readonly List<int> KeptPrefer = new List<int>();
        private static int _keptRank;
        private static int _picked;
        private static string _say;

        private sealed class Row
        {
            internal int Id;
            internal bool Pair;
            internal Image Back;
            internal Text Name, Step;
            internal Image Radio;
            internal readonly Button[] Dots = new Button[5];
            internal readonly Image[] Fill = new Image[5];
            internal readonly Outline[] Ring = new Outline[5];
        }

        internal static bool Ready
        {
            get
            {
                Load();
                return Skills.Count > 0;
            }
        }

        internal static List<WardrobeSkill> All
        {
            get
            {
                Load();
                return Skills;
            }
        }

        internal static bool IsOpen => _go != null;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string text = WardrobeData.Rules();
                if (text == null) return;
                foreach (var line in text.Split('\n')) Parse(line.TrimEnd('\r'));
                Skills.Sort((a, b) => a.Id.CompareTo(b.Id));
                Plugin.Trace("[wardrobe] general skills " + Skills.Count + ", subclass rules " + Rules.Count + ", race starts " + Starts.Count);
            }
            catch (Exception e)
            {
                Skills.Clear();
                Rules.Clear();
                Starts.Clear();
                Plugin.Warn("[wardrobe] general skills not read: " + e.Message);
            }
        }

        private static void Parse(string line)
        {
            var cell = line.Split('\t');
            switch (cell[0])
            {
                case "G":
                {
                    if (cell.Length < 10) return;
                    var skill = new WardrobeSkill { Id = Int(cell[1]), Name = cell[2], Side = Int(cell[3]), Excl = Int(cell[4]) };
                    if (skill.Id <= 0 || skill.Id >= Size) return;
                    for (int k = 0; k < 5; k++)
                    {
                        var bits = cell[5 + k].Split(new[] { ';' }, 3);
                        if (bits.Length < 3) return;
                        skill.Min[k] = Int(bits[0]);
                        var req = bits[1].Split(',');
                        for (int i = 0; i < 7 && i < req.Length; i++) skill.Req[k][i] = Int(req[i]);
                        skill.Text[k] = bits[2];
                    }
                    Skills.Add(skill);
                    return;
                }
                case "GS":
                    if (cell.Length < 4) return;
                    Starts[Int(cell[1])] = Int(cell[2]);
                    if (cell[3] == "1") DarkRaces.Add(Int(cell[1]));
                    return;
                case "GQ":
                {
                    if (cell.Length < 5 || cell[3].Length != 1) return;
                    var rule = new WardrobeSkillRule { Class = Int(cell[1]), Sub = Int(cell[2]), Kind = cell[3][0] };
                    foreach (var part in cell[4].Split(','))
                    {
                        var bits = part.Split(':');
                        if (bits.Length == 2) rule.Pairs.Add(new KeyValuePair<int, int>(Int(bits[0]), Int(bits[1])));
                    }
                    Rules.Add(rule);
                    return;
                }
            }
        }

        private static int Int(string text)
        {
            int value;
            return int.TryParse(text, out value) ? value : 0;
        }

        internal static WardrobeSkill Get(int id)
        {
            foreach (var skill in All) if (skill.Id == id) return skill;
            return null;
        }

        internal static int Start(int race)
        {
            Load();
            int id;
            return Starts.TryGetValue(race, out id) ? id : 0;
        }

        internal static int MinLevel(int id, int level)
        {
            var skill = Get(id);
            return skill == null || level <= 0 || level > 5 ? 0 : skill.Min[level - 1];
        }

        private static bool Allowed(WardrobeState s, WardrobeSkill skill)
        {
            if (skill.Side == 0) return true;
            bool dark = DarkRaces.Contains(s.RaceId);
            return skill.Side == 1 ? dark : !dark;
        }

        private static bool Banned(WardrobeState s, int id)
        {
            foreach (var rule in Rules)
            {
                if (rule.Class != s.ClassId || rule.Kind != 'X') continue;
                foreach (var pair in rule.Pairs) if (pair.Key == id) return true;
            }
            return false;
        }

        private static WardrobeSkill Clash(WardrobeState s, WardrobeSkill skill)
        {
            if (skill.Excl <= 0 || s.Mastery(skill.Excl) <= 0) return null;
            return Get(skill.Excl);
        }

        private static bool Shown(WardrobeState s, WardrobeSkill skill) => Allowed(s, skill) && !Banned(s, skill.Id);

        internal static void Trim(WardrobeState s)
        {
            if (!Ready) return;
            int start = Start(s.RaceId);
            foreach (var skill in Skills)
            {
                int id = skill.Id;
                int level = s.Mastery(id);
                if (level > 5) level = 5;
                if (id != start && level > 0 && !Shown(s, skill)) level = 0;
                int floor = id == start ? 1 : 0;
                while (level > floor && skill.Min[level - 1] > s.Level) level--;
                if (level < floor) level = floor;
                if (level != s.Mastery(id)) s.SetMastery(id, level);
            }
            foreach (var skill in Skills)
            {
                if (skill.Excl <= 0 || s.Mastery(skill.Id) <= 0 || s.Mastery(skill.Excl) <= 0) continue;
                s.SetMastery(skill.Id == start ? skill.Excl : skill.Id, 0);
            }
            foreach (var skill in Skills)
            {
                int id = skill.Id;
                int need = Forced(s, id);
                int was = s.Fit[id];
                int level = s.Mastery(id);
                if (was > 0 && was != need && level == was) level = Math.Max(need, id == start ? 1 : 0);
                if (level < need) level = need;
                if (level != s.Mastery(id)) s.SetMastery(id, level);
                s.Fit[id] = need;
            }
            int guard = Size * 5;
            while (s.SkillSpent > s.SkillPoints && guard-- > 0)
            {
                bool cut = false;
                for (int n = Skills.Count - 1; n >= 0 && !cut; n--)
                {
                    int id = Skills[n].Id;
                    int level = s.Mastery(id);
                    if (level > Floor(s, id)) { s.SetMastery(id, level - 1); cut = true; }
                }
                if (!cut) break;
            }
        }

        private static int Floor(WardrobeState s, int id) => Math.Max(Forced(s, id), id == Start(s.RaceId) ? 1 : 0);

        private static bool Usable(WardrobeState s, int id)
        {
            var skill = Get(id);
            return skill != null && Shown(s, skill) && Clash(s, skill) == null;
        }

        private static int Choice(WardrobeState s, WardrobeSkillRule group)
        {
            foreach (var pair in group.Pairs)
                if (s.Prefer.Contains(pair.Key) && Usable(s, pair.Key)) return pair.Key;
            int start = Start(s.RaceId);
            int best = 0, score = -1;
            foreach (var pair in group.Pairs)
            {
                if (!Usable(s, pair.Key)) continue;
                int mine = s.Mastery(pair.Key) * 10 + (pair.Key == start ? 5 : 0);
                if (mine > score) { score = mine; best = pair.Key; }
            }
            return best;
        }

        private static int Required(WardrobeState s, int sub, int id)
        {
            int need = 0;
            foreach (var rule in Rules)
            {
                if (rule.Class != s.ClassId || rule.Sub != sub || rule.Kind == 'X') continue;
                if (rule.Kind == 'O' && Choice(s, rule) != id) continue;
                foreach (var pair in rule.Pairs) if (pair.Key == id && pair.Value > need) need = pair.Value;
            }
            return need;
        }

        internal static int Forced(WardrobeState s, int id)
        {
            if (!Ready || s.SubN <= 0) return 0;
            return Required(s, s.SubN, id);
        }

        private static WardrobeSkillRule Group(WardrobeState s, int id)
        {
            if (s.SubN <= 0) return null;
            foreach (var rule in Rules)
            {
                if (rule.Class != s.ClassId || rule.Sub != s.SubN || rule.Kind != 'O') continue;
                foreach (var pair in rule.Pairs) if (pair.Key == id) return rule;
            }
            return null;
        }

        internal static List<string> Missing(WardrobeState s, int sub)
        {
            var list = new List<string>();
            if (!Ready || sub <= 0) return list;
            foreach (var rule in Rules)
            {
                if (rule.Class != s.ClassId || rule.Sub != sub) continue;
                if (rule.Kind == 'N')
                {
                    foreach (var pair in rule.Pairs)
                        if (s.Mastery(pair.Key) < pair.Value) list.Add(Name(pair.Key) + " " + pair.Value);
                }
                else if (rule.Kind == 'O')
                {
                    bool done = false;
                    var names = new List<string>();
                    foreach (var pair in rule.Pairs)
                    {
                        if (s.Mastery(pair.Key) >= pair.Value) done = true;
                        var skill = Get(pair.Key);
                        if (skill != null && Allowed(s, skill)) names.Add(skill.Name);
                    }
                    if (!done && rule.Pairs.Count > 0) list.Add(string.Join(" или ", names.ToArray()) + " " + rule.Pairs[0].Value);
                }
                else
                {
                    foreach (var pair in rule.Pairs)
                        if (s.Mastery(pair.Key) > 0) list.Add("без «" + Name(pair.Key) + "»");
                }
            }
            return list;
        }

        private static string Name(int id)
        {
            var skill = Get(id);
            return skill != null ? skill.Name : "умение " + id;
        }

        internal static string Caption(WardrobeState s)
        {
            if (!Ready) return "нет данных";
            int free = s.SkillFree;
            return s.SkillSpent + " из " + s.SkillPoints + (free >= 0 ? ", свободно " + free : ", лишних " + (-free));
        }

        internal static void Open()
        {
            Close();
            var panel = Wardrobe.Panel;
            if (panel == null) return;
            if (!Ready) { Notice.Show("Переодевалка: в сборке нет данных об общих умениях", 5f); return; }
            var s = Wardrobe.S;
            for (int id = 0; id < Size; id++) KeptMast[id] = s.Mast[id];
            Array.Copy(s.Dist, KeptDist, 7);
            KeptPrefer.Clear();
            KeptPrefer.AddRange(s.Prefer);
            _keptRank = s.Rank;

            _go = new GameObject("QoLWardrobeSkills", typeof(RectTransform), typeof(Image));
            _go.transform.SetParent(panel, false);
            OnlineWindow.Place((RectTransform)_go.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var boxGo = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
            boxGo.transform.SetParent(_go.transform, false);
            var box = (RectTransform)boxGo.transform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(W, H);
            var img = boxGo.GetComponent<Image>();
            img.color = WardrobeLook.Popup;
            img.sprite = OnlineWindow.Rounded(16);
            img.type = Image.Type.Sliced;
            var edge = boxGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(box, "Общие умения", 20, FontStyle.Bold, WardrobeLook.Bright);
            Wardrobe.At(title.rectTransform, 18f, 12f, W - 90f, 32f);
            title.alignment = TextAnchor.MiddleLeft;
            var close = Wardrobe.Arrow(box, "×", Cancel);
            Wardrobe.At((RectTransform)close.transform, W - 50f, 12f, 34f, 30f);

            _info = Note(box, 18f, 46f, W - 36f, 24f, 15, WardrobeLook.Accent);
            _status = Note(box, 18f, 70f, W - 36f, 40f, 13, WardrobeLook.Label);

            RowList.Clear();
            var used = new HashSet<int>();
            var sub = s.Sub;
            float y = Top;
            Section(box, sub != null ? "Нужно подклассу «" + sub.Name + "»" : "Подкласса пока нет — все умения свободные", y);
            y += RowH;
            if (sub != null)
            {
                foreach (var rule in Rules)
                {
                    if (rule.Class != s.ClassId || rule.Sub != sub.N || rule.Kind != 'N') continue;
                    foreach (var pair in rule.Pairs)
                        if (used.Add(pair.Key) && Get(pair.Key) != null) { RowList.Add(MakeRow(box, pair.Key, false, y)); y += RowH; }
                }
                foreach (var rule in Rules)
                {
                    if (rule.Class != s.ClassId || rule.Sub != sub.N || rule.Kind != 'O') continue;
                    var options = new List<int>();
                    foreach (var pair in rule.Pairs)
                    {
                        var skill = Get(pair.Key);
                        if (skill != null && Shown(s, skill) && !used.Contains(pair.Key)) options.Add(pair.Key);
                    }
                    bool pair2 = options.Count > 1;
                    foreach (int id in options)
                    {
                        used.Add(id);
                        RowList.Add(MakeRow(box, id, pair2, y));
                        y += RowH;
                    }
                }
            }
            y += 8f;
            Section(box, "Остальные", y);
            y += RowH;
            foreach (var skill in Skills)
            {
                if (used.Contains(skill.Id) || !Shown(s, skill)) continue;
                RowList.Add(MakeRow(box, skill.Id, false, y));
                y += RowH;
            }
            for (int n = 0; n < RowList.Count; n++) RowList[n].Back.color = WardrobeLook.Stripe(n);
            if (_picked == 0 || Get(_picked) == null || !Listed(_picked)) _picked = RowList.Count > 0 ? RowList[0].Id : 0;

            float deskX = 12f + ListW + 16f;
            var desk = Wardrobe.Box(box, "desk", deskX, Top, W - deskX - 16f, H - Top - 70f, WardrobeLook.Card, 10);
            _detail = OnlineWindow.Label(desk, "", 14, FontStyle.Normal, WardrobeLook.Body);
            OnlineWindow.Place(_detail.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, 12f), new Vector2(-14f, -12f));
            _detail.alignment = TextAnchor.UpperLeft;
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            _detail.verticalOverflow = VerticalWrapMode.Truncate;
            _detail.supportRichText = true;

            float by = H - 56f;
            float bw = (W - 32f - 12f) / 2f;
            var done = Wardrobe.GameButton(box, "Готово", Close, false);
            Wardrobe.At(done, 16f, by, bw, 40f);
            done.GetComponent<Image>().color = Green;
            var label = done.GetComponentInChildren<Text>();
            if (label != null) label.color = Color.white;
            Wardrobe.At(Wardrobe.GameButton(box, "Сбросить свободные", Reset, true), 16f + bw + 12f, by, bw, 40f);

            Refresh();
        }

        private static bool Listed(int id)
        {
            foreach (var row in RowList) if (row.Id == id) return true;
            return false;
        }

        private static void Section(RectTransform box, string text, float y)
        {
            var label = OnlineWindow.Label(box, text.ToUpperInvariant(), 13, FontStyle.Bold, WardrobeLook.Accent);
            Wardrobe.At(label.rectTransform, 18f, y, ListW, RowH);
            label.alignment = TextAnchor.MiddleLeft;
        }

        private static Text Note(RectTransform box, float x, float y, float w, float h, int size, Color color)
        {
            var text = OnlineWindow.Label(box, "", size, FontStyle.Normal, color);
            Wardrobe.At(text.rectTransform, x, y, w, h);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Row MakeRow(RectTransform box, int id, bool pair, float y)
        {
            var row = new Row { Id = id, Pair = pair };
            var strip = Wardrobe.Box(box, "skill" + id, 12f, y, ListW, RowH - 2f, WardrobeLook.Stripe(0), 5);
            row.Back = strip.GetComponent<Image>();
            var pick = strip.gameObject.AddComponent<Button>();
            pick.transition = Selectable.Transition.None;
            pick.onClick.AddListener(() => { _picked = id; _say = null; Refresh(); });

            float nameX = 10f;
            if (pair)
            {
                var radio = Circle(strip, 8f, (RowH - 2f - 16f) / 2f, 16f, () => Swap(id));
                row.Radio = radio.GetComponent<Image>();
                nameX = 32f;
            }
            row.Name = Cell(strip, nameX, DotX - nameX - 8f, TextAnchor.MiddleLeft, 14);
            for (int k = 0; k < 5; k++)
            {
                int step = k + 1;
                var dot = Circle(strip, DotX + k * (Dot + 8f), (RowH - 2f - Dot) / 2f, Dot, () => Set(id, step));
                row.Dots[k] = dot;
                row.Fill[k] = dot.GetComponent<Image>();
                row.Ring[k] = dot.GetComponent<Outline>();
            }
            row.Step = Cell(strip, DotX + 5f * (Dot + 8f) + 4f, ListW - (DotX + 5f * (Dot + 8f) + 4f) - 6f, TextAnchor.MiddleLeft, 13);
            return row;
        }

        private static Button Circle(RectTransform parent, float x, float y, float size, Action click)
        {
            var go = new GameObject("dot", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(parent, false);
            Wardrobe.At((RectTransform)go.transform, x, y, size, size);
            var image = go.GetComponent<Image>();
            image.sprite = OnlineWindow.Rounded((int)(size / 2f));
            image.type = Image.Type.Sliced;
            var ring = go.GetComponent<Outline>();
            ring.effectDistance = new Vector2(1f, -1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                try { click(); }
                catch (Exception e) { Plugin.Warn("[wardrobe] skill: " + e.Message); }
            });
            return button;
        }

        private static Text Cell(RectTransform parent, float x, float w, TextAnchor align, int size)
        {
            var text = OnlineWindow.Label(parent, "", size, FontStyle.Normal, WardrobeLook.Bright);
            Wardrobe.At(text.rectTransform, x, 0f, w, RowH - 2f);
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        internal static void Close()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _say = null;
            _info = _status = _detail = null;
            RowList.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_go == null) return false;
            Cancel();
            return true;
        }

        internal static void Cancel()
        {
            if (_go == null) return;
            Close();
            var s = Wardrobe.S;
            bool same = s.Rank == _keptRank && s.Prefer.Count == KeptPrefer.Count;
            for (int id = 0; id < Size; id++) if (s.Mast[id] != KeptMast[id]) same = false;
            for (int i = 0; i < 7; i++) if (s.Dist[i] != KeptDist[i]) same = false;
            foreach (int id in KeptPrefer) if (!s.Prefer.Contains(id)) same = false;
            if (same) return;
            for (int id = 0; id < Size; id++) s.Mast[id] = KeptMast[id];
            Array.Copy(KeptDist, s.Dist, 7);
            s.Prefer.Clear();
            s.Prefer.AddRange(KeptPrefer);
            s.Rank = _keptRank;
            Wardrobe.Changed(true);
        }

        internal static void Refresh()
        {
            if (_go == null) return;
            var s = Wardrobe.S;
            int free = s.SkillFree;
            _info.text = free >= 0 ? "Свободно умений: " + free : "Лишних умений: " + (-free);
            _info.color = free < 0 ? WardrobeLook.Bad : WardrobeLook.Accent;

            var sub = s.Sub;
            if (_say != null)
            {
                _status.text = _say;
                _status.color = WardrobeLook.Bad;
            }
            else
            {
                var miss = sub != null ? Missing(s, sub.N) : new List<string>();
                _status.text = miss.Count > 0
                    ? "«" + sub.Name + "»: не хватает " + string.Join(", ", miss.ToArray()) + "."
                    : "";
                _status.color = miss.Count > 0 ? WardrobeLook.Bad : WardrobeLook.Label;
            }

            for (int n = 0; n < RowList.Count; n++) Paint(s, RowList[n], n);
            Describe(s);
        }

        private static void Paint(WardrobeState s, Row row, int n)
        {
            var skill = Get(row.Id);
            if (skill == null) return;
            int level = s.Mastery(skill.Id);
            int floor = Floor(s, skill.Id);
            var clash = level > 0 ? null : Clash(s, skill);
            bool start = skill.Id == Start(s.RaceId);
            row.Back.color = row.Id == _picked ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.28f) : WardrobeLook.Stripe(n);
            row.Name.text = skill.Name + (start ? " ★" : "");
            row.Name.color = clash != null ? WardrobeLook.Faint : WardrobeLook.Bright;
            row.Step.text = clash != null ? "" : level > 0 ? Steps[level - 1] : "";
            row.Step.color = clash != null ? WardrobeLook.Faint : WardrobeLook.Label;

            for (int k = 1; k <= 5; k++)
            {
                bool taken = k <= level;
                bool locked = taken && k <= floor;
                bool reach = clash == null && skill.Min[k - 1] <= s.Level;
                var fill = row.Fill[k - 1];
                var ring = row.Ring[k - 1];
                fill.color = locked ? WardrobeLook.Accent : taken ? WardrobeLook.Bright : reach ? WardrobeLook.Field : new Color(1f, 1f, 1f, 0.04f);
                ring.effectColor = locked ? WardrobeLook.Accent : taken ? WardrobeLook.Bright : reach ? WardrobeLook.Label : new Color(1f, 1f, 1f, 0.12f);
                row.Dots[k - 1].interactable = reach && !(locked && k < floor) && !(k == floor && level == floor);
            }

            if (row.Radio != null)
            {
                var group = Group(s, skill.Id);
                bool chosen = group != null && Choice(s, group) == skill.Id;
                row.Radio.color = chosen ? WardrobeLook.Accent : WardrobeLook.Field;
                var ring = row.Radio.GetComponent<Outline>();
                if (ring != null) ring.effectColor = chosen ? WardrobeLook.Accent : WardrobeLook.Label;
            }
        }

        private static void Describe(WardrobeState s)
        {
            if (_detail == null) return;
            var skill = Get(_picked);
            if (skill == null) { _detail.text = ""; return; }
            int level = s.Mastery(skill.Id);
            int forced = Forced(s, skill.Id);
            var text = new StringBuilder();
            text.Append("<size=18><b>").Append(skill.Name).Append("</b></size>\n");
            var sub = s.Sub;
            if (forced > 0 && sub != null) text.Append(Hex(WardrobeLook.Accent, "Нужно «" + sub.Name + "»: " + Steps[forced - 1].ToLowerInvariant())).Append('\n');
            var group = Group(s, skill.Id);
            if (group != null && forced == 0 && sub != null)
            {
                int chosen = Choice(s, group);
                if (chosen > 0) text.Append(Hex(WardrobeLook.Accent, "Для «" + sub.Name + "» можно вместо «" + Name(chosen) + "» — отметь кружок слева от названия")).Append('\n');
            }
            var clash = level > 0 ? null : Clash(s, skill);
            if (clash != null) text.Append(Hex(WardrobeLook.Bad, "Нельзя вместе с «" + clash.Name + "»")).Append('\n');
            for (int k = 1; k <= 5; k++)
            {
                bool mine = k == level;
                var head = Hex(mine ? WardrobeLook.Accent : k < level ? WardrobeLook.Label : WardrobeLook.Bright,
                    (mine ? "▸ " : "") + Steps[k - 1] + " (" + Terms(skill, k) + ")");
                text.Append('\n').Append(mine ? "<b>" + head + "</b>" : head).Append('\n');
                text.Append(Hex(k <= level ? WardrobeLook.Body : WardrobeLook.Faint, skill.Text[k - 1])).Append('\n');
            }
            _detail.text = text.ToString();
        }

        private static string Hex(Color color, string text) => "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";

        private static string Terms(WardrobeSkill skill, int level)
        {
            var parts = new List<string> { "с " + skill.Min[level - 1] + " ур." };
            for (int n = 0; n < 7; n++)
            {
                int i = WardrobeData.StatOrder[n];
                int value = skill.Req[level - 1][i];
                if (value > 0) parts.Add(WardrobeData.StatNames[i].ToLowerInvariant() + " " + value);
            }
            return string.Join(", ", parts.ToArray());
        }

        private static void Set(int id, int step)
        {
            var s = Wardrobe.S;
            var skill = Get(id);
            if (skill == null) return;
            _picked = id;
            _say = null;
            int level = s.Mastery(id);
            int want = step == level ? step - 1 : step;
            int floor = Floor(s, id);
            if (want < floor) want = floor;
            if (want == level) { Refresh(); return; }
            if (want > level)
            {
                var clash = Clash(s, skill);
                if (clash != null) { Say("«" + skill.Name + "» нельзя вместе с «" + clash.Name + "»"); return; }
                if (skill.Min[want - 1] > s.Level) { Say("«" + skill.Name + "» " + Steps[want - 1].ToLowerInvariant() + " — с " + skill.Min[want - 1] + " уровня"); return; }
                int more = want - level;
                if (more > s.SkillFree) { Say("Не хватает свободных умений: нужно " + more + ", свободно " + Math.Max(0, s.SkillFree)); return; }
            }
            Apply(p => p.SetMastery(id, want));
        }

        private static void Swap(int id)
        {
            var s = Wardrobe.S;
            _picked = id;
            _say = null;
            var group = Group(s, id);
            if (group == null) { Refresh(); return; }
            int old = Choice(s, group);
            if (old == id) { Refresh(); return; }
            var skill = Get(id);
            var clash = skill != null ? Clash(s, skill) : null;
            if (clash != null && clash.Id != old) { Say("«" + skill.Name + "» нельзя вместе с «" + clash.Name + "»"); return; }
            int need = old > 0 ? Forced(s, old) : 0;
            int start = Start(s.RaceId);
            Apply(p =>
            {
                foreach (var pair in group.Pairs) p.Prefer.Remove(pair.Key);
                p.Prefer.Add(id);
                if (old > 0)
                {
                    p.SetMastery(old, old == start ? 1 : 0);
                    p.Fit[old] = 0;
                }
                if (p.Mastery(id) < need) p.SetMastery(id, need);
                p.Fit[id] = need;
            });
        }

        private static void Say(string text)
        {
            _say = text;
            Refresh();
        }

        private static void Reset()
        {
            _say = null;
            Apply(Unlearn);
        }

        internal static void Unlearn(WardrobeState p)
        {
            if (!Ready) return;
            int start = Start(p.RaceId);
            var keep = new int[Size];
            foreach (var skill in Skills) keep[skill.Id] = Forced(p, skill.Id);
            if (start > 0 && keep[start] < 1) keep[start] = 1;
            p.Forget();
            for (int id = 1; id < Size; id++) if (keep[id] > 0) p.SetMastery(id, keep[id]);
        }

        private static void Apply(Action<WardrobeState> change)
        {
            var s = Wardrobe.S;
            var probe = s.Clone();
            change(probe);
            Trim(probe);
            var race = probe.Race;
            if (race == null) { Refresh(); return; }
            for (int i = 0; i < 7; i++)
            {
                int need = Math.Max(0, probe.Floor(i) - race.Base[i] - probe.Potion(i));
                if (probe.Dist[i] < need) probe.Dist[i] = need;
            }
            if (probe.Spent > WardrobeState.Points(probe.Level))
            {
                Say("Не хватает очков характеристик на требования умения: нужно ещё " + (probe.Spent - WardrobeState.Points(probe.Level)));
                return;
            }
            for (int id = 0; id < Size; id++) s.Mast[id] = probe.Mast[id];
            for (int id = 0; id < Size; id++) s.Fit[id] = probe.Fit[id];
            s.Prefer.Clear();
            s.Prefer.AddRange(probe.Prefer);
            s.Rank = probe.Rank;
            Array.Copy(probe.Dist, s.Dist, 7);
            Wardrobe.Changed(true);
        }
    }
}
