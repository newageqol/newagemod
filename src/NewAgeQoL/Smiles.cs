using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Smiles
    {
        private const string Sheet = "NewAgeSmiles";
        private const int Columns = 6;
        private const float CellSide = 26f;
        private const float CellRoom = 26f;
        private const float PanelPad = 6f;
        private const float TitleHigh = 18f;
        private const string Caption = "Смайлики";
        private const float Zoom = 1.5f;
        private const float Lift = 0.78f;
        private const float Side = 0.06f;

        private sealed class Face
        {
            internal string Clip;
            internal string Code;
            internal int Width;
            internal int Height;
            internal int Start;
            internal int Count;
            internal string Tag;
            internal int[] SpotX;
            internal int[] SpotY;
        }

        private static readonly List<Face> Faces = new List<Face>();
        private static readonly Dictionary<char, List<Face>> Heads = new Dictionary<char, List<Face>>();
        private static Texture2D _sheet;
        private static TMP_SpriteAsset _asset;
        private static int _rate = 25;
        private static int _atlasW;
        private static int _atlasH;
        private static bool _tried;
        private static bool _ready;
        private static Sprite _icon;

        private static GameObject _panel;
        private static RectTransform _panelRt;
        private static RectTransform _near;
        private static Canvas _host;
        private static Text _title;
        private static readonly List<RawImage> _cells = new List<RawImage>();
        private static readonly List<Face> _cellFaces = new List<Face>();
        private static float _openedAt;
        private static int _lastBeat = -1;

        internal static bool Ready => _ready;

        internal static bool Open => _panel != null;

        internal static void Tick()
        {
            Prepare();
            if (_panel == null) return;
            if (!ChatDock.Active) { Close(); return; }
            Roll();
            Outside();
        }

        internal static bool EscapeClose()
        {
            if (_panel == null) return false;
            Close();
            return true;
        }

        internal static void Prepare()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                byte[] art = Grab("smiles.png");
                string list = null;
                byte[] raw = Grab("smiles.txt");
                if (raw != null) list = new UTF8Encoding(false).GetString(raw);
                if (art == null || list == null) { Plugin.Trace("[смайлики] нет картинки или описи"); return; }
                if (!Read(list)) return;

                _sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _sheet.wrapMode = TextureWrapMode.Clamp;
                _sheet.filterMode = FilterMode.Bilinear;
                _sheet.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(_sheet, art)) { Plugin.Trace("[смайлики] картинка не прочиталась"); return; }

                Build();
                _ready = true;
                Plugin.Trace("[смайлики] готово: " + Faces.Count + " штук, кадров " + _asset.spriteCharacterTable.Count);
            }
            catch (Exception e) { Plugin.Fault("[смайлики] подготовка: " + e.Message); }
        }

        private static byte[] Grab(string tail)
        {
            var asm = Assembly.GetExecutingAssembly();
            string found = null;
            foreach (var one in asm.GetManifestResourceNames())
                if (one.EndsWith("." + tail, StringComparison.OrdinalIgnoreCase)) { found = one; break; }
            if (found == null) return null;
            using (var stream = asm.GetManifestResourceStream(found))
            using (var box = new MemoryStream())
            {
                stream.CopyTo(box);
                return box.ToArray();
            }
        }

        private static bool Read(string list)
        {
            Faces.Clear();
            Heads.Clear();
            foreach (string row in list.Split('\n'))
            {
                string line = row.Trim('\r', ' ');
                if (line.Length == 0) continue;
                var parts = line.Split(' ');
                if (parts[0] == "atlas")
                {
                    if (parts.Length < 4) return false;
                    _atlasW = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    _atlasH = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    _rate = Math.Max(1, (int)Math.Round(double.Parse(parts[3], CultureInfo.InvariantCulture)));
                    continue;
                }
                if (parts.Length < 7) continue;
                var face = new Face
                {
                    Clip = parts[0],
                    Code = parts[1],
                    Width = int.Parse(parts[2], CultureInfo.InvariantCulture),
                    Height = int.Parse(parts[3], CultureInfo.InvariantCulture),
                    Start = int.Parse(parts[4], CultureInfo.InvariantCulture),
                    Count = int.Parse(parts[5], CultureInfo.InvariantCulture),
                };
                if (face.Count <= 0 || parts.Length - 6 < face.Count) return false;
                face.SpotX = new int[face.Count];
                face.SpotY = new int[face.Count];
                for (int i = 0; i < face.Count; i++)
                {
                    var pair = parts[6 + i].Split(',');
                    if (pair.Length != 2) return false;
                    face.SpotX[i] = int.Parse(pair[0], CultureInfo.InvariantCulture);
                    face.SpotY[i] = int.Parse(pair[1], CultureInfo.InvariantCulture);
                }
                face.Tag = face.Count > 1
                    ? "<sprite=\"" + Sheet + "\" anim=\"" + face.Start + "," + (face.Start + face.Count - 1) + "," + _rate + "\">"
                    : "<sprite=\"" + Sheet + "\" index=" + face.Start + ">";
                Faces.Add(face);
            }
            if (Faces.Count == 0 || _atlasW <= 0 || _atlasH <= 0) return false;
            foreach (var face in Faces)
            {
                List<Face> bucket;
                if (!Heads.TryGetValue(face.Code[0], out bucket)) Heads[face.Code[0]] = bucket = new List<Face>();
                bucket.Add(face);
            }
            foreach (var bucket in Heads.Values)
                bucket.Sort((a, b) => b.Code.Length.CompareTo(a.Code.Length));
            return true;
        }

        private static void Build()
        {
            _asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            _asset.name = Sheet;
            _asset.hideFlags = HideFlags.HideAndDontSave;
            _asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(Sheet);
            _asset.spriteSheet = _sheet;
            var stamp = AccessTools.Field(typeof(TMP_SpriteAsset), "m_Version");
            if (stamp != null) stamp.SetValue(_asset, "1.1.0");
            ShaderUtilities.GetShaderPropertyIDs();
            var paint = Paint();
            if (paint == null) throw new Exception("не нашёлся материал для картинок в тексте");
            paint.name = Sheet + " Material";
            paint.hideFlags = HideFlags.HideAndDontSave;
            paint.SetTexture(ShaderUtilities.ID_MainTex, _sheet);
            _asset.material = paint;
            _asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(paint.name);
            _asset.spriteInfoList = new List<TMP_Sprite>();
            _asset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();

            uint slot = 0;
            foreach (var face in Faces)
                for (int i = 0; i < face.Count; i++)
                {
                    var metrics = new GlyphMetrics(face.Width, face.Height, face.Width * Side,
                        face.Height * Lift, face.Width * (1f + Side * 2f));
                    var rect = new GlyphRect(face.SpotX[i], face.SpotY[i], face.Width, face.Height);
                    var glyph = new TMP_SpriteGlyph(slot, metrics, rect, 1f, 0);
                    var letter = new TMP_SpriteCharacter(0xFFFE, _asset, glyph);
                    letter.name = face.Clip + "_" + i;
                    letter.scale = Zoom;
                    _asset.spriteGlyphTable.Add(glyph);
                    _asset.spriteCharacterTable.Add(letter);
                    slot++;
                }
            _asset.UpdateLookupTables();
            MaterialReferenceManager.AddSpriteAsset(_asset.hashCode, _asset);
            TMP_SpriteAsset back;
            if (!MaterialReferenceManager.TryGetSpriteAsset(_asset.hashCode, out back) || !ReferenceEquals(back, _asset))
                throw new Exception("лист смайликов не записался в справочник TextMeshPro");
            if (_asset.spriteCharacterTable.Count != (int)slot)
                throw new Exception("кадров в листе " + _asset.spriteCharacterTable.Count + ", ждали " + slot);
        }

        private static Material Paint()
        {
            var known = TMP_Settings.defaultSpriteAsset;
            if (known != null && known.material != null) return new Material(known.material);
            var shader = Shader.Find("TextMeshPro/Sprite");
            return shader != null ? new Material(shader) : null;
        }

        internal static string Dress(string text)
        {
            if (!_ready || string.IsNullOrEmpty(text)) return text;
            StringBuilder box = null;
            int at = 0, kept = 0;
            while (at < text.Length)
            {
                if (text[at] == '<')
                {
                    int shut = text.IndexOf('>', at + 1);
                    at = shut > at ? shut + 1 : at + 1;
                    continue;
                }
                var face = Hit(text, at);
                if (face == null) { at++; continue; }
                if (box == null) box = new StringBuilder(text.Length + 96);
                box.Append(text, kept, at - kept);
                box.Append(face.Tag);
                at += face.Code.Length;
                kept = at;
            }
            if (box == null) return text;
            box.Append(text, kept, text.Length - kept);
            return box.ToString();
        }

        private static Face Hit(string text, int at)
        {
            List<Face> bucket;
            if (!Heads.TryGetValue(text[at], out bucket)) return null;
            for (int i = 0; i < bucket.Count; i++)
            {
                var face = bucket[i];
                if (at + face.Code.Length > text.Length) continue;
                if (string.CompareOrdinal(text, at, face.Code, 0, face.Code.Length) == 0) return face;
            }
            return null;
        }

        internal static Sprite Icon()
        {
            if (_icon != null) return _icon;
            Prepare();
            if (!_ready) return Icons.Head();
            var face = Faces.Find(one => one.Clip == "smile4") ?? Faces[0];
            _icon = Sprite.Create(_sheet, new Rect(face.SpotX[0], face.SpotY[0], face.Width, face.Height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _icon;
        }

        internal static void Toggle(RectTransform near)
        {
            if (_panel != null) { Close(); return; }
            Prepare();
            if (!_ready) return;
            try { Show(near); }
            catch (Exception e) { Plugin.Fault("[смайлики] окно: " + e.Message); Close(); }
        }

        internal static void Close()
        {
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
            _panel = null;
            _panelRt = null;
            _near = null;
            _host = null;
            _title = null;
            _cells.Clear();
            _cellFaces.Clear();
            _lastBeat = -1;
        }

        private static void Show(RectTransform near)
        {
            _near = near;
            _host = near != null ? near.GetComponentInParent<Canvas>() : null;
            if (_host == null) return;
            var deck = (RectTransform)_host.transform;

            int rows = (Faces.Count + Columns - 1) / Columns;
            float wide = Columns * CellSide + PanelPad * 2f;
            float high = rows * CellSide + PanelPad * 2f + TitleHigh;

            _panel = new GameObject("QoLSmiles", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panel.transform.SetParent(deck, false);
            _panelRt = (RectTransform)_panel.transform;
            _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRt.pivot = new Vector2(0.5f, 0f);
            _panelRt.sizeDelta = new Vector2(wide, high);
            var lift = _panel.AddComponent<Canvas>();
            _panel.AddComponent<GraphicRaycaster>();
            lift.overrideSorting = true;
            lift.sortingOrder = _host.rootCanvas.sortingOrder + 60;
            var back = _panel.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            WardrobeLook.Frame(_panel, WardrobeLook.Edge);

            _title = OnlineWindow.Label(_panel.transform, Caption, 12, FontStyle.Bold, WardrobeLook.Label);
            var title = _title;
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(PanelPad, -(TitleHigh + 2f)), new Vector2(-PanelPad, -2f));
            title.alignment = TextAnchor.MiddleCenter;
            title.raycastTarget = false;

            var grid = new GameObject("grid", typeof(RectTransform), typeof(GridLayoutGroup));
            grid.transform.SetParent(_panel.transform, false);
            OnlineWindow.Place((RectTransform)grid.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(PanelPad, PanelPad), new Vector2(-PanelPad, -(PanelPad + TitleHigh)));
            var cells = grid.GetComponent<GridLayoutGroup>();
            cells.cellSize = new Vector2(CellSide, CellSide);
            cells.spacing = Vector2.zero;
            cells.padding = new RectOffset(0, 0, 0, 0);
            cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cells.constraintCount = Columns;
            cells.childAlignment = TextAnchor.UpperLeft;

            foreach (var face in Faces) Cell(grid.transform, face);

            Aim(deck, near);
            _openedAt = Time.unscaledTime;
            Roll();
        }

        private static void Cell(Transform host, Face face)
        {
            var go = new GameObject(face.Clip, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.color = new Color(1f, 1f, 1f, 0f);
            var hover = go.AddComponent<HoverWatch>();
            hover.OnEnter = () =>
            {
                if (back != null) back.color = new Color(1f, 1f, 1f, 0.08f);
                if (_title != null) _title.text = face.Code;
            };
            hover.OnExit = () =>
            {
                if (back != null) back.color = new Color(1f, 1f, 1f, 0f);
                if (_title != null) _title.text = Caption;
            };
            var chosen = face;
            go.GetComponent<Button>().onClick.AddListener(() => Pick(chosen));

            float fit = Mathf.Min(1f, CellRoom / Mathf.Max(face.Width, face.Height));
            var shotGo = new GameObject("face", typeof(RectTransform), typeof(RawImage));
            shotGo.transform.SetParent(go.transform, false);
            var srt = (RectTransform)shotGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(face.Width * fit, face.Height * fit);
            srt.anchoredPosition = Vector2.zero;
            var shot = shotGo.GetComponent<RawImage>();
            shot.texture = _sheet;
            shot.raycastTarget = false;
            shot.uvRect = Window(face, 0);
            _cells.Add(shot);
            _cellFaces.Add(face);
        }

        private static Rect Window(Face face, int frame)
        {
            int i = face.Count > 0 ? frame % face.Count : 0;
            return new Rect(face.SpotX[i] / (float)_atlasW, face.SpotY[i] / (float)_atlasH,
                face.Width / (float)_atlasW, face.Height / (float)_atlasH);
        }

        private static void Roll()
        {
            int beat = (int)(Time.unscaledTime * _rate);
            if (beat == _lastBeat) return;
            _lastBeat = beat;
            for (int i = 0; i < _cells.Count; i++)
            {
                var shot = _cells[i];
                if (shot == null) continue;
                shot.uvRect = Window(_cellFaces[i], beat);
            }
        }

        private static void Aim(RectTransform deck, RectTransform near)
        {
            var top = near.TransformPoint(new Vector3(near.rect.center.x, near.rect.yMax, 0f));
            var spot = deck.InverseTransformPoint(top);
            float half = _panelRt.sizeDelta.x * 0.5f;
            float edge = deck.rect.width * 0.5f - 4f;
            float x = Mathf.Clamp(spot.x, -edge + half, edge - half);
            _panelRt.anchoredPosition = new Vector2(x, spot.y + 6f);
        }

        private static void Outside()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (Time.unscaledTime - _openedAt < 0.2f) return;
            var eye = _host != null && _host.renderMode != RenderMode.ScreenSpaceOverlay ? _host.worldCamera : null;
            if (RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, eye)) return;
            if (_near != null && RectTransformUtility.RectangleContainsScreenPoint(_near, Input.mousePosition, eye)) return;
            Close();
        }

        private static void Pick(Face face)
        {
            ChatDock.Type(face.Code);
            Close();
        }
    }

    [HarmonyPatch(typeof(TextMeshProUGUI), "ClearMesh")]
    public static class ChatSmilesGhostPatch
    {
        private static void Postfix(TextMeshProUGUI __instance)
        {
            try
            {
                var run = __instance != null ? __instance.GetComponent<TMP_SpriteAnimator>() : null;
                if (run != null) run.StopAllAnimations();
            }
            catch (Exception e) { Plugin.Trace("[смайлики] пустой текст: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChatContent), "ReplaceSmiles")]
    public static class ChatSmilesPatch
    {
        private static bool Prefix(string str, ref string __result)
        {
            if (!Smiles.Ready) return true;
            try { __result = Smiles.Dress(str); }
            catch (Exception e) { Plugin.Trace("[смайлики] строка: " + e.Message); return true; }
            return false;
        }
    }
}
