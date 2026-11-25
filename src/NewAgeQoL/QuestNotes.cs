using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Things.Thinginfo.Contextinfo;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class QuestNotes
    {
        private const float Again = 600f;
        private const float Retry = 10f;

        private sealed class Boss
        {
            internal int Note;
            internal int Race;
            internal string Name;
            internal bool Owned;
            internal int Inv;
            internal long Ms = -1;
            internal double At;
            internal double AskAt;
            internal bool Rescanned;
            internal Sprite Face;
        }

        private static readonly Boss[] Bosses =
        {
            new Boss { Note = 7042, Race = 20018, Name = "Жемчужина" },
            new Boss { Note = 7046, Race = 20020, Name = "Братья" },
            new Boss { Note = 7061, Race = 20024, Name = "Палач" },
        };

        private static readonly List<ResponseCallbackContext> Spent = new List<ResponseCallbackContext>();
        private static System.Reflection.MethodInfo _round;
        private static bool _roundLooked;

        internal static void Register()
        {
            foreach (var boss in Bosses)
            {
                var b = boss;
                Enchantments.Add(() => Has(b), () => Face(b), () => Lang.T(b.Name), () => Text(b), () => Left(b));
            }
        }

        private static bool Has(Boss b)
        {
            Sweep();
            if (Flasks.Scanning) return b.Owned;
            int inv = Flasks.InvOf(b.Note);
            b.Owned = inv > 0;
            if (inv != b.Inv)
            {
                b.Inv = inv;
                b.Ms = -1;
                b.AskAt = 0d;
                b.Rescanned = false;
            }
            return b.Owned;
        }

        private static long Left(Boss b)
        {
            if (b.Inv <= 0) return -1;
            if (RealTime.Now >= b.AskAt) Ask(b);
            if (b.Ms < 0) return -1;
            long left = b.Ms / 1000 - (long)(RealTime.Now - b.At);
            if (left <= 0 && !b.Rescanned)
            {
                b.Rescanned = true;
                Flasks.RequestScanNow();
            }
            return Math.Max(0, left);
        }

        private static string Text(Boss b)
        {
            long left = Left(b);
            if (left < 0) return Lang.T("Срок спрашиваю у сервера…");
            return Lang.F("Осталось {0}", Enchantments.Full(left));
        }

        private static void Ask(Boss b)
        {
            b.AskAt = RealTime.Now + Retry;
            int inv = b.Inv;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                var context = new ResponseCallbackContext();
                nc.SendRequest(context, new ContextInventoryThingHintRequest(inv, EThingContextWindow.WINDOW_INVENTORY), ResponseConsts.CONTEXT_INVENTORY_THING_HINT_RESPONSE, -1, msg =>
                {
                    Spent.Add(context);
                    var reply = msg as ContextInventoryThingHintResponseMessage;
                    if (reply == null || b.Inv != inv) return;
                    b.Ms = reply.RemoveTime.HasValue ? reply.RemoveTime.Value : -1;
                    b.At = RealTime.Now;
                    b.AskAt = RealTime.Now + Again;
                });
            }
            catch (Exception e) { Plugin.Caught("[notes] remove time " + b.Note + ": " + e.Message); }
        }

        private static void Sweep()
        {
            if (Spent.Count == 0) return;
            foreach (var context in Spent)
            {
                try { context.Destroy(); }
                catch (Exception e) { Plugin.Caught("[notes] reply context: " + e.Message); }
            }
            Spent.Clear();
        }

        private static Sprite Face(Boss b)
        {
            if (b.Face != null && b.Face.texture != null) return b.Face;
            b.Face = null;
            try
            {
                if (!_roundLooked)
                {
                    _roundLooked = true;
                    _round = AccessTools.Method(typeof(AtlasUtils), "GetRaceRoundAvatar");
                    if (_round == null) Plugin.Trace("[notes] no round avatar method in game");
                }
                var data = RaceDatabase.Instance != null ? RaceDatabase.Instance.GetData(b.Race) : null;
                if (_round != null && data != null && !string.IsNullOrEmpty(data.AvatarAtlasSpriteName))
                    b.Face = _round.Invoke(null, new object[] { data.AvatarAtlasSpriteName }) as Sprite;
            }
            catch (Exception e) { Plugin.Caught("[notes] face " + b.Race + ": " + e.Message); }
            if (b.Face != null && b.Face.texture != null) return b.Face;
            b.Face = null;
            return Flasks.Exact(b.Note);
        }
    }
}
