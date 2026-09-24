using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WardrobeArt
    {
        internal const int Rarity = 4;
        private const int Count = 16;
        private const int Energy = 15;
        private const int Unlimited = 999999;
        private const float Bottom = 62f;

        private sealed class Row
        {
            internal int K;
            internal GameObject Go;
            internal InputField Input;
        }

        private static GameObject _go;
        private static GameObject _shade;
        private static RectTransform _scroll;
        private static int _slot;
        private static List<WardrobeArtKind> _kinds = new List<WardrobeArtKind>();
        private static int _kind;
        private static int _level;
        private static readonly int[] Values = new int[Count];
        private static readonly List<Row> Rows = new List<Row>();
        private static Text _type, _levelText, _points, _gain, _limits;
        private static Button _wear, _wearAll, _reset;

        internal static bool IsOpen => _go != null;

        private static int Cost(int k)
        {
            if (k < 7) return 3;
            if (k < 12) return 1;
            if (k < 14) return 2;
            if (k == 14) return 3;
            return 36;
        }

        private static bool HasEnergy(int sub) => sub == 40 || sub == 55;

        private static int Limit(WardrobeArtRule rule, int k)
        {
            if (rule == null) return 0;
            if (k == Energy) return HasEnergy(rule.Sub) ? (rule.MaxEnergy > 0 ? rule.MaxEnergy : Unlimited) : 0;
            int max = k < 7 ? rule.MaxStat : k < 12 ? rule.MaxArmor : rule.MaxMagic;
            return max > 0 ? max : Unlimited;
        }

        private static int Spent(int[] values)
        {
            int sum = 0;
            for (int k = 0; k < Count; k++) sum += values[k] * Cost(k);
            return sum;
        }

        private static int[] ValuesOf(WardrobeThing thing)
        {
            var values = new int[Count];
            for (int i = 0; i < 7; i++) values[i] = thing.Bonus[i];
            for (int p = 0; p < 5; p++) values[7 + p] = thing.Armor[p];
            for (int m = 0; m < 3; m++) values[12 + m] = thing.Magic[m];
            values[Energy] = thing.Energy;
            return values;
        }

        private static bool Valid(int sub, int level, int[] values)
        {
            var rule = WardrobeData.ArtRule(sub, level);
            if (rule == null || WardrobeData.ArtKind(sub) == null) return false;
            for (int k = 0; k < Count; k++)
                if (values[k] < 0 || values[k] > Limit(rule, k)) return false;
            return Spent(values) <= rule.Points;
        }

        internal static WardrobeThing Make(int sub, int level, int[] values)
        {
            var kind = WardrobeData.ArtKind(sub);
            var rule = WardrobeData.ArtRule(sub, level);
            var thing = new WardrobeThing
            {
                Art = true,
                Sub = sub,
                Rarity = Rarity,
                Level = level,
                ItemLevel = level,
                Name = "Артефакт: " + (kind != null ? kind.Name.ToLowerInvariant() : "?") + ", " + level + " ур.",
                Image = kind != null ? kind.Image : "",
                Energy = values[Energy]
            };
            for (int i = 0; i < 7; i++) thing.Bonus[i] = values[i];
            for (int p = 0; p < 5; p++) thing.Armor[p] = values[7 + p];
            for (int m = 0; m < 3; m++) thing.Magic[m] = values[12 + m];
            if (rule != null)
            {
                thing.DamageMin = rule.DamageMin;
                thing.DamageMax = rule.DamageMax;
                thing.Range = rule.Range;
            }
            return thing;
        }

        internal static string Code(WardrobeThing thing)
        {
            var text = new StringBuilder("@");
            text.Append(thing.Sub).Append('.').Append(thing.ItemLevel);
            foreach (int value in ValuesOf(thing)) text.Append('.').Append(value);
            return text.ToString();
        }

        internal static WardrobeThing Parse(string reference)
        {
            var bits = reference.Substring(1).Split('.');
            if (bits.Length != 2 + Count) return null;
            int sub, level;
            if (!int.TryParse(bits[0], out sub) || !int.TryParse(bits[1], out level)) return null;
            var values = new int[Count];
            for (int k = 0; k < Count; k++)
                if (!int.TryParse(bits[2 + k], out values[k])) return null;
            return Valid(sub, level, values) ? Make(sub, level, values) : null;
        }

        internal static WardrobeThing Recheck(WardrobeThing old)
        {
            var values = ValuesOf(old);
            return Valid(old.Sub, old.ItemLevel, values) ? Make(old.Sub, old.ItemLevel, values) : null;
        }

        internal static List<KeyValuePair<int, WardrobeThing>> FromGame(WardrobeState s, List<KeyValuePair<int, IGeneralThingInfoDescription>> found, int[] card)
        {
            var made = new List<KeyValuePair<int, WardrobeThing>>();
            if (found == null || found.Count == 0) return made;
            int[] rest = null;
            if (card != null)
            {
                rest = new int[5];
                var sub = s.Sub;
                for (int p = 0; p < 5; p++)
                {
                    int sum = card[p] - (sub != null ? sub.Armor[p] : 0);
                    foreach (var pair in s.Worn) if (pair.Value != null && !pair.Value.Art) sum -= pair.Value.Armor[p];
                    rest[p] = Math.Max(0, sum);
                }
            }
            foreach (var pair in found)
            {
                var about = pair.Value;
                var add = about.AddedParams;
                int kind = (int)about.ThingSubType;
                int level = about.Level ?? 0;
                var values = new int[Count];
                int average = 0;
                if (add != null)
                {
                    values[0] = add.Strength ?? 0;
                    values[1] = add.Dexterity ?? 0;
                    values[2] = add.Constitution ?? 0;
                    values[3] = add.Intelligence ?? 0;
                    values[4] = add.Wisdom ?? 0;
                    values[5] = add.Luck ?? 0;
                    values[6] = add.Reaction ?? 0;
                    values[12] = add.WhiteMagicProtection ?? 0;
                    values[13] = add.BlackMagicProtection ?? 0;
                    values[14] = add.AstralMagicProtection ?? 0;
                    values[Energy] = add.AddStamina ?? 0;
                    average = add.Armor ?? 0;
                }
                var rule = WardrobeData.ArtRule(kind, level);
                int armor = rule != null ? rule.Points - Spent(values) : average * 5;
                if (armor < 0 || Math.Abs((armor + 4) / 5 - average) > 1) armor = average * 5;
                var zones = Split(armor, rest, rule != null ? Limit(rule, 7) : Unlimited);
                for (int p = 0; p < 5; p++)
                {
                    values[7 + p] = zones[p];
                    if (rest != null) rest[p] = Math.Max(0, rest[p] - zones[p]);
                }
                made.Add(new KeyValuePair<int, WardrobeThing>(pair.Key, Make(kind, level, values)));
            }
            return made;
        }

        private static int[] Split(int total, int[] weights, int limit)
        {
            var result = new int[5];
            if (total <= 0) return result;
            var share = new double[5];
            double sum = 0;
            for (int p = 0; p < 5; p++)
            {
                share[p] = weights != null ? Math.Max(0, weights[p]) : 0;
                sum += share[p];
            }
            if (sum <= 0)
            {
                for (int p = 0; p < 5; p++) share[p] = 1;
                sum = 5;
            }
            int left = total;
            for (int p = 0; p < 5; p++)
            {
                result[p] = Math.Min(limit, (int)Math.Floor(total * share[p] / sum));
                left -= result[p];
            }
            while (left > 0)
            {
                int best = -1;
                double gap = double.MinValue;
                for (int p = 0; p < 5; p++)
                {
                    if (result[p] >= limit) continue;
                    double want = total * share[p] / sum - result[p];
                    if (want > gap) { gap = want; best = p; }
                }
                if (best < 0) break;
                result[best]++;
                left--;
            }
            return result;
        }

        internal static InputField Number(Transform parent, float width, int limit)
        {
            var input = OnlineWindow.MakeInput(parent, width, "0");
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = limit;
            WardrobeLook.Style(input);
            foreach (var text in new[] { input.textComponent, input.placeholder as Text })
            {
                if (text == null) continue;
                OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(3f, 0f), new Vector2(-3f, 0f));
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 15;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return input;
        }

        private static WardrobeArtKind Kind => _kind >= 0 && _kind < _kinds.Count ? _kinds[_kind] : null;
        private static int Sub => Kind != null ? Kind.Sub : 0;
        private static WardrobeArtRule Rule => WardrobeData.ArtRule(Sub, _level);
        private static int Target => _slot == 11 && !WardrobeData.Fits(11, Sub) ? 12 : _slot;

        private static List<int> Levels()
        {
            var list = new List<int>();
            foreach (int level in WardrobeData.ArtLevels(Sub)) if (level <= Wardrobe.S.Level) list.Add(level);
            return list;
        }

        internal static void Open(RectTransform host, int slot)
        {
            Close();
            if (host == null || !Prepare(slot)) return;
            Build(host);
            Paint();
        }

        internal static void Pop(RectTransform panel, int slot, float areaX, float areaY, float areaW, float areaH)
        {
            Close();
            if (panel == null || !Prepare(slot)) return;
            BuildCompact(panel, areaX, areaY, areaW, areaH);
            Paint();
        }

        private static bool Prepare(int slot)
        {
            _kinds = WardrobeData.ArtKinds(slot);
            if (_kinds.Count == 0) return false;
            _slot = slot;
            _kind = 0;
            _level = 0;
            for (int k = 0; k < Count; k++) Values[k] = 0;
            WardrobeThing worn;
            if (Wardrobe.S.Worn.TryGetValue(slot, out worn) && worn != null && worn.Art) Take(worn);
            else if (slot == 11 && Wardrobe.S.Worn.TryGetValue(12, out worn) && worn != null && worn.Art && WardrobeData.IsTwoHand(worn.Sub)) Take(worn);
            Fit();
            Squeeze();
            return true;
        }

        private static void Take(WardrobeThing worn)
        {
            int at = _kinds.FindIndex(kind => kind.Sub == worn.Sub);
            if (at < 0) return;
            _kind = at;
            _level = worn.ItemLevel;
            Array.Copy(ValuesOf(worn), Values, Count);
        }

        internal static void Close()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_shade != null) UnityEngine.Object.Destroy(_shade);
            _go = null;
            _shade = null;
            _scroll = null;
            _type = null;
            _levelText = null;
            _points = null;
            _gain = null;
            _limits = null;
            _wear = null;
            _wearAll = null;
            _reset = null;
            Rows.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_go == null) return false;
            Close();
            return true;
        }

        internal static void Refresh()
        {
            if (_go == null) return;
            Fit();
            Squeeze();
            Paint();
        }

        private static void Fit()
        {
            var levels = Levels();
            if (levels.Count == 0) return;
            if (levels.Contains(_level)) return;
            if (_level <= 0 || _level > levels[levels.Count - 1])
            {
                _level = levels[levels.Count - 1];
                return;
            }
            int best = levels[0];
            foreach (int level in levels) if (level <= _level) best = level;
            _level = best;
        }

        private static void Squeeze()
        {
            var rule = Rule;
            if (rule == null) return;
            for (int k = 0; k < Count; k++) Values[k] = Mathf.Clamp(Values[k], 0, Limit(rule, k));
            int over = Spent(Values) - rule.Points;
            for (int k = Count - 1; k >= 0 && over > 0; k--)
            {
                int cost = Cost(k);
                int cut = Math.Min(Values[k], (over + cost - 1) / cost);
                Values[k] -= cut;
                over -= cut * cost;
            }
        }

        private static void Set(int k, int want)
        {
            var rule = Rule;
            if (rule == null) return;
            int left = Math.Max(0, rule.Points - Spent(Values));
            int most = Math.Min(Limit(rule, k), Values[k] + left / Cost(k));
            Values[k] = Mathf.Clamp(want, 0, most);
            Paint();
        }

        private static void StepKind(int step)
        {
            if (_kinds.Count < 2) return;
            _kind = (_kind + step + _kinds.Count) % _kinds.Count;
            Fit();
            Squeeze();
            Paint();
        }

        private static void StepLevel(int step)
        {
            var levels = Levels();
            if (levels.Count == 0) return;
            int at = Mathf.Clamp(levels.IndexOf(_level) + step, 0, levels.Count - 1);
            _level = levels[at];
            Squeeze();
            Paint();
        }

        private static void Wear(bool all)
        {
            var rule = Rule;
            if (rule == null) return;
            int left = rule.Points - Spent(Values);
            if (left != 0)
            {
                Notice.Show("Распредели все очки артефакта: осталось " + left, 5f);
                return;
            }
            var values = new int[Count];
            Array.Copy(Values, values, Count);
            Wardrobe.Pick(_slot, Make(Sub, _level, values), all);
        }

        private static void Build(RectTransform host)
        {
            float width = host.rect.width > 10f ? host.rect.width : 440f;
            float height = host.rect.height > 10f ? host.rect.height : 680f;
            _go = new GameObject("QoLWardrobeArt", typeof(RectTransform), typeof(Image));
            _go.transform.SetParent(host, false);
            var rt = (RectTransform)_go.transform;
            OnlineWindow.Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var back = _go.GetComponent<Image>();
            back.color = WardrobeLook.Card;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;

            var title = OnlineWindow.Label(rt, "Артефакт: " + WardrobeData.SlotName(_slot).ToLowerInvariant(), 18, FontStyle.Bold, WardrobeLook.Bright);
            Wardrobe.At(title.rectTransform, 14f, 10f, width - 70f, 30f);
            title.alignment = TextAnchor.MiddleLeft;
            var close = Wardrobe.Arrow(rt, "×", Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 30f);
            crt.anchoredPosition = new Vector2(-10f, -10f);

            float y = 48f;
            if (_kinds.Count > 1)
            {
                _type = Cycle(rt, "Тип", y, width, StepKind);
                y += 38f;
            }
            _levelText = Cycle(rt, "Уровень", y, width, StepLevel);
            y += 42f;

            _points = OnlineWindow.Label(rt, "", 14, FontStyle.Bold, WardrobeLook.Accent);
            Wardrobe.At(_points.rectTransform, 14f, y, width - 128f, 24f);
            _points.alignment = TextAnchor.MiddleLeft;
            _points.horizontalOverflow = HorizontalWrapMode.Wrap;
            _points.resizeTextForBestFit = true;
            _points.resizeTextMinSize = 10;
            _points.resizeTextMaxSize = 14;
            _gain = OnlineWindow.Label(rt, "", 15, FontStyle.Bold, WardrobeLook.Label);
            Wardrobe.At(_gain.rectTransform, width - 114f, y, 100f, 24f);
            _gain.alignment = TextAnchor.MiddleRight;
            y += 26f;
            _limits = OnlineWindow.Label(rt, "", 13, FontStyle.Normal, WardrobeLook.Faint);
            Wardrobe.At(_limits.rectTransform, 14f, y, width - 28f, 22f);
            _limits.alignment = TextAnchor.MiddleLeft;
            y += 28f;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(rt, false);
            _scroll = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(_scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, Bottom), new Vector2(-8f, -y));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = _scroll;

            float rowW = width - 24f;
            foreach (int i in WardrobeData.StatOrder) MakeRow(content, rowW, i, WardrobeData.StatNames[i]);
            for (int p = 0; p < 5; p++) MakeRow(content, rowW, 7 + p, "Броня: " + WardrobeData.ArmorNames[p].ToLowerInvariant());
            for (int m = 0; m < 3; m++) MakeRow(content, rowW, 12 + m, "Защита: " + WardrobeData.MagicNames[m]);
            MakeRow(content, rowW, Energy, "Энергия");

            float by = height - Bottom + 10f;
            _wear = Wardrobe.GameButton(rt, "Надеть", () => Wear(false), false).GetComponent<Button>();
            Wardrobe.At((RectTransform)_wear.transform, 14f, by, 130f, 42f);
            var group = WardrobeData.Group(Target);
            if (group != null)
            {
                _wearAll = Wardrobe.GameButton(rt, "Надеть " + group.Length, () => Wear(true), false).GetComponent<Button>();
                Wardrobe.At((RectTransform)_wearAll.transform, 152f, by, 130f, 42f);
            }
            _reset = Wardrobe.GameButton(rt, "Обнулить", () =>
            {
                for (int k = 0; k < Count; k++) Values[k] = 0;
                Paint();
            }, true).GetComponent<Button>();
            Wardrobe.At((RectTransform)_reset.transform, width - 14f - 130f, by, 130f, 42f);
        }

        private static void BuildCompact(RectTransform panel, float areaX, float areaY, float areaW, float areaH)
        {
            const float Width = 580f;
            const float RowH = 32f;
            float listH = RowH * 9f;
            float height = 46f + (_kinds.Count > 1 ? 38f : 0f) + 42f + 26f + 26f + listH + 12f + 42f + 14f;

            _shade = new GameObject("QoLWardrobeArtShade", typeof(RectTransform), typeof(Image), typeof(Button));
            _shade.transform.SetParent(panel, false);
            OnlineWindow.Place((RectTransform)_shade.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dim = _shade.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.25f);
            _shade.GetComponent<Button>().onClick.AddListener(Close);

            _go = new GameObject("QoLWardrobeArt", typeof(RectTransform), typeof(Image), typeof(Outline));
            _go.transform.SetParent(panel, false);
            var rt = (RectTransform)_go.transform;
            Wardrobe.At(rt, areaX + Mathf.Max(0f, (areaW - Width) / 2f), areaY + Mathf.Max(0f, (areaH - height) / 2f), Width, height);
            var back = _go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var edge = _go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(rt, "Артефакт: " + WardrobeData.SlotName(_slot).ToLowerInvariant(), 18, FontStyle.Bold, WardrobeLook.Bright);
            Wardrobe.At(title.rectTransform, 14f, 8f, Width - 70f, 30f);
            title.alignment = TextAnchor.MiddleLeft;
            var close = Wardrobe.Arrow(rt, "×", Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 30f);
            crt.anchoredPosition = new Vector2(-10f, -8f);

            float y = 46f;
            if (_kinds.Count > 1)
            {
                _type = Cycle(rt, "Тип", y, Width, StepKind);
                y += 38f;
            }
            _levelText = Cycle(rt, "Уровень", y, Width, StepLevel);
            y += 42f;
            _points = OnlineWindow.Label(rt, "", 14, FontStyle.Bold, WardrobeLook.Accent);
            Wardrobe.At(_points.rectTransform, 14f, y, Width - 140f, 24f);
            _points.alignment = TextAnchor.MiddleLeft;
            _points.resizeTextForBestFit = true;
            _points.resizeTextMinSize = 10;
            _points.resizeTextMaxSize = 14;
            _gain = OnlineWindow.Label(rt, "", 15, FontStyle.Bold, WardrobeLook.Label);
            Wardrobe.At(_gain.rectTransform, Width - 124f, y, 110f, 24f);
            _gain.alignment = TextAnchor.MiddleRight;
            y += 26f;
            _limits = OnlineWindow.Label(rt, "", 13, FontStyle.Normal, WardrobeLook.Faint);
            Wardrobe.At(_limits.rectTransform, 14f, y, Width - 28f, 22f);
            _limits.alignment = TextAnchor.MiddleLeft;
            y += 26f;

            var body = new GameObject("params", typeof(RectTransform));
            body.transform.SetParent(rt, false);
            _scroll = (RectTransform)body.transform;
            Wardrobe.At(_scroll, 10f, y, Width - 20f, listH);
            float colW = (Width - 20f - 10f) / 2f;
            var leftCol = Column(_scroll, 0f, colW, listH);
            var rightCol = Column(_scroll, colW + 10f, colW, listH);
            foreach (int i in WardrobeData.StatOrder) MakeRow(leftCol, colW, i, WardrobeData.StatNames[i], true);
            for (int p = 0; p < 5; p++) MakeRow(rightCol, colW, 7 + p, "Броня: " + WardrobeData.ArmorNames[p].ToLowerInvariant(), true);
            for (int m = 0; m < 3; m++) MakeRow(rightCol, colW, 12 + m, "Защита: " + WardrobeData.MagicNames[m], true);
            MakeRow(rightCol, colW, Energy, "Энергия", true);
            y += listH + 12f;

            float bw = (Width - 28f - 3f * 8f) / 4f;
            int slot = _slot;
            _wear = Wardrobe.GameButton(rt, "Сохранить", () => Wear(false), false).GetComponent<Button>();
            Wardrobe.At((RectTransform)_wear.transform, 14f, y, bw, 42f);
            _reset = Wardrobe.GameButton(rt, "Обнулить", () =>
            {
                for (int k = 0; k < Count; k++) Values[k] = 0;
                Paint();
            }, false).GetComponent<Button>();
            Wardrobe.At((RectTransform)_reset.transform, 14f + (bw + 8f), y, bw, 42f);
            var other = Wardrobe.GameButton(rt, "Заменить", () => { Close(); Wardrobe.Replace(slot); }, false);
            Wardrobe.At(other, 14f + (bw + 8f) * 2f, y, bw, 42f);
            var off = Wardrobe.GameButton(rt, "Снять", () => Wardrobe.Pick(slot, null, false), true);
            Wardrobe.At(off, 14f + (bw + 8f) * 3f, y, bw, 42f);
        }

        private static RectTransform Column(RectTransform parent, float x, float w, float h)
        {
            var go = new GameObject("column", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Wardrobe.At(rt, x, 0f, w, h);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        private static void MakeRow(RectTransform content, float rowW, int k, string name, bool compact = false)
        {
            var go = new GameObject("param" + k, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(content, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 30f;
            var rt = (RectTransform)go.transform;

            float field = compact ? 64f : 84f;
            float plusX = rowW - 38f;
            float inputX = plusX - 4f - field;
            float minusX = inputX - 4f - 34f;
            float costX = compact ? minusX : minusX - 8f - 58f;

            var label = OnlineWindow.Label(rt, name, 14, FontStyle.Normal, WardrobeLook.Label);
            Wardrobe.At(label.rectTransform, 8f, 2f, costX - 12f, 26f);
            label.alignment = TextAnchor.MiddleLeft;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 14;
            if (!compact)
            {
                var cost = OnlineWindow.Label(rt, Cost(k) + " оч.", 11, FontStyle.Normal, WardrobeLook.Faint);
                Wardrobe.At(cost.rectTransform, costX, 2f, 58f, 26f);
                cost.alignment = TextAnchor.MiddleRight;
            }

            var minus = Wardrobe.Arrow(rt, "−", () => Set(k, Values[k] - 1));
            Wardrobe.At((RectTransform)minus.transform, minusX, 1f, 34f, 28f);
            var input = Number(rt, field, 6);
            Wardrobe.At((RectTransform)input.transform, inputX, 1f, field, 28f);
            input.onEndEdit.AddListener(value =>
            {
                int number;
                if (!int.TryParse(value, out number)) number = 0;
                Set(k, number);
            });
            var plus = Wardrobe.Arrow(rt, "+", () => Set(k, Values[k] + 1));
            Wardrobe.At((RectTransform)plus.transform, plusX, 1f, 34f, 28f);
            Rows.Add(new Row { K = k, Go = go, Input = input });
        }

        private static Text Cycle(RectTransform parent, string name, float y, float width, Action<int> step)
        {
            var label = OnlineWindow.Label(parent, name, 15, FontStyle.Normal, WardrobeLook.Label);
            Wardrobe.At(label.rectTransform, 14f, y, 90f, 34f);
            label.alignment = TextAnchor.MiddleLeft;
            float x = 104f;
            float right = width - 14f;
            var back = Wardrobe.Arrow(parent, "‹", () => step(-1));
            Wardrobe.At((RectTransform)back.transform, x, y + 2f, 34f, 30f);
            var next = Wardrobe.Arrow(parent, "›", () => step(1));
            Wardrobe.At((RectTransform)next.transform, right - 34f, y + 2f, 34f, 30f);
            var field = Wardrobe.Box(parent, name, x + 38f, y + 2f, right - 34f - 4f - (x + 38f), 30f, WardrobeLook.Field, 8);
            var value = OnlineWindow.Label(field, "", 15, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            value.alignment = TextAnchor.MiddleCenter;
            value.resizeTextForBestFit = true;
            value.resizeTextMinSize = 10;
            value.resizeTextMaxSize = 15;
            return value;
        }

        private static void Paint()
        {
            if (_go == null) return;
            var kind = Kind;
            var rule = Rule;
            var levels = Levels();
            bool ready = rule != null && levels.Contains(_level);
            if (_type != null) _type.text = kind != null ? kind.Name : "?";
            if (_levelText != null) _levelText.text = ready ? _level + " ур." : "—";
            if (_scroll != null) _scroll.gameObject.SetActive(ready);
            if (_reset != null) _reset.gameObject.SetActive(ready);
            if (!ready)
            {
                var all = WardrobeData.ArtLevels(Sub);
                _points.text = all.Count > 0 ? "Такой артефакт надевается с " + all[0] + " уровня" : "Для этого типа правил нет";
                _points.color = WardrobeLook.Bad;
                _gain.text = "";
                _limits.text = "";
                if (_wear != null) _wear.interactable = false;
                if (_wearAll != null) _wearAll.interactable = false;
                return;
            }

            int left = rule.Points - Spent(Values);
            _points.text = left > 0 ? "Очки распределения: " + left + ", надеть можно, когда потрачены все" : "Очки распределения: 0";
            _points.color = left > 0 ? WardrobeLook.Accent : WardrobeLook.Good;

            var probe = Wardrobe.S.Clone();
            var values = new int[Count];
            Array.Copy(Values, values, Count);
            probe.Wear(Target, Make(Sub, _level, values));
            int delta = probe.Rating - Wardrobe.S.Rating;
            _gain.text = "рейтинг " + (delta > 0 ? "+" + delta : delta.ToString());
            _gain.color = delta > 0 ? WardrobeLook.Good : delta < 0 ? WardrobeLook.Bad : WardrobeLook.Label;

            _limits.text = "Цена " + rule.Price + " " + Wardrobe.Plural(rule.Price, "кристалл", "кристалла", "кристаллов");

            foreach (var row in Rows)
            {
                bool on = row.K != Energy || HasEnergy(rule.Sub);
                if (row.Go.activeSelf != on) row.Go.SetActive(on);
                if (!on) continue;
                string value = Values[row.K].ToString();
                if (!row.Input.isFocused && row.Input.text != value) row.Input.text = value;
            }
            if (_wear != null) _wear.interactable = left == 0;
            if (_wearAll != null) _wearAll.interactable = left == 0;
        }
    }
}
