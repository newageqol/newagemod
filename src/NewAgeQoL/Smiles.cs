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
        private const float Typical = 19f;
        private const float Grow = 1.4f;
        private const byte Opaque = 200;
        private static TMP_FontAsset _fitted, _wanted;

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
            internal int Solid;
        }

        private const string Unknown = "[эмодзи]";

        private static readonly Dictionary<string, int[]> Likeness = new Dictionary<string, int[]>
        {
            { ":-)", new[] { 0x1F642, 0x263A, 0x1F60A, 0x1F643, 0x1F60C, 0x1F917 } },
            { ":D", new[] { 0x1F600, 0x1F603, 0x1F604, 0x1F601, 0x1F606, 0x1F638, 0x1F63A } },
            { "%}", new[] { 0x1F602, 0x1F923, 0x1F639, 0x1F605 } },
            { ";-)", new[] { 0x1F609 } },
            { ":-p", new[] { 0x1F61B, 0x1F61C, 0x1F61D, 0x1F60B, 0x1F92A } },
            { ":-8", new[] { 0x1F618, 0x1F617, 0x1F619, 0x1F61A, 0x1F48B, 0x1F60D, 0x1F970, 0x2764, 0x1F496, 0x1F495, 0x1F497, 0x1F49E } },
            { ":-(", new[] { 0x1F641, 0x2639, 0x1F61E, 0x1F61F, 0x1F614, 0x1F63F } },
            { ":-/", new[] { 0x1F615, 0x1F610, 0x1F611, 0x1F612, 0x1F928 } },
            { ";-(", new[] { 0x1F622, 0x1F62D, 0x1F625, 0x1F63E } },
            { "):(", new[] { 0x1F620, 0x1F621, 0x1F92C, 0x1F624 } },
            { "}:)", new[] { 0x1F608, 0x1F47F } },
            { "8-)", new[] { 0x1F60E } },
            { ":-O", new[] { 0x1F62E, 0x1F62F, 0x1F632, 0x1F631, 0x1F626, 0x1F627, 0x1F640 } },
            { ":-]", new[] { 0x1F633, 0x1F60F } },
            { ":-!", new[] { 0x1F922, 0x1F92E } },
            { "||-Z", new[] { 0x1F634, 0x1F4A4, 0x1F62A } },
            { "))@((", new[] { 0x1F607 } },
            { ":-U", new[] { 0x1F37A, 0x1F37B } },
            { "*\"\"0", new[] { 0x1F4A3 } },
            { "|:)v", new[] { 0x1F44B } },
            { ":o)", new[] { 0x1F437, 0x1F416, 0x1F43D } },
            { "}%{", new[] { 0x1F430, 0x1F407 } },
            { "(B-))", new[] { 0x2600, 0x1F31E } },
        };

        private static readonly List<Face> Faces = new List<Face>();
        private static readonly Dictionary<char, List<Face>> Heads = new Dictionary<char, List<Face>>();
        private static readonly Dictionary<int, Face> Pictures = new Dictionary<int, Face>();
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
                if (art == null || list == null) { Plugin.Trace("[smiles] no picture or index"); return; }
                if (!Read(list)) return;

                _sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _sheet.wrapMode = TextureWrapMode.Clamp;
                _sheet.filterMode = FilterMode.Bilinear;
                _sheet.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(_sheet, art)) { Plugin.Trace("[smiles] picture could not be read"); return; }

                Measure();
                Build();
                _ready = true;
                Fit(_wanted ?? TMP_Settings.defaultFontAsset);
                Plugin.Trace("[smiles] ready: " + Faces.Count + " pcs, frames " + _asset.spriteCharacterTable.Count);
            }
            catch (Exception e) { Plugin.Fault("[smiles] setup: " + e.Message); }
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
            Pictures.Clear();
            foreach (var pair in Likeness)
            {
                var face = Faces.Find(one => one.Code == pair.Key);
                if (face == null) { Plugin.Trace("[smiles] no smile for emoji code " + pair.Key); continue; }
                foreach (int code in pair.Value) Pictures[code] = face;
            }
            return true;
        }

        private static void Measure()
        {
            try
            {
                var px = _sheet.GetPixels32();
                int tw = _sheet.width, th = _sheet.height;
                foreach (var face in Faces)
                {
                    int lo = int.MaxValue, hi = -1;
                    for (int i = 0; i < face.Count; i++)
                        for (int y = 0; y < face.Height; y++)
                        {
                            int row = face.SpotY[i] + y;
                            if (row < 0 || row >= th) continue;
                            for (int x = 0; x < face.Width; x++)
                            {
                                int col = face.SpotX[i] + x;
                                if (col < 0 || col >= tw || px[row * tw + col].a < Opaque) continue;
                                if (y < lo) lo = y;
                                if (y > hi) hi = y;
                                break;
                            }
                        }
                    face.Solid = hi >= lo ? hi - lo + 1 : face.Height;
                }
            }
            catch (Exception e) { Plugin.Trace("[smiles] glyph measure: " + e.Message); }
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
            if (paint == null) throw new Exception("material for inline text images not found");
            paint.name = Sheet + " Material";
            paint.hideFlags = HideFlags.HideAndDontSave;
            paint.SetTexture(ShaderUtilities.ID_MainTex, _sheet);
            _asset.material = paint;
            _asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(paint.name);
            _asset.spriteInfoList = new List<TMP_Sprite>();
            _asset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();

            uint slot = 0;
            foreach (var face in Faces)
            {
                float body = Math.Max(face.Solid > 0 ? face.Solid : face.Height, face.Height / Grow);
                float k = Typical / body;
                for (int i = 0; i < face.Count; i++)
                {
                    var metrics = new GlyphMetrics(face.Width * k, face.Height * k, face.Width * Side * k,
                        face.Height * Lift * k, face.Width * (1f + Side * 2f) * k);
                    var rect = new GlyphRect(face.SpotX[i], face.SpotY[i], face.Width, face.Height);
                    var glyph = new TMP_SpriteGlyph(slot, metrics, rect, 1f, 0);
                    var letter = new TMP_SpriteCharacter(0xFFFE, _asset, glyph);
                    letter.name = face.Clip + "_" + i;
                    letter.scale = Zoom;
                    _asset.spriteGlyphTable.Add(glyph);
                    _asset.spriteCharacterTable.Add(letter);
                    slot++;
                }
            }
            _asset.UpdateLookupTables();
            MaterialReferenceManager.AddSpriteAsset(_asset.hashCode, _asset);
            int upper = TMP_TextUtilities.GetSimpleHashCode(Sheet.ToUpperInvariant());
            if (upper != _asset.hashCode) MaterialReferenceManager.AddSpriteAsset(upper, _asset);
            TMP_SpriteAsset back;
            if (!MaterialReferenceManager.TryGetSpriteAsset(_asset.hashCode, out back) || !ReferenceEquals(back, _asset))
                throw new Exception("smiles sheet was not registered in TextMeshPro");
            if (_asset.spriteCharacterTable.Count != (int)slot)
                throw new Exception("frames in sheet " + _asset.spriteCharacterTable.Count + ", expected " + slot);
        }

        private static Material Paint()
        {
            var known = TMP_Settings.defaultSpriteAsset;
            if (known != null && known.material != null) return new Material(known.material);
            var shader = Shader.Find("TextMeshPro/Sprite");
            return shader != null ? new Material(shader) : null;
        }

        internal static void Fit(TMP_FontAsset font)
        {
            if (font == null) return;
            _wanted = font;
            if (!_ready || _asset == null || ReferenceEquals(font, _fitted)) return;
            try
            {
                var main = font.faceInfo;
                if (main.pointSize <= 0 || main.ascentLine <= 0f) return;
                var info = main;
                info.scale = main.scale * main.ascentLine / Typical;
                info.ascentLine = Typical / Zoom;
                info.descentLine = main.descentLine * Typical / (main.ascentLine * Zoom);
                var slot = AccessTools.Field(typeof(TMP_SpriteAsset), "m_FaceInfo");
                if (slot == null) return;
                slot.SetValue(_asset, info);
                _fitted = font;
                Plugin.Trace("[smiles] size from font '" + font.name + "': " + main.pointSize + " pt");
            }
            catch (Exception e) { Plugin.Trace("[smiles] size: " + e.Message); }
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
                int from = at;
                string swap = Picture(text, ref at);
                if (swap == null)
                {
                    var face = Hit(text, at);
                    if (face == null) { at++; continue; }
                    swap = face.Tag;
                    at += face.Code.Length;
                }
                if (box == null) box = new StringBuilder(text.Length + 96);
                box.Append(text, kept, from - kept);
                box.Append(swap);
                kept = at;
            }
            if (box == null) return text;
            box.Append(text, kept, text.Length - kept);
            return box.ToString();
        }

        private static string Picture(string text, ref int at)
        {
            int step;
            int code = Point(text, at, out step);
            bool joint = code == 0xFE0F || code == 0xFE0E || code == 0x200D || code == 0x20E3;
            Face face;
            bool known = Pictures.TryGetValue(code, out face);
            if (!known && !joint && !Strange(code)) return null;
            int end = at + step;
            bool flag = code >= 0x1F1E6 && code <= 0x1F1FF;
            while (end < text.Length)
            {
                int next = Point(text, end, out step);
                if (next == 0xFE0F || next == 0xFE0E || next == 0x20E3 || (next >= 0x1F3FB && next <= 0x1F3FF)) { end += step; continue; }
                if (flag && next >= 0x1F1E6 && next <= 0x1F1FF) { end += step; flag = false; known = false; continue; }
                if (next == 0x200D && end + 1 < text.Length)
                {
                    end++;
                    Point(text, end, out step);
                    end += step;
                    known = false;
                    continue;
                }
                break;
            }
            at = end;
            if (known) return face.Tag;
            return joint ? "" : Lang.T(Unknown);
        }

        private static int Point(string text, int at, out int step)
        {
            if (char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]))
            {
                step = 2;
                return char.ConvertToUtf32(text[at], text[at + 1]);
            }
            step = 1;
            return text[at];
        }

        private static bool Strange(int code)
        {
            if (code >= 0x1F000 && code <= 0x1FAFF) return true;
            bool symbol = (code >= 0x2190 && code <= 0x21FF) || (code >= 0x2300 && code <= 0x23FF)
                || (code >= 0x2600 && code <= 0x27BF) || (code >= 0x2900 && code <= 0x297F)
                || (code >= 0x2B00 && code <= 0x2BFF) || code == 0x3030 || code == 0x303D || code == 0x3297 || code == 0x3299;
            if (!symbol) return false;
            var font = _wanted ?? TMP_Settings.defaultFontAsset;
            return font == null || !font.HasCharacter((char)code, true, true);
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
            catch (Exception e) { Plugin.Fault("[smiles] window: " + e.Message); Close(); }
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

            _title = OnlineWindow.Label(_panel.transform, Lang.T(Caption), 12, FontStyle.Bold, WardrobeLook.Label);
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
                if (_title != null) _title.text = Lang.T(Caption);
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
            catch (Exception e) { Plugin.Trace("[smiles] empty text: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChatContent), "ReplaceSmiles")]
    public static class ChatSmilesPatch
    {
        private static bool Prefix(string str, ref string __result)
        {
            if (!Smiles.Ready) return true;
            try { __result = Smiles.Dress(str); }
            catch (Exception e) { Plugin.Trace("[smiles] line: " + e.Message); return true; }
            return false;
        }
    }
}
