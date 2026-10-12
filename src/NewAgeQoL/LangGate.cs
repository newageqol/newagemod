using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class LangGate
    {
        private static GameObject _root;
        private static GameObject _cards;
        private static GameObject _wait;
        private static GameObject _go;
        private static GameObject _barBox;
        private static RectTransform _bar;
        private static Text _status;
        private static Action _next;
        private static bool _busy;

        internal static bool Broken { get; private set; }

        internal static bool Open => _next != null;

        internal static bool Hold(Action next)
        {
            try
            {
                Build();
                _next = next ?? (() => { });
                Mode(true, false, false);
                Plugin.Log?.LogInfo("[lang] asking the player for the game language");
                return true;
            }
            catch (Exception e)
            {
                Broken = true;
                _next = null;
                if (_root != null) _root.SetActive(false);
                Plugin.Warn("[lang] language dialog: " + e.Message);
                return false;
            }
        }

        internal static bool Refresh(Action next)
        {
            if (Plugin.Instance == null) return false;
            Lang.Synced = true;
            _next = next ?? (() => { });
            _busy = true;
            Plugin.Instance.StartCoroutine(Sync());
            return true;
        }

        private static void Pick(string code)
        {
            if (_next == null || _busy) return;
            try { Lang.Choose(code); }
            catch (Exception e) { Plugin.Warn("[lang] choosing " + code + ": " + e.Message); }
            if (code == "uk" && Plugin.Instance != null)
            {
                Lang.Synced = true;
                _busy = true;
                Say("Перевіряю український переклад…", -1f);
                Plugin.Instance.StartCoroutine(Sync());
                return;
            }
            Finish();
        }

        private static IEnumerator Sync()
        {
            int result = AssetSync.Missed;
            yield return AssetSync.Refresh(Lang.Asset, Progress, r => result = r);
            if (result == AssetSync.Updated) Lang.Reload();
            Plugin.Log?.LogInfo("[lang] Ukrainian translation " + (result == AssetSync.Current ? "is current" : result == AssetSync.Updated ? "updated" : Lang.Present ? "not checked, the downloaded one is used" : "is missing"));
            if (result == AssetSync.Missed && !Lang.Present)
            {
                Failed();
                yield break;
            }
            Finish();
        }

        private static void Finish()
        {
            var next = _next;
            _next = null;
            _busy = false;
            if (_root != null) _root.SetActive(false);
            if (next != null) ResourceStrings.InitializeExternalStringResources(next);
        }

        private static void Progress(long got, long total)
        {
            try
            {
                Build();
                Say(string.Format("Завантажую український переклад…\n{0} з {1} МБ", Mb(got), Mb(total)), total > 0 ? Mathf.Clamp01(got / (float)total) : 0f);
            }
            catch (Exception e) { Plugin.Trace("[lang] download window: " + e.Message); }
        }

        private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("uk-UA"));

        private static void Say(string text, float share)
        {
            Build();
            _status.text = text;
            _barBox.SetActive(share >= 0f);
            if (share >= 0f) _bar.anchorMax = new Vector2(share, 1f);
            Mode(false, true, false);
        }

        private static void Failed()
        {
            try
            {
                Build();
                _status.text = "Не вдалося завантажити український переклад.\nГра запуститься російською, спробуємо ще раз під час наступного запуску.";
                _barBox.SetActive(false);
                Mode(false, true, true);
            }
            catch (Exception e)
            {
                Plugin.Warn("[lang] download failed, dialog: " + e.Message);
                Finish();
            }
        }

        private static void Mode(bool cards, bool wait, bool go)
        {
            _cards.SetActive(cards);
            _wait.SetActive(wait);
            _go.SetActive(go);
            _root.SetActive(true);
        }

        private static void Build()
        {
            if (_root != null) return;
            _root = new GameObject("QoLLangGate", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32001;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var veil = new GameObject("veil", typeof(RectTransform), typeof(Image));
            veil.transform.SetParent(_root.transform, false);
            var vrt = (RectTransform)veil.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            veil.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var box = new GameObject("box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(_root.transform, false);
            var brt = (RectTransform)box.transform;
            brt.sizeDelta = new Vector2(Side * 3f + Gap * 2f + Pad * 2f, 300f);
            var bimg = box.GetComponent<Image>();
            bimg.color = WardrobeLook.Window;
            bimg.sprite = OnlineWindow.Rounded(16);
            bimg.type = Image.Type.Sliced;
            WardrobeLook.Frame(box, WardrobeLook.Edge);

            var head = OnlineWindow.Label(box.transform, "Мова  ·  Язык  ·  Language", 15, FontStyle.Bold, WardrobeLook.Accent);
            var hrt = head.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(0f, 24f);
            hrt.anchoredPosition = new Vector2(0f, -26f);

            var line = new GameObject("line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(box.transform, false);
            var lrt = (RectTransform)line.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(48f, 2f);
            lrt.anchoredPosition = new Vector2(0f, -58f);
            line.GetComponent<Image>().color = WardrobeLook.Accent;

            _cards = Layer(box.transform);
            Card(_cards.transform, -(Side + Gap), "uk", "Українська", "Оберіть мову гри", "Змінити можна в налаштуваннях гри");
            Card(_cards.transform, 0f, "ru", "Русский", "Выберите язык игры", "Изменить можно в настройках игры");
            Card(_cards.transform, Side + Gap, "en", "English", "Choose the game language", "You can change it in the game settings");

            _wait = Layer(box.transform);
            _status = OnlineWindow.Label(_wait.transform, "", 18, FontStyle.Normal, WardrobeLook.Bright);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            var srt = _status.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(Pad, 110f);
            srt.offsetMax = new Vector2(-Pad, -76f);

            _barBox = new GameObject("bar", typeof(RectTransform), typeof(Image));
            _barBox.transform.SetParent(_wait.transform, false);
            var bb = (RectTransform)_barBox.transform;
            bb.anchorMin = bb.anchorMax = new Vector2(0.5f, 0f);
            bb.pivot = new Vector2(0.5f, 0f);
            bb.sizeDelta = new Vector2(520f, 14f);
            bb.anchoredPosition = new Vector2(0f, 64f);
            var bbi = _barBox.GetComponent<Image>();
            bbi.color = WardrobeLook.Field;
            bbi.sprite = OnlineWindow.Rounded(6);
            bbi.type = Image.Type.Sliced;
            var fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(_barBox.transform, false);
            _bar = (RectTransform)fill.transform;
            _bar.anchorMin = Vector2.zero;
            _bar.anchorMax = new Vector2(0f, 1f);
            _bar.offsetMin = _bar.offsetMax = Vector2.zero;
            var fi = fill.GetComponent<Image>();
            fi.color = WardrobeLook.Accent;
            fi.sprite = OnlineWindow.Rounded(6);
            fi.type = Image.Type.Sliced;

            var go = OnlineWindow.MakeGameButton(_wait.transform, "Продовжити", 240f, 46f, Finish);
            _go = go.gameObject;
            var grt = (RectTransform)go.transform;
            var le = go.GetComponent<LayoutElement>();
            if (le != null) grt.sizeDelta = new Vector2(le.preferredWidth, le.preferredHeight);
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0f);
            grt.pivot = new Vector2(0.5f, 0f);
            grt.anchoredPosition = new Vector2(0f, Pad);
            _root.SetActive(false);
        }

        private static GameObject Layer(Transform host)
        {
            var go = new GameObject("layer", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private const float Side = 250f;
        private const float Gap = 18f;
        private const float Pad = 32f;

        private static void Card(Transform host, float x, string code, string name, string ask, string later)
        {
            var go = new GameObject("lang_" + code, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(Side, 176f);
            rt.anchoredPosition = new Vector2(x, Pad);
            var img = go.GetComponent<Image>();
            img.color = Color.white;
            img.sprite = OnlineWindow.Rounded(10);
            img.type = Image.Type.Sliced;
            WardrobeLook.Frame(go, WardrobeLook.Edge);

            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            var tint = button.colors;
            tint.normalColor = WardrobeLook.Card;
            tint.selectedColor = WardrobeLook.Card;
            tint.highlightedColor = WardrobeLook.Mix(WardrobeLook.Card, WardrobeLook.Accent, 0.2f);
            tint.pressedColor = WardrobeLook.Mix(WardrobeLook.Card, WardrobeLook.Accent, 0.36f);
            tint.colorMultiplier = 1f;
            tint.fadeDuration = 0.08f;
            button.colors = tint;
            button.onClick.AddListener(() => Pick(code));

            Put(OnlineWindow.Label(go.transform, name, 24, FontStyle.Bold, WardrobeLook.Bright), 30f, 36f);

            var mark = new GameObject("mark", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(go.transform, false);
            var mrt = (RectTransform)mark.transform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 1f);
            mrt.pivot = new Vector2(0.5f, 1f);
            mrt.sizeDelta = new Vector2(28f, 2f);
            mrt.anchoredPosition = new Vector2(0f, -78f);
            var mimg = mark.GetComponent<Image>();
            mimg.color = WardrobeLook.Accent;
            mimg.raycastTarget = false;

            Put(OnlineWindow.Label(go.transform, ask, 15, FontStyle.Normal, WardrobeLook.Body), 94f, 22f);
            Put(OnlineWindow.Label(go.transform, later, 12, FontStyle.Normal, WardrobeLook.Faint), 122f, 36f);
        }

        private static void Put(Text text, float top, float height)
        {
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperCenter;
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-28f, height);
            rt.anchoredPosition = new Vector2(0f, -top);
        }
    }
}
