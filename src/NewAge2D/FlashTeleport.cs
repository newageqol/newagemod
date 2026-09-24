using System.Reflection;
using HarmonyLib;
using Model.Combat.Animation;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class FlashTeleport
{
    private const float Frame = 0.05f;
    private const float Step = 0.1f;

    private sealed class Jump
    {
        public AbstractCharacter Who;
        public int Phase;
        public int More;
        public float Alpha = 1f;
        public float NextAt;
    }

    private static readonly List<Jump> Jumps = new();
    private static readonly FieldInfo CharactersReadyField = AccessTools.Field(typeof(CombatController), "_charactersInitialized");

    private static string NameOf(AbstractCharacter character) =>
        character == null ? "-" : !string.IsNullOrEmpty(character.Login) ? character.Login : character.UserId.ToString();

    [HarmonyPrefix, HarmonyPatch(typeof(CombatController), "OnTeleportResponse")]
    private static bool Queue(CombatController __instance, object msg)
    {
        if (!Plugin.FlashFight || !(msg is Transport.Messages.Responses.Combat.TeleportResponseMessage message)) return true;
        try
        {
            if (CharactersReadyField != null && !(CharactersReadyField.GetValue(__instance) is bool ready && ready)) return true;
            var combat = Fighters.Combat();
            if (combat == null || !combat.Characters.TryGetValue(message.UserId, out var who)) return true;
            var doll = Fighters.DollOf(who);
            if (doll != null && doll.Placed && !doll.Hold.HasValue) doll.Hold = doll.Feet;
            combat.CharacterTeleported(message.UserId, OffsetCoord.createByServerCoords(message.X, message.Y));
            if (doll == null || !doll.Hold.HasValue) return false;
            var jump = Jumps.FirstOrDefault(known => ReferenceEquals(known.Who, who));
            if (jump == null)
            {
                jump = new Jump { Who = who };
                Jumps.Add(jump);
                Begin(jump);
            }
            else if (jump.Phase == 3) jump.More++;
            if (Trace.On) Trace.Write($"«{NameOf(who)}» перемещение умением на {message.X};{message.Y}: клетка сменилась сразу, кукла исчезает и появляется, как во Flash");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[бой] перемещение: " + ex.Message);
            return true;
        }
    }

    private static void Begin(Jump jump)
    {
        jump.Phase = -3;
        jump.NextAt = Time.time;
    }

    internal static void Tick()
    {
        if (Jumps.Count == 0) return;
        float now = Time.time;
        var combat = Fighters.Combat();
        for (int i = Jumps.Count - 1; i >= 0; i--)
        {
            var jump = Jumps[i];
            var doll = Fighters.DollOf(jump.Who);
            if (doll == null || combat == null || !combat.Characters.TryGetValue(jump.Who.UserId, out var current) || !ReferenceEquals(current, jump.Who))
            {
                Jumps.RemoveAt(i);
                continue;
            }
            if (jump.Phase < 3 && jump.Who.Initialized && jump.Who.MoverState != MoverState.Idle)
            {
                if (Trace.On) Trace.Write($"«{NameOf(jump.Who)}» пошёл до конца перемещения: кукла переставлена сразу");
                Finish(doll);
                Jumps.RemoveAt(i);
                continue;
            }
            if (now < jump.NextAt) continue;
            jump.NextAt += Frame;
            if (jump.NextAt < now) jump.NextAt = now + Frame;
            try
            {
                if (Advance(jump, doll)) Jumps.RemoveAt(i);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[бой] перемещение: " + ex);
                Finish(doll);
                Jumps.RemoveAt(i);
            }
        }
    }

    private static bool Advance(Jump jump, FighterDoll doll)
    {
        switch (jump.Phase)
        {
            case -3:
                jump.Phase = -2;
                return false;
            case -2:
                jump.Phase = -1;
                return false;
            case -1:
                if (jump.Who.Initialized && !FlashQueue.ClipReady(jump.Who)) return false;
                jump.Phase = 2;
                return false;
            case 2:
                if (jump.Alpha > 0f)
                {
                    jump.Alpha = Mathf.Max(0f, jump.Alpha - Step);
                    doll.TeleportAlpha = jump.Alpha;
                    return false;
                }
                doll.Hold = null;
                doll.Jump("перемещение умением");
                jump.Phase = 3;
                return false;
            default:
                if (jump.Alpha < 1f)
                {
                    jump.Alpha = Mathf.Min(1f, jump.Alpha + Step);
                    doll.TeleportAlpha = jump.Alpha;
                    return false;
                }
                if (jump.More == 0)
                {
                    if (Trace.On) Trace.Write($"«{NameOf(jump.Who)}» перемещение закончено");
                    return true;
                }
                jump.More--;
                Begin(jump);
                return false;
        }
    }

    private static void Finish(FighterDoll doll)
    {
        doll.TeleportAlpha = 1f;
        if (!doll.Hold.HasValue) return;
        doll.Hold = null;
        doll.Jump("перемещение умением");
    }
}
