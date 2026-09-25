using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class TravelEdit
    {
        private static GameObject _canvasGo;
        private static InputField _input;
        private static Action<string> _done;
        private static Action _yes;

        internal static bool IsOpen => _canvasGo != null;

        internal static void Ask(string title, string start, Action<string> done)
        {
            Build(title, start, "Сохранить", true);
            _done = done;
        }

        internal static void Confirm(string title, string yesText, Action yes)
        {
            Build(title, null, yesText, false);
            _yes = yes;
        }

        private static void Build(string title, string start, string yesText, bool typed)
        {
            Close();
            var go = new GameObject("QoLTravelEdit", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 835;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);
            go.AddComponent<TravelEditTicker>();

            var shade = new GameObject("shade", typeof(RectTransform), typeof(Image), typeof(Button));
            shade.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)shade.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            shade.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            shade.GetComponent<Button>().onClick.AddListener(Close);

            var boxGo = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
            boxGo.transform.SetParent(go.transform, false);
            var box = (RectTransform)boxGo.transform;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(520f, typed ? 210f : 170f);
            var back = boxGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var edge = boxGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var label = OnlineWindow.Label(box, title, 17, FontStyle.Normal, WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(24f, typed ? 118f : 74f), new Vector2(-24f, -16f));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.alignment = TextAnchor.MiddleCenter;

            if (typed)
            {
                _input = OnlineWindow.MakeInput(box, 400f, "Название точки");
                var irt = (RectTransform)_input.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0f);
                irt.pivot = new Vector2(0.5f, 0f);
                irt.sizeDelta = new Vector2(420f, 36f);
                irt.anchoredPosition = new Vector2(0f, 74f);
                _input.characterLimit = 30;
                _input.onValidateInput = (text, index, c) => c == ',' || c == ';' || c == ':' || c == '@' || c == '>' || c == '\n' ? '\0' : c;
                WardrobeLook.Style(_input);
                _input.text = start ?? "";
                _input.ActivateInputField();
            }

            Button(box, yesText, -110f, Accept);
            Button(box, "Отмена", 110f, Close);
        }

        private static void Button(RectTransform box, string text, float x, Action click)
        {
            var go = OnlineWindow.MakeGameButton(box, text, 200f, 44f, click);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(200f, 44f);
            rt.anchoredPosition = new Vector2(x, 18f);
        }

        private static void Accept()
        {
            if (_yes != null)
            {
                var yes = _yes;
                Close();
                try { yes(); }
                catch (Exception e) { Plugin.Warn("[travel] подтверждение: " + e.Message); }
                return;
            }
            if (_input == null) return;
            string name = (_input.text ?? "").Trim();
            if (name.Length == 0)
            {
                Notice.Show("Впиши название точки", 3f);
                _input.ActivateInputField();
                return;
            }
            var done = _done;
            Close();
            try { done?.Invoke(name); }
            catch (Exception e) { Plugin.Warn("[travel] точка: " + e.Message); }
        }

        internal static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _input = null;
            _done = null;
            _yes = null;
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
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Accept();
        }
    }

    internal sealed class TravelEditTicker : MonoBehaviour
    {
        private void Update()
        {
            try { TravelEdit.Tick(); }
            catch (Exception e) { Plugin.Warn("[travel] окно точки: " + e.Message); }
        }
    }
}
