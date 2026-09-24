using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

[HarmonyPatch]
internal static class Captions
{
    private static readonly System.Reflection.FieldInfo DisplaysField = AccessTools.Field(typeof(CombatData), "_lifeManaEnergyDisplays");
    private static readonly System.Reflection.FieldInfo LoginField = AccessTools.Field(typeof(LifeManaEnergyDisplay), "LoginText");
    private static readonly System.Reflection.FieldInfo EffectField = AccessTools.Field(typeof(LifeManaEnergyDisplay), "AnimationText");
    private static readonly List<(LifeManaEnergyDisplay Display, float Depth)> Sorted = new();
    private static readonly Dictionary<int, string> Logged = new();
    private static readonly Color32 LifeColor = new(0xFF, 0x33, 0x00, 0xFF);
    private static readonly Color32 ManaColor = new(0x00, 0xCC, 0xFF, 0xFF);
    private static readonly int[] TeamColors = { 0xFF0000, 0x00CCFF, 0xD8D801, 0xFFFFCC, 0xFF9933, 0x3399FF, 0x993300, 0x999933, 0x663366, 0xFF6666, 0x663300, 0x9999FF, 0xCC9999, 0x666699, 0x66FFFF };
    private static readonly Vector3[] Corners = new Vector3[4];
    private const float FlashFont = 12f;
    private const float Middle = 1.35f;
    private static float _orderAt;

    private sealed class Board
    {
        public Text Login;
        public Text Life;
        public Text Mana;
        public readonly List<Graphic> Muted = new();
        public int Life0 = int.MinValue;
        public int Mana0 = int.MinValue;
        public int Size = -1;
        public float LifeWidth;
        public float ManaWidth;
        public float Since;
        public float Alpha = 1f;
        public Vector3 Origin = Vector3.one;
        public float Base = 1f;
        public float Traced;
    }

    private static readonly ConditionalWeakTable<LifeManaEnergyDisplay, Board> Boards = new();

    internal static LifeManaEnergyDisplay DisplayOf(AbstractCharacter character)
    {
        if (character == null || DisplaysField == null || !(Fighters.Combat() is CombatData combat)) return null;
        return DisplaysField.GetValue(combat) is Dictionary<int, LifeManaEnergyDisplay> displays && displays.TryGetValue(character.UserId, out var display) ? display : null;
    }

