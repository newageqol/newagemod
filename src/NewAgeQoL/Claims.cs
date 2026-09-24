using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Claims
    {
        private const float Grace = 10f;
        private const float Look = 0.5f;

        private sealed class Claim
        {
            internal BaseEnterfightView View;
            internal int Id;
            internal float DueAt;
            internal string Who;
        }

        private static readonly List<Claim> Live = new List<Claim>();

        private sealed class Ended
        {
            internal string Leader;
            internal int Kind;
            internal int Max;
            internal float At;
        }

        private static readonly List<Ended> Ends = new List<Ended>();
        private static readonly HashSet<int> Kinds = new HashSet<int>();
        private static readonly System.Reflection.FieldInfo AnnounceField = AccessTools.Field(typeof(BaseEnterfightWidget), "_announce");
        private static float _next;

        internal static void Note(BaseEnterfightView view, FightAnnounce one)
        {
            try
            {
                if (view == null || one == null || one.Id <= 0) return;
                if (one.RoundTimeout > 0 && Kinds.Add(one.ClaimType) && Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                    Plugin.Log?.LogInfo("[заявки] вид заявки " + one.ClaimType + " впервые: создатель " + one.LeaderLogin
                        + ", игроков до " + one.MaxCount + ", раунд " + one.RoundTimeout + ", уровни " + one.MinLevel + "-" + one.MaxLevel);
                if (one.Timeout <= 0) return;
                for (int i = Live.Count - 1; i >= 0; i--)
                    if (Live[i].Id == one.Id) Live.RemoveAt(i);
                Live.Add(new Claim
                {
                    View = view,
                    Id = one.Id,
                    DueAt = RealTime.Now + one.Timeout / 1000f + Grace,
                    Who = one.LeaderLogin,
                });
            }
            catch (Exception e) { Plugin.Trace("[заявки] запись: " + e.Message); }
        }

        internal static void Tick()
        {
            Stale();
            if (Live.Count == 0) return;
            if (RealTime.Now < _next) return;
            _next = RealTime.Now + Look;

            float now = RealTime.Now;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var one = Live[i];
                if (one == null || one.View == null) { Live.RemoveAt(i); continue; }
                if (!Here(one)) { Live.RemoveAt(i); continue; }
                if (now < one.DueAt) continue;
                Live.RemoveAt(i);
                try
                {
                    one.View.RemoveWidgetById(one.Id);
                    Plugin.Trace("[заявки] снял просроченную заявку " + one.Id
                        + (string.IsNullOrEmpty(one.Who) ? "" : " от " + one.Who));
                }
                catch (Exception e) { Plugin.Trace("[заявки] снятие " + one.Id + ": " + e.Message); }
            }
        }

        internal static bool Update(AbstractEnterFightController ctrl, object resp)
        {
            try
            {
                var one = resp as FightAnnounce;
                if (ctrl == null || one == null) return true;
                var view = Traverse.Create(ctrl).Property("EnterfightView").GetValue<BaseEnterfightView>();
                if (view == null || view.Widgets == null) return true;
                if (one.Type == 1)
                {
                    if (Count(view, one.Id) == 0) return true;
                    Note(view, one);
                    Plugin.Trace("[заявки] заявка " + one.Id + " пришла повторно, вторую строку не добавляю");
                    return false;
                }
                Remember(view, one.Id);
                int gone = 0;
                while (gone < 20 && view.RemoveWidgetById(one.Id)) gone++;
                for (int i = Live.Count - 1; i >= 0; i--)
                    if (Live[i].Id == one.Id) Live.RemoveAt(i);
                Plugin.Trace("[заявки] сервер снял заявку " + one.Id
                    + (gone == 0 ? ", строки уже не было" : gone == 1 ? "" : ", убрано строк: " + gone));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[заявки] обновление: " + e.Message);
                return true;
            }
        }

        private static void Remember(BaseEnterfightView view, int id)
        {
            try
            {
                foreach (var widget in view.Widgets)
                {
                    if (widget == null || widget.Id != id) continue;
                    var announce = AnnounceField != null ? AnnounceField.GetValue(widget) as FightAnnounce : null;
                    if (announce == null || string.IsNullOrEmpty(announce.LeaderLogin)) return;
                    Ends.Add(new Ended { Leader = announce.LeaderLogin, Kind = announce.ClaimType, Max = announce.MaxCount, At = RealTime.Now });
                    return;
                }
            }
            catch { }
        }

        private static void Stale()
        {
            if (Ends.Count == 0) return;
            float now = RealTime.Now;
            Ends.RemoveAll(e => now - e.At > 5f);
        }

        internal static bool Origin(string desc, out int kind, out int max)
        {
            kind = 0;
            max = 0;
            Stale();
            if (string.IsNullOrEmpty(desc)) return false;
            foreach (var end in Ends)
            {
                if (!desc.EndsWith(end.Leader, StringComparison.Ordinal)) continue;
                kind = end.Kind;
                max = end.Max;
                return true;
            }
            return false;
        }

        private static int Count(BaseEnterfightView view, int id)
        {
            int count = 0;
            foreach (var widget in view.Widgets)
                if (widget != null && widget.Id == id) count++;
            return count;
        }

        private static bool Here(Claim one)
        {
            try
            {
                var widgets = one.View.Widgets;
                if (widgets == null) return false;
                foreach (var widget in widgets)
                    if (widget != null && widget.Id == one.Id) return true;
                return false;
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(AbstractEnterFightController), "OnUpdateAnnounce")]
    internal static class ClaimsUpdatePatch
    {
        private static bool Prefix(AbstractEnterFightController __instance, object resp)
        {
            return Claims.Update(__instance, resp);
        }
    }

    [HarmonyPatch(typeof(BaseEnterfightView), "Add")]
    internal static class ClaimsAddPatch
    {
        private static void Postfix(BaseEnterfightView __instance, FightAnnounce data)
        {
            Claims.Note(__instance, data);
        }
    }
}
