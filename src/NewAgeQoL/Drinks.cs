using System;
using System.Collections.Generic;
using Transport.Messages.Responses.Combat;
using Transport.Messages.Responses.Combat.Buttons;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Drinks
    {
        internal const int Kinds = 4;
        internal const int Cannibal = 3;
        private const int DrinkEffect = 9;
        private const int DodgeEffect = 3;
        private const int ChangeType = 2;
        private const float OwnLag = 1f;

        private static readonly string[] Changes = { "change_life", "change_mana", "change_stamina", null };
        private static readonly int[] Liquids = { 1, 2, 0, 0 };
        private static readonly int[] Recharge = { 6, 6, 7, 10 };
        private static int _cannibalRecharge;
        internal static readonly Color32[] Tints =
        {
            new Color32(255, 106, 90, 255),
            new Color32(109, 179, 255, 255),
            new Color32(255, 210, 87, 255),
            new Color32(150, 226, 120, 255),
        };

        private sealed class Sip
        {
            internal int Walk, Recharge;
        }

        private static readonly Dictionary<int, Sip[]> Sips = new Dictionary<int, Sip[]>();
        private static readonly Dictionary<int, int> Recharging = new Dictionary<int, int>();
        private static readonly Sprite[] Flasks = new Sprite[Kinds];
        private static int _walks, _lastWalk = int.MinValue;
        private static int _ownRecharge;
        private static float _ownAt = -99f, _rechargingAt = -99f;

        internal static void Forget()
        {
            Sips.Clear();
            _walks = 0;
            _lastWalk = int.MinValue;
            _ownAt = -99f;
        }

        internal static void OnRound(object m)
        {
            var msg = m as NewRoundResponseMessage;
            if (msg == null || msg.RoundType != (int)RoundType.WALK_ROUND || msg.RoundNum == _lastWalk) return;
            _lastWalk = msg.RoundNum;
            _walks++;
        }

        internal static void OnButtons(object m)
        {
            var msg = m as UpdateButtonsResponseMessage;
            if (msg == null || msg.Buttons == null) return;
            Recharging.Clear();
            foreach (var b in msg.Buttons)
            {
                if (b == null || (b.RefreshType & 0x100) == 0 || b.CanActivate != false) continue;
                int recharge = b.Recharge.GetValueOrDefault();
                if (recharge <= 0 || recharge > 100) continue;
                Recharging[b.Id] = recharge;
            }
            _rechargingAt = Time.unscaledTime;
        }

        internal static void OnUsed(object m)
        {
            var msg = m as UseButtonResponseMessage;
            int recharge;
            if (msg == null || Time.unscaledTime - _rechargingAt > OwnLag || !Recharging.TryGetValue(msg.ButtonId, out recharge)) return;
            _ownRecharge = recharge;
            _ownAt = Time.unscaledTime;
        }

        internal static void OnAnimation(object m)
        {
            try
            {
                var msg = m as AnimationResponseMessage;
                if (msg == null || msg.Groups == null) return;
                foreach (var group in msg.Groups)
                    if (group != null) Scan(group.Items);
            }
            catch (Exception e) { Plugin.Caught("[drinks] animation: " + e.Message); }
        }

        private static void Scan(List<AnimationItemMessage> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                if (item.VisualEffect == DrinkEffect) Sipped(item, items, i);
                else if (item.VisualEffect == DodgeEffect && item.VisualEffectSubId.GetValueOrDefault() == DodgeConsts.DODGE_CANNIBALISM) Ate(item.SourceUserId);
                Scan(item.Items);
            }
        }

        private static void Sipped(AnimationItemMessage drink, List<AnimationItemMessage> items, int at)
        {
            int who = drink.SourceUserId;
            if (who == 0) return;
            int kind = -1;
            if (drink.Items != null)
                foreach (var one in drink.Items)
                    if (Change(one, who, ref kind)) break;
            if (kind < 0)
                for (int j = at + 1; j < items.Count; j++)
                {
                    var next = items[j];
                    if (next == null || next.Type != ChangeType || next.TargetUserId != who) break;
                    if (Change(next, who, ref kind)) break;
                }
            if (kind < 0)
            {
                int liquid = drink.VisualEffectSubId.GetValueOrDefault();
                for (int k = 0; k < Kinds; k++)
                    if (Liquids[k] > 0 && Liquids[k] == liquid) kind = k;
            }
            if (kind < 0) return;
            Drank(who, kind);
        }

        private static void Ate(int who)
        {
            if (who == 0) return;
            bool mine = Me() == who;
            int own = mine ? OwnCannibal() : 0;
            if (own > 0) _cannibalRecharge = own;
            int recharge = own > 0 ? own : _cannibalRecharge > 0 ? _cannibalRecharge : Recharge[Cannibal];
            if (recharge <= 0)
            {
                Plugin.Trace("[drinks] " + who + ": cannibalism, recharge unknown");
                return;
            }
            Sip[] row;
            if (!Sips.TryGetValue(who, out row)) Sips[who] = row = new Sip[Kinds];
            row[Cannibal] = new Sip { Walk = _walks, Recharge = recharge };
            Plugin.Trace("[drinks] " + who + (mine ? " (me)" : "") + ": cannibalism, recharge " + recharge + ", walk round " + _walks);
        }

        private static int OwnCannibal()
        {
            try
            {
                var cd = FighterHint.Cd();
                var manager = cd != null ? cd.ButtonManager : null;
                var button = manager != null ? manager.Dodges.GetButton(DodgeConsts.DODGE_CANNIBALISM) : null;
                int recharge = button != null ? button.Recharge : 0;
                return recharge > 0 && recharge <= 100 ? recharge : 0;
            }
            catch { return 0; }
        }

        internal static void Learn()
        {
            int own = OwnCannibal();
            if (own > 0) _cannibalRecharge = own;
        }

        private static bool Change(AnimationItemMessage one, int who, ref int kind)
        {
            if (one == null || one.TargetUserId != who) return false;
            int k = Array.IndexOf(Changes, one.Animation);
            if (k < 0) return false;
            kind = k;
            return true;
        }

        private static void Drank(int who, int kind)
        {
            Sip[] row;
            if (!Sips.TryGetValue(who, out row)) Sips[who] = row = new Sip[Kinds];
            bool mine = Me() == who;
            int recharge = mine && Time.unscaledTime - _ownAt <= OwnLag && _ownRecharge > 0 ? _ownRecharge : Recharge[kind];
            if (mine) _ownAt = -99f;
            row[kind] = new Sip { Walk = _walks, Recharge = recharge };
            Plugin.Trace("[drinks] " + who + (mine ? " (me)" : "") + ": " + Changes[kind]
                + ", recharge " + recharge + ", walk round " + _walks);
        }

        private static int Me()
        {
            try
            {
                var cd = FighterHint.Cd();
                return cd != null && cd.MyCharacter != null ? cd.MyCharacter.UserId : 0;
            }
            catch { return 0; }
        }

        internal static bool Left(int userId, int kind, out int left)
        {
            left = 0;
            Sip[] row;
            if (!Sips.TryGetValue(userId, out row) || row[kind] == null) return false;
            left = Math.Max(0, row[kind].Recharge - (_walks - row[kind].Walk));
            return true;
        }

        internal static bool Any(int userId) => Sips.ContainsKey(userId);

        internal static Sprite Flask(int kind)
        {
            var have = Flasks[kind];
            if (have != null && have.texture != null) return have;
            if (kind == Cannibal)
            {
                try { have = AtlasUtils.GetQuickButtonSprite("dodge" + DodgeConsts.DODGE_CANNIBALISM); }
                catch (Exception e) { Plugin.Caught("[drinks] cannibal icon: " + e.Message); }
                Flasks[kind] = have;
                return have;
            }
            try
            {
                const int side = 64;
                const int ss = 4;
                var tex = new Texture2D(side, side, TextureFormat.RGBA32, false);
                Color liquid = Tints[kind];
                var px = new Color[side * side];
                for (int py = 0; py < side; py++)
                    for (int qx = 0; qx < side; qx++)
                    {
                        Color sum = new Color(0f, 0f, 0f, 0f);
                        for (int sy = 0; sy < ss; sy++)
                            for (int sx = 0; sx < ss; sx++)
                            {
                                float x = (qx + (sx + 0.5f) / ss) / side;
                                float y = (py + (sy + 0.5f) / ss) / side;
                                var c = Paint(x, y, liquid);
                                sum.r += c.r * c.a; sum.g += c.g * c.a; sum.b += c.b * c.a; sum.a += c.a;
                            }
                        px[py * side + qx] = sum.a > 0f
                            ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a / (ss * ss))
                            : new Color(1f, 1f, 1f, 0f);
                    }
                tex.SetPixels(px);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                have = Sprite.Create(tex, new Rect(0f, 0f, side, side), new Vector2(0.5f, 0.5f), 100f);
                Flasks[kind] = have;
            }
            catch (Exception e) { Plugin.Caught("[drinks] flask: " + e.Message); }
            return have;
        }

        private static Color Paint(float x, float y, Color liquid)
        {
            float body = Circle(x, y, 0.5f, 0.36f, 0.3f);
            float neck = Box(x, y, 0.5f, 0.7f, 0.1f, 0.14f);
            float lip = Box(x, y, 0.5f, 0.83f, 0.15f, 0.035f, 0.03f);
            float cork = Box(x, y, 0.5f, 0.91f, 0.085f, 0.06f, 0.025f);
            float glass = Mathf.Min(body, neck);
            const float rim = 0.045f;

            if (cork <= 0f) return new Color(0.62f, 0.43f, 0.26f, 1f);
            if (lip <= 0f) return new Color(0.86f, 0.9f, 0.95f, 1f);
            if (glass > 0f) return new Color(0f, 0f, 0f, 0f);
            if (glass > -rim) return new Color(0.86f, 0.9f, 0.95f, 1f);

            float shine = Circle(x, y, 0.38f, 0.44f, 0.075f);
            if (shine <= 0f) return new Color(1f, 1f, 1f, 1f);
            if (body <= 0f && y < 0.47f + 0.02f * Mathf.Sin(x * 18f))
            {
                float deep = Mathf.Clamp01((0.47f - y) / 0.4f);
                var tone = Color.Lerp(liquid, liquid * 0.62f, deep * 0.8f);
                tone.a = 1f;
                return tone;
            }
            return new Color(0.75f, 0.82f, 0.9f, 0.35f);
        }

        private static float Circle(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        private static float Box(float x, float y, float cx, float cy, float hw, float hh, float round = 0f)
        {
            float dx = Mathf.Abs(x - cx) - hw + round, dy = Mathf.Abs(y - cy) - hh + round;
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - round;
        }
    }
}