    internal static void Order()
    {
        if (!Plugin.FlashFight || DisplaysField == null || Time.unscaledTime < _orderAt) return;
        _orderAt = Time.unscaledTime + 0.1f;
        try
        {
            if (!(Fighters.Combat() is CombatData combat)) return;
            if (!(DisplaysField.GetValue(combat) is Dictionary<int, LifeManaEnergyDisplay> displays) || displays.Count < 2) return;
            var location = CombatView.Get();
            var eye = location != null ? location.CombatCamera : null;
            if (eye == null) return;
            Sorted.Clear();
            foreach (var display in displays.Values)
            {
                if (display == null || display.Character == null || !display.Character.Initialized) continue;
                var doll = Fighters.DollOf(display.Character);
                var point = doll != null && doll.Placed ? doll.Feet : display.Character.position;
                Sorted.Add((display, Vector3.Dot(point - eye.transform.position, eye.transform.forward)));
            }
            if (Sorted.Count < 2) return;
            Sorted.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            int previous = -1;
            bool ordered = true;
            foreach (var entry in Sorted)
            {
                int index = entry.Display.transform.GetSiblingIndex();
                if (index < previous)
                {
                    ordered = false;
                    break;
                }
                previous = index;
            }
            if (ordered) return;
            foreach (var entry in Sorted) entry.Display.transform.SetAsLastSibling();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[подписи] порядок: " + ex.Message);
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(LifeManaEnergyDisplay), "Update")]
    private static bool FollowDoll(LifeManaEnergyDisplay __instance)
    {
        if (!Plugin.FlashFight)
        {
            if (__instance != null) Plain(__instance);
            return true;
        }
        try
        {
            Numbers(__instance);
            var character = __instance.Character;
            if (character == null || !character.Initialized) return true;
            var doll = Fighters.DollOf(character);
            var location = CombatView.Get();
            if (location == null || location.CombatCamera == null) return true;
            if (doll == null || !doll.Placed || !doll.HasPicture)
            {
                Scale(__instance, character, character.position, location.CombatCamera);
                return true;
            }
            var collider = character.CharacterCollider;
            if (collider == null) return true;
            float lift = (collider.direction == 1 ? collider.height / 2f : collider.radius) + collider.center.y;
            var screen = location.CombatCamera.WorldToScreenPoint(doll.Feet + doll.HeadShift(location.CombatCamera) + new Vector3(0f, lift, 0f));
            if (Trace.On && (!Logged.TryGetValue(character.UserId, out string seen) || seen != doll.ViewLook))
            {
                Logged[character.UserId] = doll.ViewLook;
                var feet = location.CombatCamera.WorldToScreenPoint(doll.Feet + new Vector3(0f, lift, 0f));
                Trace.Write($"«{(character is PlayerCharacter player ? player.Login : character.UserId.ToString())}» ник над головой: облик {Trace.Look(doll.ViewLook)}, голова {FrameCache.HeadPixels(doll.ViewLook):0} пикс. кадра, сдвиг от ног на экране {screen.x - feet.x:0} пикс., ноги x {feet.x:0}");
            }
            float scale = Scale(__instance, character, doll.Feet, location.CombatCamera);
            screen.y += Fighters.Caption + Fighters.CaptionRaise * scale;
            __instance.transform.position = screen;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static float Scale(LifeManaEnergyDisplay display, AbstractCharacter character, Vector3 feet, Camera eye)
    {
        if (!Boards.TryGetValue(display, out var board) || board.Login == null || board.Login.fontSize <= 0) return 1f;
        var node = display.transform;
        float now = node.localScale.y;
        float login = Mathf.Abs(board.Login.rectTransform.lossyScale.y);
        if (Mathf.Abs(now) < 0.0001f || login < 0.0001f) return 1f;
        float unit = Field.DollSize / Field.PxPerUnit;
        var at = eye.WorldToScreenPoint(feet);
        var up = eye.WorldToScreenPoint(feet + eye.transform.up * unit);
        float pixel = ((Vector2)up - (Vector2)at).magnitude;
        if (pixel < 0.0001f) return now / board.Base;
        float font = FlashFont * Middle;
        float want = Mathf.Clamp(now * font * pixel / (board.Login.fontSize * login), board.Base * 0.2f, board.Base * 2f);
        if (Mathf.Abs(want - now) > 0.002f) node.localScale = new Vector3(want, want, node.localScale.z);
        float scale = want / board.Base;
        if (Trace.On && Mathf.Abs(scale - board.Traced) > 0.1f)
        {
            board.Traced = scale;
            Trace.Write($"«{(character is PlayerCharacter player ? player.Login : character.UserId.ToString())}» подписи над головой одного размера у всех, в размер сцены: шрифт {font:0.#} пикс. Flash = {font * pixel:0.#} пикс. экрана, масштаб ×{scale:0.00}");
        }
        return scale;
    }

    private static void Numbers(LifeManaEnergyDisplay display)
    {
        var character = display.Character;
        var indicators = character?.Indicators;
        if (indicators == null) return;
        if (!Boards.TryGetValue(display, out var board))
        {
            if (!(LoginField?.GetValue(display) is Text login) || login == null) return;
            board = Build(display, login);
            Boards.Add(display, board);
        }
        if (board.Login == null || board.Life == null || board.Mana == null) return;
        foreach (var graphic in board.Muted)
            if (graphic != null && graphic.enabled)
                graphic.enabled = false;
        var team = TeamColor(character.Team);
        if (board.Login.color != team) board.Login.color = team;
        bool measure = false;
        if (board.Size != board.Login.fontSize)
        {
            board.Size = board.Login.fontSize;
            board.Life.fontSize = board.Size;
            board.Mana.fontSize = board.Size;
            measure = true;
        }
        if (board.Life0 != indicators.CurrentLife)
        {
            board.Life0 = indicators.CurrentLife;
            board.Life.text = board.Life0.ToString();
            measure = true;
        }
        if (board.Mana0 != indicators.CurrentMana)
        {
            board.Mana0 = indicators.CurrentMana;
            board.Mana.text = board.Mana0.ToString();
            measure = true;
        }
        if (measure)
        {
            board.LifeWidth = board.Life.preferredWidth;
            board.ManaWidth = board.Mana.preferredWidth;
        }
        Fade(display, board, character);
        Lay(board);
    }

    private static Board Build(LifeManaEnergyDisplay display, Text login)
    {
        var origin = display.transform.localScale;
        var board = new Board { Login = login, Since = Time.unscaledTime, Origin = origin, Base = Mathf.Abs(origin.y) < 0.0001f ? 1f : origin.y };
        var effect = EffectField?.GetValue(display) as Text;
        foreach (var graphic in display.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null || !graphic.enabled) continue;
            var node = graphic.transform;
            if (node.IsChildOf(login.transform)) continue;
            if (effect != null && node.IsChildOf(effect.transform)) continue;
            graphic.enabled = false;
            board.Muted.Add(graphic);
        }
        board.Life = Label(display, login, "NewAge2D.Life", LifeColor);
        board.Mana = Label(display, login, "NewAge2D.Mana", ManaColor);
        if (Trace.On) Trace.Write($"«{(display.Character is PlayerCharacter player ? player.Login : display.Character.UserId.ToString())}» над головой числа жизни и маны вместо полосок, как во Flash, выключено: {string.Join(", ", board.Muted.Select(graphic => graphic.name))}");
        return board;
    }

    private static Text Label(LifeManaEnergyDisplay display, Text login, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(display.transform, false);
        go.layer = login.gameObject.layer;
        var text = go.GetComponent<Text>();
        text.font = login.font;
        text.fontSize = login.fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = color;
        foreach (var shadow in login.GetComponents<Shadow>())
        {
            if (!(go.AddComponent(shadow.GetType()) is Shadow copy)) continue;
            copy.effectColor = shadow.effectColor;
            copy.effectDistance = shadow.effectDistance;
            copy.useGraphicAlpha = shadow.useGraphicAlpha;
        }
        return text;
    }

    private static void Fade(LifeManaEnergyDisplay display, Board board, AbstractCharacter character)
    {
        var doll = Fighters.DollOf(character);
        float alpha = doll != null ? doll.TeleportAlpha : 1f;
        bool appearing = Fighters.ClipOf(character) != null && Time.unscaledTime - board.Since < 6f && (doll == null || (doll.Appearing && !doll.Broken));
        if (appearing) alpha = 0f;
        if (Mathf.Approximately(alpha, board.Alpha)) return;
        var group = display.GetComponent<CanvasGroup>();
        if (group == null) group = display.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alpha;
        board.Alpha = alpha;
    }

    private static void Lay(Board board)
    {
        var login = board.Login.rectTransform;
        login.GetWorldCorners(Corners);
        var center = (Corners[0] + Corners[2]) * 0.5f;
        var scale = login.lossyScale;
        float line = board.Size * 1.15f;
        float gap = board.Size * 0.5f;
        float left = -(board.LifeWidth + gap + board.ManaWidth) * 0.5f;
        var pivot = new Vector2(0f, 0.5f);
        Place(board.Life.rectTransform, center, scale, new Vector2(left, -line), new Vector2(board.LifeWidth + 2f, line), pivot);
        Place(board.Mana.rectTransform, center, scale, new Vector2(left + board.LifeWidth + gap, -line), new Vector2(board.ManaWidth + 2f, line), pivot);
    }

    private static void Place(RectTransform rect, Vector3 center, Vector3 scale, Vector2 offset, Vector2 size, Vector2 pivot)
    {
        if (rect.pivot != pivot) rect.pivot = pivot;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
        var at = center + new Vector3(offset.x * scale.x, offset.y * scale.y, 0f);
        if ((rect.position - at).sqrMagnitude > 0.01f) rect.position = at;
    }

    private static void Plain(LifeManaEnergyDisplay display)
    {
        if (!Boards.TryGetValue(display, out var board)) return;
        Boards.Remove(display);
        foreach (var graphic in board.Muted)
            if (graphic != null)
                graphic.enabled = true;
        if (board.Life != null) UnityEngine.Object.Destroy(board.Life.gameObject);
        if (board.Mana != null) UnityEngine.Object.Destroy(board.Mana.gameObject);
        if (board.Login != null) board.Login.color = display.TeamColor;
        display.transform.localScale = board.Origin;
        if (Mathf.Approximately(board.Alpha, 1f)) return;
        var group = display.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = 1f;
    }

    private static Color TeamColor(int team)
    {
        int rgb = TeamColors[team >= 0 && team < TeamColors.Length ? team : 0];
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
    }
}
