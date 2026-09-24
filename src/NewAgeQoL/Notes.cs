using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Notes
    {
        private const int Longest = 200;

        private sealed class Entry
        {
            internal string Login;
            internal string Text;
        }

        private static readonly Dictionary<int, Entry> Known = new Dictionary<int, Entry>();
        private static bool _read;
        private static bool _dirty;

        private static string FilePath
        {
            get { return Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "newage.qol"), "notes.txt"); }
        }

        internal static string Of(int userId)
        {
            Read();
            Entry one;
            return userId > 0 && Known.TryGetValue(userId, out one) ? one.Text : "";
        }

        private static void Put(int userId, string login, string text)
        {
            if (userId <= 0) return;
            Read();
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                if (Known.Remove(userId)) _dirty = true;
                return;
            }
            Entry one;
            if (Known.TryGetValue(userId, out one) && one.Text == text && one.Login == (login ?? "")) return;
            Known[userId] = new Entry { Login = login ?? "", Text = text };
            _dirty = true;
        }

        internal static void Flush()
        {
            if (!_dirty) return;
            if (!_read) { Plugin.Warn("[заметки] прежние заметки не прочитаны, сохранение отложено, чтобы не затереть файл"); return; }
            _dirty = false;
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var text = new StringBuilder();
                foreach (var pair in Known)
                    text.Append(pair.Key).Append('\t').Append(Pack(pair.Value.Login)).Append('\t').Append(Pack(pair.Value.Text)).Append('\n');
                string temp = path + ".tmp";
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (!File.Exists(path)) File.Move(temp, path);
                else
                {
                    try { File.Replace(temp, path, null); }
                    catch (Exception e)
                    {
                        Plugin.Trace("[заметки] замена одним действием не вышла, меняю по частям: " + e.Message);
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                Plugin.Trace("[заметки] сохранено заметок: " + Known.Count);
            }
            catch (Exception e)
            {
                _dirty = true;
                Plugin.Warn("[заметки] сохранение: " + e.Message);
            }
        }

        private static void Read()
        {
            if (_read) return;
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) { _read = true; return; }
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    int id;
                    if (parts.Length < 3 || !int.TryParse(parts[0], out id) || id <= 0) continue;
                    string note = Unpack(parts[2]);
                    if (note.Length == 0) continue;
                    Known[id] = new Entry { Login = Unpack(parts[1]), Text = note };
                }
                _read = true;
                Plugin.Trace("[заметки] прочитано заметок: " + Known.Count);
            }
            catch (Exception e) { Plugin.Warn("[заметки] чтение: " + e.Message); }
        }

        private static string Pack(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return raw.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Unpack(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var text = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c != '\\' || i + 1 >= raw.Length) { text.Append(c); continue; }
                char next = raw[++i];
                text.Append(next == 't' ? '\t' : next == 'n' ? '\n' : next);
            }
            return text.ToString();
        }

        internal static RectTransform Box(Transform parent, int userId, string login, int size, Font font)
        {
            if (parent == null || userId <= 0) return null;
            try
            {
                var box = new GameObject("QoLNote", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField), typeof(LayoutElement));
                box.transform.SetParent(parent, false);
                box.GetComponent<LayoutElement>().ignoreLayout = true;
                var back = box.GetComponent<Image>();
                back.color = new Color(0.07f, 0.05f, 0.03f, 0.85f);
                back.sprite = OnlineWindow.Rounded(8);
                back.type = Image.Type.Sliced;
                var edge = box.GetComponent<Outline>();
                edge.effectColor = new Color(0.62f, 0.48f, 0.26f, 0.9f);
                edge.effectDistance = new Vector2(1f, -1f);

                var text = OnlineWindow.Label(box.transform, "", size, FontStyle.Normal, new Color32(245, 235, 210, 255));
                text.alignment = TextAnchor.MiddleLeft;
                text.supportRichText = false;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                if (font != null) text.font = font;
                OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 2f), new Vector2(-10f, -2f));

                var hint = OnlineWindow.Label(box.transform, "Заметка: чей мульт, кто это", size, FontStyle.Italic, new Color(1f, 1f, 1f, 0.45f));
                hint.alignment = TextAnchor.MiddleLeft;
                hint.verticalOverflow = VerticalWrapMode.Overflow;
                if (font != null) hint.font = font;
                OnlineWindow.Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 2f), new Vector2(-10f, -2f));

                var field = box.GetComponent<InputField>();
                field.targetGraphic = back;
                field.textComponent = text;
                field.placeholder = hint;
                field.lineType = InputField.LineType.SingleLine;
                field.characterLimit = Longest;
                field.caretColor = new Color32(245, 235, 210, 255);
                field.customCaretColor = true;
                field.SetTextWithoutNotify(Of(userId));

                int id = userId;
                string who = login ?? "";
                field.onValueChanged.AddListener(v => Put(id, who, v));
                field.onEndEdit.AddListener(v => { Put(id, who, v); Flush(); });
                box.AddComponent<NoteKeeper>();
                return (RectTransform)box.transform;
            }
            catch (Exception e) { Plugin.Trace("[заметки] поле в карточке: " + e.Message); return null; }
        }
    }

    internal sealed class NoteKeeper : MonoBehaviour
    {
        private void OnDisable() => Notes.Flush();

        private void OnDestroy() => Notes.Flush();
    }
}
