using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WardrobeElixirs
    {
        private const float W = 520f;
        private const float H = 420f;
        private const float RowW = W - 32f;
        private static readonly Color Green = new Color32(52, 128, 66, 255);

        private static readonly string[] Names =
        {
            "Эликсир силы", "Эликсир ловкости", "Эликсир сложения", "Эликсир интеллекта",
            "Эликсир мудрости", "Эликсир удачи", "Эликсир реакции"
        };

        private static GameObject _go;
        private static Text _info;
        private static readonly InputField[] Inputs = new InputField[7];
        private static readonly int[] KeptElix = new int[7];
        private static readonly int[] KeptDist = new int[7];

        internal static bool IsOpen => _go != null;

        internal static void Open()
        {
            Close();
            var panel = Wardrobe.Panel;
            if (panel == null) return;
            Array.Copy(Wardrobe.S.Elix, KeptElix, 7);
            Array.Copy(Wardrobe.S.Dist, KeptDist, 7);
            _go = new GameObject("QoLWardrobeElixirs", typeof(RectTransform), typeof(Image));
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

            var title = OnlineWindow.Label(box, "Эликсиры", 20, FontStyle.Bold, WardrobeLook.Bright);
            Wardrobe.At(title.rectTransform, 18f, 12f, W - 90f, 32f);
            title.alignment = TextAnchor.MiddleLeft;
            var close = Wardrobe.Arrow(box, "×", Cancel);
            Wardrobe.At((RectTransform)close.transform, W - 50f, 12f, 34f, 30f);

            _info = OnlineWindow.Label(box, "", 14, FontStyle.Normal, WardrobeLook.Label);
            Wardrobe.At(_info.rectTransform, 18f, 48f, W - 36f, 36f);
            _info.alignment = TextAnchor.MiddleLeft;
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;

            float y = 92f;
            for (int n = 0; n < 7; n++)
            {
                int i = WardrobeData.StatOrder[n];
                int stat = i;
                var row = Wardrobe.Box(box, "elix" + i, 16f, y, RowW, 34f, WardrobeLook.Stripe(n), 6);
                var name = OnlineWindow.Label(row, Names[i], 15, FontStyle.Normal, WardrobeLook.Label);
                Wardrobe.At(name.rectTransform, 10f, 0f, 220f, 34f);
                name.alignment = TextAnchor.MiddleLeft;
                var plus = Wardrobe.Arrow(row, "+", () => Set(stat, Wardrobe.S.Elix[stat] + 1));
                Wardrobe.At((RectTransform)plus.transform, RowW - 46f, 3f, 38f, 28f);
                Inputs[i] = WardrobeArt.Number(row, 80f, 4);
                Wardrobe.At((RectTransform)Inputs[i].transform, RowW - 46f - 6f - 80f, 3f, 80f, 28f);
                Inputs[i].onEndEdit.AddListener(value =>
                {
                    int number;
                    if (string.IsNullOrEmpty(value)) number = 0;
                    else if (!int.TryParse(value, out number)) { Refresh(); return; }
                    Set(stat, number);
                });
                var minus = Wardrobe.Arrow(row, "−", () => Set(stat, Wardrobe.S.Elix[stat] - 1));
                Wardrobe.At((RectTransform)minus.transform, RowW - 46f - 6f - 80f - 6f - 38f, 3f, 38f, 28f);
                y += 36f;
            }

            y += 14f;
            float bw = (W - 32f - 12f) / 2f;
            var done = Wardrobe.GameButton(box, "Готово", Close, false);
            Wardrobe.At(done, 16f, y, bw, 40f);
            done.GetComponent<Image>().color = Green;
            var label = done.GetComponentInChildren<Text>();
            if (label != null) label.color = Color.white;
            var reset = Wardrobe.GameButton(box, "Сбросить эликсиры", () => Brew(p => p.Sober()), true);
            Wardrobe.At(reset, 16f + bw + 12f, y, bw, 40f);

            Refresh();
        }

        internal static void Close()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _info = null;
            for (int i = 0; i < 7; i++) Inputs[i] = null;
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
            bool same = true;
            for (int i = 0; i < 7; i++) if (s.Elix[i] != KeptElix[i] || s.Dist[i] != KeptDist[i]) same = false;
            if (same) return;
            Array.Copy(KeptElix, s.Elix, 7);
            Array.Copy(KeptDist, s.Dist, 7);
            Wardrobe.Changed(true);
        }

        internal static string Caption()
        {
            int drunk = Wardrobe.S.Drunk;
            return drunk > 0 ? "Эликсиры +" + drunk : "Эликсиры";
        }

        internal static void Refresh()
        {
            if (_go == null) return;
            var s = Wardrobe.S;
            if (_info != null) _info.text = "Выпито " + s.Drunk + " из " + s.Cap + " на " + s.Level + " уровне, осталось " + s.Sips;
            for (int i = 0; i < 7; i++)
            {
                if (Inputs[i] == null || Inputs[i].isFocused) continue;
                string value = s.Elix[i].ToString();
                if (Inputs[i].text != value) Inputs[i].SetTextWithoutNotify(value);
            }
        }

        private static void Set(int i, int want)
        {
            var s = Wardrobe.S;
            if (want < 0) want = 0;
            int top = s.Elix[i] + s.Sips;
            if (want > top)
            {
                Notice.Show("Эликсиров не хватает: на " + s.Level + " уровне можно " + s.Cap + ", свободно ещё " + s.Sips, 4f);
                want = top;
            }
            if (want == s.Elix[i]) { Refresh(); return; }
            Brew(p => p.Elix[i] = want);
        }

        private static void Brew(Action<WardrobeState> change)
        {
            var s = Wardrobe.S;
            var probe = s.Clone();
            change(probe);
            var race = probe.Race;
            if (race == null) { Refresh(); return; }
            int extra = 0;
            for (int i = 0; i < 7; i++)
            {
                int need = Math.Max(0, probe.Need(i) - race.Base[i] - probe.Potion(i));
                if (probe.Dist[i] < need) { extra += need - probe.Dist[i]; probe.Dist[i] = need; }
            }
            if (probe.Spent > WardrobeState.Points(probe.Level))
            {
                Notice.Show("Не хватает свободных очков на требования подкласса: нужно ещё " + extra, 5f);
                Refresh();
                return;
            }
            foreach (var pair in s.Worn)
            {
                var thing = pair.Value;
                if (thing == null || !s.Fits(thing) || probe.Fits(thing)) continue;
                Notice.Show("Так «" + thing.Name + "» перестанет действовать — сначала сними её или добавь статов", 5f);
                Refresh();
                return;
            }
            Array.Copy(probe.Elix, s.Elix, 7);
            Array.Copy(probe.Dist, s.Dist, 7);
            Wardrobe.Changed(true);
        }
    }
}
