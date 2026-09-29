using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Notice
    {
        private const int Most = 5;

        private sealed class Plate
        {
            internal GameObject Go;
            internal CanvasGroup Veil;
            internal Text Text;
            internal float Until;
        }

        private static GameObject _canvasGo;
        private static RectTransform _stack;
        private static readonly List<Plate> Plates = new List<Plate>();

        internal static void Show(string text, float seconds)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                Build();
                if (_stack == null) return;
                float until = Time.unscaledTime + Mathf.Max(1f, seconds);
                foreach (var one in Plates)
                {
                    if (one.Text.text != text) continue;
                    one.Until = Mathf.Max(one.Until, until);
                    one.Veil.alpha = 1f;
                    return;
                }
                while (Plates.Count >= Most) Drop(Plates[0]);
                var plate = Make(text);
                plate.Until = until;
                Plates.Add(plate);
                _canvasGo.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[message] " + e.Message); }
        }

        internal static void Tick()
        {
            if (_canvasGo == null || !_canvasGo.activeSelf) return;
            float now = Time.unscaledTime;
            for (int i = Plates.Count - 1; i >= 0; i--)
            {
                var plate = Plates[i];
                float left = plate.Until - now;
                if (left <= 0f) { Drop(plate); continue; }
                plate.Veil.alpha = left < 0.6f ? left / 0.6f : 1f;
            }
            if (Plates.Count == 0) _canvasGo.SetActive(false);
        }

        private static void Drop(Plate plate)
        {
            Plates.Remove(plate);
            if (plate.Go != null) UnityEngine.Object.Destroy(plate.Go);
        }

        private static Plate Make(string text)
        {
            var go = new GameObject("plate", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(HorizontalLayoutGroup), typeof(CanvasGroup));
            go.transform.SetParent(_stack, false);

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(12);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Accent;
            edge.effectDistance = new Vector2(2f, -2f);

            var box = go.GetComponent<HorizontalLayoutGroup>();
            box.padding = new RectOffset(18, 18, 10, 12);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = false;
            box.childForceExpandHeight = false;

            var veil = go.GetComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;

            var label = OnlineWindow.Label(go.transform, text, 18, FontStyle.Bold, WardrobeLook.Bright);
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 620f;

            return new Plate { Go = go, Veil = veil, Text = label };
        }

        private static void Build()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject("QoLNotice", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);
            var group = _canvasGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var stackGo = new GameObject("stack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            stackGo.transform.SetParent(_canvasGo.transform, false);
            _stack = (RectTransform)stackGo.transform;
            _stack.anchorMin = _stack.anchorMax = new Vector2(0.5f, 1f);
            _stack.pivot = new Vector2(0.5f, 1f);
            _stack.anchoredPosition = new Vector2(0f, -150f);

            var column = stackGo.GetComponent<VerticalLayoutGroup>();
            column.spacing = 6f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fit = stackGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _canvasGo.SetActive(false);
        }
    }
}
