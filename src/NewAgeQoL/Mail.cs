using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Mail
    {
        private const string Mark = "newage-mail 1";
        private const float PanelW = 560f;
        private const float PanelH = 600f;
        private const float TopH = 34f;
        private const float InputH = 70f;
        private const float BubbleW = 400f;
        private const float Icon = 52f;
        private const float Every = 60f;
        private const float OpenEvery = 5f;
        private const float ShownEvery = 10f;
        private const int Most = 1000;
        private const long Fresh = 7L * 24L * 3600L;

        private sealed class Letter
        {
            internal int Id;
            internal bool Dev;
            internal long At;
            internal string Text;
        }

        private static ConfigEntry<string> _box;
        private static ConfigEntry<int> _seen;
        private static readonly List<Letter> Letters = new List<Letter>();
        private static int _last;
        private static int _told;
        private static int _wiped = -1;
        private static bool _loaded;
        private static bool _busy;
        private static bool _sending;
        private static float _askAt;

        private static GameObject _iconCanvas;
        private static RectTransform _iconRt;
        private static Image _iconBack;
        private static Image _iconPic;
        private static GameObject _badgeGo;
        private static Text _badge;
        private static Sprite _envelope;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static InputField _field;
        private static Text _state;
        private static int _drawn = -1;
        private static bool _drawnLoaded;

        private static void Bind()
        {
            var home = Plugin.Instance != null ? Plugin.Instance.Config : null;
            if (home == null) return;
            if (_box == null)
                _box = home.Bind("Report", "Box", "",
                    "Ящик для переписки с разработчиком мода. Заводится сам, по нему мод забирает письма разработчика.");
            if (_seen == null)
                _seen = home.Bind("Report", "MailSeen", 0, "Последнее прочитанное письмо разработчика.");
        }

        private static bool Valid(string box)
        {
            if (box == null || box.Length != 32) return false;
            foreach (char c in box)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }

        private static string Stored()
        {
            Bind();
            string box = _box != null ? (_box.Value ?? "").Trim() : "";
            return Valid(box) ? box : "";
        }

        internal static string Box
        {
            get
            {
                string box = Stored();
                if (box.Length > 0 || _box == null) return box;
                box = Guid.NewGuid().ToString("N");
                _box.Value = box;
                return box;
            }
        }

        internal static bool Any => Letters.Count > 0;

        internal static void Wake() => _askAt = 0f;

        private static string Where()
        {
            string url = Report.Address;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
            int at = url.LastIndexOf("/report", StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at + 7 != url.TrimEnd('/').Length) return "";
            return url.Substring(0, at) + "/mail";
        }

        private static int Unread()
        {
            int seen = _seen != null ? _seen.Value : 0;
            int n = 0;
            foreach (var one in Letters) if (one.Dev && one.Id > seen) n++;
            return n;
        }

        private static bool Recent()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var one in Letters) if (one.Dev && now - one.At < Fresh) return true;
            return false;
        }

        internal static void Tick()
        {
            try
            {
                if (!SideButtons.InWorld())
                {
                    DropIcon();
                    if (_panelGo != null) Close();
                    return;
                }
                string box = Box;
                if (box.Length == 0) { DropIcon(); return; }

                float now = Time.unscaledTime;
                if (!_busy && now >= _askAt && Plugin.Instance != null)
                {
                    string url = Where();
                    if (url.Length == 0) _askAt = now + Every;
                    else
                    {
                        _busy = true;
                        Plugin.Instance.StartCoroutine(Fetch(url, box));
                    }
                }

                if (_panelGo != null)
                {
                    Draw();
                    DropIcon();
                    return;
                }
                bool want = _loaded && !SideButtons.InCombat() && (Unread() > 0 || Recent());
                if (!want) { DropIcon(); return; }
                if (_iconCanvas == null)
                {
                    BuildIcon();
                    _askAt = Mathf.Min(_askAt, now + ShownEvery);
                }
                PlaceIcon();
                Blink(now);
            }
            catch (Exception e) { Plugin.Trace("[mail] " + e.Message); DropIcon(); }
        }

        private static IEnumerator Fetch(string url, string box)
        {
            string who = "";
            try { who = Controllers.User?.UserInfo?.Login ?? ""; }
            catch { }
            int read = _seen != null ? _seen.Value : 0;
            var req = UnityWebRequest.Get(url + "?box=" + box + "&after=" + _last + "&read=" + read + "&who=" + Uri.EscapeDataString(who));
            req.timeout = 15;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            long code = req.responseCode;
            req.Dispose();
            try
            {
                if (body.StartsWith(Mark, StringComparison.Ordinal)) Take(body);
                else Plugin.Trace("[mail] server answered " + code);
            }
            catch (Exception e) { Plugin.Trace("[mail] answer: " + e.Message); }
            _busy = false;
            _askAt = Time.unscaledTime + (_panelGo != null ? OpenEvery : _iconCanvas != null ? ShownEvery : Every);
        }

        private static void Take(string body)
        {
            bool first = !_loaded;
            _loaded = true;
            int fresh = 0;
            foreach (var raw in body.Split('\n'))
            {
                var bits = raw.TrimEnd('\r').Split(new[] { '\t' }, 5);
                if (bits.Length >= 2 && bits[0] == "c")
                {
                    int wiped;
                    if (int.TryParse(bits[1], out wiped))
                    {
                        if (_wiped >= 0 && wiped != _wiped) Wipe();
                        _wiped = wiped;
                    }
                    continue;
                }
                if (bits.Length < 5 || bits[0] != "m") continue;
                int id;
                long at;
                if (!int.TryParse(bits[1], out id) || id <= _last) continue;
                long.TryParse(bits[3], out at);
                var one = new Letter { Id = id, Dev = bits[2] == "dev", At = at, Text = Unflat(bits[4]) };
                Letters.Add(one);
                _last = id;
                if (one.Dev && id > _told) { _told = id; fresh++; }
            }
            if (_panelGo != null) { Read(); return; }
            int unread = Unread();
            if (fresh > 0 && unread > 0)
            {
                Plugin.Log?.LogInfo("[mail] letters from the developer: " + unread + " unread");
                Notice.Show(first ? "Есть письмо от naqol — конверт слева внизу" : "Новое письмо от naqol — конверт слева внизу", 6f);
            }
        }

        private static void Wipe()
        {
            Letters.Clear();
            _drawn = -1;
            Plugin.Trace("[mail] conversation cleared by the developer");
        }

        private static string Unflat(string text)
        {
            if (text.IndexOf('\\') < 0) return text;
            var box = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char next = text[++i];
                    box.Append(next == 'n' ? '\n' : next);
                }
                else box.Append(c);
            }
            return box.ToString();
        }

        private static void Read()
        {
            if (_seen == null) return;
            int top = _seen.Value;
            foreach (var one in Letters) if (one.Dev && one.Id > top) top = one.Id;
            if (top != _seen.Value) _seen.Value = top;
        }

        private static void BuildIcon()
        {
            _iconCanvas = new GameObject("QoLMailIcon", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Curtain.Stage(_iconCanvas);
            UnityEngine.Object.DontDestroyOnLoad(_iconCanvas);
            var canvas = _iconCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 252;
            var scaler = _iconCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var go = new GameObject("envelope", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_iconCanvas.transform, false);
            _iconRt = (RectTransform)go.transform;
            _iconRt.anchorMin = _iconRt.anchorMax = Vector2.zero;
            _iconRt.pivot = Vector2.zero;
            _iconRt.sizeDelta = new Vector2(Icon, Icon);
            _iconBack = go.GetComponent<Image>();
            _iconBack.sprite = OnlineWindow.Rounded(12);
            _iconBack.type = Image.Type.Sliced;
            _iconBack.color = WardrobeLook.Window;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Accent;
            edge.effectDistance = new Vector2(1f, -1f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = _iconBack;
            button.onClick.AddListener(Open);

            var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image));
            picGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)picGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 13f), new Vector2(-10f, -13f));
            _iconPic = picGo.GetComponent<Image>();
            _iconPic.sprite = Envelope();
            _iconPic.raycastTarget = false;
            _iconPic.color = WardrobeLook.Bright;

            _badgeGo = new GameObject("badge", typeof(RectTransform), typeof(Image));
            _badgeGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)_badgeGo.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(20f, 20f);
            brt.anchoredPosition = new Vector2(-4f, -4f);
            var bimg = _badgeGo.GetComponent<Image>();
            bimg.sprite = OnlineWindow.Rounded(10);
            bimg.type = Image.Type.Sliced;
            bimg.color = WardrobeLook.Danger;
            bimg.raycastTarget = false;
            _badge = OnlineWindow.Label(_badgeGo.transform, "", 12, FontStyle.Bold, WardrobeLook.DangerText);
            _badge.alignment = TextAnchor.MiddleCenter;
            _badge.raycastTarget = false;
            OnlineWindow.Place(_badge.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 1f));

            Plugin.Trace("[mail] envelope shown");
        }

        private static void PlaceIcon()
        {
            var area = (RectTransform)_iconCanvas.transform;
            float k = area.rect.height / Mathf.Max(1f, Screen.height);
            float x = 14f, y = 14f;
            if (ChatDock.Active && ChatDock.LeftPixels * k < Icon + 28f) y = ChatDock.PanelPixels * k + 10f;
            var at = new Vector2(x, y);
            if (_iconRt.anchoredPosition != at) _iconRt.anchoredPosition = at;
        }

        private static void Blink(float now)
        {
            int unread = Unread();
            bool on = unread > 0;
            if (_badgeGo.activeSelf != on) _badgeGo.SetActive(on);
            string count = unread > 9 ? "9+" : unread.ToString();
            if (on && _badge.text != count) _badge.text = count;
            float wave = on ? 0.5f + 0.5f * Mathf.Sin(now * 5f) : 0f;
            _iconBack.color = Color.Lerp(WardrobeLook.Window, WardrobeLook.Accent, wave);
            _iconPic.color = Color.Lerp(WardrobeLook.Bright, WardrobeLook.OnAccent, wave);
        }

        private static Sprite Envelope()
        {
            if (_envelope != null && _envelope.texture != null) return _envelope;
            const int w = 64, h = 46;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            const float line = 2.6f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float frame = Mathf.Min(Mathf.Min(fx, w - fx), Mathf.Min(fy, h - fy));
                float d = frame;
                float top = h - fy;
                float flap = Mathf.Abs(top - (w * 0.5f - Mathf.Abs(fx - w * 0.5f)) * (h * 0.62f) / (w * 0.5f));
                if (top <= h * 0.62f + line) d = Mathf.Min(d, flap * 0.8f);
                float a = Mathf.Clamp01(line - d + 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            _envelope = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            _envelope.hideFlags = HideFlags.HideAndDontSave;
            return _envelope;
        }

        private static void DropIcon()
        {
            if (_iconCanvas == null) return;
            UnityEngine.Object.Destroy(_iconCanvas);
            _iconCanvas = null;
            _iconRt = null;
            _iconBack = null;
            _iconPic = null;
            _badgeGo = null;
            _badge = null;
        }

        internal static void Open()
        {
            if (_panelGo != null) { Close(); return; }
            if (Box.Length == 0) return;
            try
            {
                try { Report.Close(); } catch { }
                Build();
                _drawn = -1;
                Draw();
                Read();
                Wake();
                Focus();
            }
            catch (Exception e) { Plugin.Warn("[mail] window: " + e.Message); Close(); }
        }

        internal static bool EscapeClose()
        {
            if (_panelGo == null) return false;
            Close();
            return true;
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
            _content = null;
            _scroll = null;
            _field = null;
            _state = null;
            _drawn = -1;
            _askAt = Mathf.Min(_askAt, Time.unscaledTime + Every);
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLMail", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var panel = (RectTransform)_panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelW, PanelH);
            panel.anchoredPosition = Vector2.zero;
            var back = _panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var edge = _panelGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -TopH), Vector2.zero);
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = panel;
            mover.Canvas = canvas;

            var title = OnlineWindow.Label(_panelGo.transform, "Переписка с naqol", 17, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(14f, -32f), new Vector2(-52f, -8f));
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;
            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, InputH + 46f), new Vector2(-14f, -44f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28f;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var column = contentGo.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(10, 10, 10, 10);
            column.spacing = 8f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            _scroll.viewport = srt;

            _field = Field(_panelGo.transform);
            OnlineWindow.Place((RectTransform)_field.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(14f, 16f), new Vector2(-150f, 16f + InputH));

            var send = Wardrobe.GameButton(_panelGo.transform, "Отправить", Send, false);
            OnlineWindow.Place(send, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-142f, 16f), new Vector2(-14f, 16f + InputH));
            send.GetComponent<Image>().color = WardrobeLook.Accent;
            var sendText = send.GetComponentInChildren<Text>();
            if (sendText != null) sendText.color = WardrobeLook.OnAccent;

            _state = OnlineWindow.Label(_panelGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Faint);
            _state.alignment = TextAnchor.MiddleLeft;
            _state.raycastTarget = false;
            OnlineWindow.Place(_state.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(16f, 18f + InputH), new Vector2(-14f, 40f + InputH));

            _panelGo.AddComponent<MailKeys>();
        }

        private static InputField Field(Transform host)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Field;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.FieldEdge;
            edge.effectDistance = new Vector2(1f, -1f);

            var text = OnlineWindow.Label(go.transform, "", 14, FontStyle.Normal, WardrobeLook.Bright);
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 6f), new Vector2(-10f, -6f));

            var ph = OnlineWindow.Label(go.transform, "Напиши ответ", 14, FontStyle.Normal, WardrobeLook.Faint);
            ph.alignment = TextAnchor.UpperLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Wrap;
            OnlineWindow.Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 6f), new Vector2(-10f, -6f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = back;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.MultiLineSubmit;
            input.characterLimit = Most;
            input.caretColor = WardrobeLook.Bright;
            input.customCaretColor = true;
            return input;
        }

        internal static void Focus()
        {
            if (_field == null) return;
            try
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_field.gameObject);
                _field.ActivateInputField();
            }
            catch (Exception e) { Plugin.Trace("[mail] input field: " + e.Message); }
        }

        private static void Draw()
        {
            if (_content == null || (_drawn == Letters.Count && _drawnLoaded == _loaded)) return;
            _drawn = Letters.Count;
            _drawnLoaded = _loaded;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            if (Letters.Count == 0)
            {
                var none = OnlineWindow.Label(_content, _loaded ? "Писем пока нет." : "Забираю письма…", 13, FontStyle.Normal, WardrobeLook.Faint);
                none.alignment = TextAnchor.MiddleCenter;
                none.raycastTarget = false;
                none.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
                return;
            }
            foreach (var one in Letters) Bubble(one);
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        private static void Bubble(Letter one)
        {
            var row = new GameObject("letter", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_content, false);
            var line = row.GetComponent<HorizontalLayoutGroup>();
            line.childAlignment = one.Dev ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;

            var cloud = new GameObject("bubble", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            cloud.transform.SetParent(row.transform, false);
            var back = cloud.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            back.color = one.Dev ? WardrobeLook.Card : WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.25f);
            back.raycastTarget = false;
            var stack = cloud.GetComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(12, 12, 7, 9);
            stack.spacing = 3f;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var head = OnlineWindow.Label(cloud.transform, (one.Dev ? "naqol" : "Я") + "  ·  " + When(one.At), 11, FontStyle.Bold,
                one.Dev ? WardrobeLook.Accent : WardrobeLook.Label);
            head.alignment = TextAnchor.MiddleLeft;
            head.raycastTarget = false;
            head.horizontalOverflow = HorizontalWrapMode.Overflow;

            var body = OnlineWindow.Label(cloud.transform, one.Text, 14, FontStyle.Normal, WardrobeLook.Bright);
            body.alignment = TextAnchor.UpperLeft;
            body.supportRichText = false;
            body.raycastTarget = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            float wide = Mathf.Max(head.preferredWidth, body.preferredWidth) + 2f;
            var size = body.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = Mathf.Min(wide, BubbleW);
        }

        private static string When(long at)
        {
            if (at <= 0) return "";
            var game = DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime.AddHours(3);
            var today = DateTime.UtcNow.AddHours(3).Date;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return game.Date == today ? game.ToString("HH:mm", inv) : game.ToString("dd.MM HH:mm", inv);
        }

        internal static void Send()
        {
            if (_field == null || _sending || Plugin.Instance == null) return;
            string text = (_field.text ?? "").Trim();
            if (text.Length == 0) { Focus(); return; }
            string url = Where();
            string box = Stored();
            if (url.Length == 0 || box.Length == 0) { Say("Отправлять некуда: приёмник отчётов не настроен."); return; }
            _sending = true;
            Say("Отправляю…");
            Plugin.Instance.StartCoroutine(Post(url, box, text));
        }

        private static IEnumerator Post(string url, string box, string text)
        {
            var form = new WWWForm();
            form.AddField("box", box, Encoding.UTF8);
            form.AddField("text", text, Encoding.UTF8);
            var web = UnityWebRequest.Post(url, form);
            web.timeout = 30;
            web.redirectLimit = 0;
            web.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return web.SendWebRequest();
            long code = web.responseCode;
            string answer = web.downloadHandler != null ? web.downloadHandler.text ?? "" : "";
            web.Dispose();
            _sending = false;
            bool taken = code >= 200 && code < 300 && answer.Replace(" ", "").Contains("\"ok\":true");
            if (taken)
            {
                Plugin.Log?.LogInfo("[mail] letter sent, " + text.Length + " chars");
                if (_field != null && (_field.text ?? "").Trim() == text) _field.text = "";
                Say("Ушло.");
                Wake();
                Focus();
                yield break;
            }
            Plugin.Warn("[mail] letter not sent: code " + code);
            Say(code == 429 ? "Слишком много писем за час, подожди немного."
                : code == 404 ? "Сервер ещё не знает этот ящик, попробуй через минуту."
                : "Не ушло (" + (code == 0 ? "нет связи" : code.ToString()) + "), попробуй ещё раз.");
        }

        private static void Say(string text)
        {
            if (_state != null)
            {
                _state.text = text;
                _state.color = WardrobeLook.Label;
            }
        }
    }

    internal sealed class MailKeys : MonoBehaviour
    {
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            var picked = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (picked == null || picked.transform.parent != transform || picked.GetComponent<InputField>() == null) return;
            Mail.Send();
        }
    }
}
