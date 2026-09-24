using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WardrobeLook
    {
        internal static readonly Color Window = new Color32(21, 23, 27, 250);
        internal static readonly Color Card = new Color32(28, 31, 36, 255);
        internal static readonly Color Stage = new Color32(33, 36, 42, 255);
        internal static readonly Color Popup = new Color32(27, 30, 35, 252);
        internal static readonly Color Edge = new Color32(56, 62, 71, 255);
        internal static readonly Color Field = new Color32(17, 19, 22, 255);
        internal static readonly Color FieldEdge = new Color32(62, 69, 79, 255);
        internal static readonly Color Button = new Color32(45, 50, 58, 255);
        internal static readonly Color Danger = new Color32(116, 44, 40, 255);
        internal static readonly Color Tab = new Color32(35, 39, 45, 255);
        internal static readonly Color TabReceived = new Color32(31, 43, 60, 255);
        internal static readonly Color Accent = new Color32(226, 184, 92, 255);
        internal static readonly Color OnAccent = new Color32(26, 23, 18, 255);
        internal static readonly Color Bright = new Color32(236, 238, 241, 255);
        internal static readonly Color Label = new Color32(172, 179, 189, 255);
        internal static readonly Color Body = new Color32(214, 218, 224, 255);
        internal static readonly Color Faint = new Color32(116, 123, 133, 255);
        internal static readonly Color Good = new Color32(126, 214, 138, 255);
        internal static readonly Color Bad = new Color32(240, 122, 110, 255);
        internal static readonly Color DangerText = new Color32(255, 220, 214, 255);
        internal static readonly Color ReceivedText = new Color32(168, 196, 234, 255);

        internal static Color Stripe(int n) => new Color(1f, 1f, 1f, n % 2 == 0 ? 0.035f : 0.012f);

        internal static Color Mix(Color a, Color b, float k) => Color.Lerp(a, b, k);

        internal static void Style(InputField input)
        {
            if (input == null) return;
            var image = input.GetComponent<Image>();
            if (image != null) image.color = Field;
            var edge = input.GetComponent<Outline>();
            if (edge != null) edge.effectColor = FieldEdge;
            if (input.textComponent != null) input.textComponent.color = Bright;
            var hint = input.placeholder as Text;
            if (hint != null) hint.color = new Color(Faint.r, Faint.g, Faint.b, 0.8f);
            input.caretColor = Bright;
            input.customCaretColor = true;
            input.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        }

        internal static void Frame(GameObject go, Color edge)
        {
            if (go == null) return;
            var line = go.GetComponent<Outline>() ?? go.AddComponent<Outline>();
            line.effectColor = edge;
            line.effectDistance = new Vector2(1f, -1f);
        }
    }
}
