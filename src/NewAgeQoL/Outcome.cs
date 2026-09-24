using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Outcome
    {
        internal struct Shift
        {
            internal int Life;
            internal int Mana;
            internal int Stamina;
            internal int Expower;
        }

        private static readonly Dictionary<int, Shift> Now = new Dictionary<int, Shift>();
        private static readonly HashSet<AnimationItem> SeenItems = new HashSet<AnimationItem>();
        private static readonly HashSet<AnimationGroup> SeenGroups = new HashSet<AnimationGroup>();
        private static AccessTools.FieldRef<AnimationProcessor, List<AnimationGroup>> _waiting;
        private static AccessTools.FieldRef<AnimationProcessor, List<AnimationGroup>> _playing;
        private static AccessTools.FieldRef<ChangeLifeAnimationItem, float?> _started;
        private static bool _looked;
        private static int _frame = -1;

        internal static Shift Of(ICombatData cd, int userId)
        {
            Collect(cd);
            Shift shift;
            return Now.TryGetValue(userId, out shift) ? shift : default(Shift);
        }

        private static void Collect(ICombatData cd)
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Now.Clear();
            SeenItems.Clear();
            SeenGroups.Clear();
            if (cd == null) return;
            Look();
            if (_waiting == null || _playing == null || _started == null) return;
            try
            {
                var processor = cd.AnimationProcessor;
                if (processor == null || processor.GroupCount == 0) return;
                Walk(_playing(processor));
                Walk(_waiting(processor));
            }
            catch (Exception e)
            {
                Now.Clear();
                Plugin.Trace("[итог] очередь анимаций: " + e.Message);
            }
        }

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                _waiting = AccessTools.FieldRefAccess<AnimationProcessor, List<AnimationGroup>>("groups");
                _playing = AccessTools.FieldRefAccess<AnimationProcessor, List<AnimationGroup>>("activeGroups");
                _started = AccessTools.FieldRefAccess<ChangeLifeAnimationItem, float?>("_startTime");
            }
            catch (Exception e) { Plugin.Trace("[итог] поля очереди анимаций: " + e.Message); }
        }

        private static void Walk(List<AnimationGroup> groups)
        {
            if (groups == null) return;
            foreach (var group in groups) Walk(group);
        }

        private static void Walk(AnimationGroup group)
        {
            if (group == null || !SeenGroups.Add(group)) return;
            Walk(group.Items);
            Walk(group.wounds);
            Walk(group.blocks);
            Walk(group.GluedGroups);
        }

        private static void Walk(List<AnimationItem> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null || !SeenItems.Add(item)) continue;
                var change = item as ChangeLifeAnimationItem;
                if (change != null && change.Target != null && !_started(change).HasValue) Add(change);
                Walk(item.Items);
            }
        }

        private static void Add(ChangeLifeAnimationItem change)
        {
            int id = change.Target.UserId;
            Shift shift;
            Now.TryGetValue(id, out shift);
            switch (change.AnimationName)
            {
                case AnimationItem.CHANGE_LIFE_ANIMATION: shift.Life += change.life; break;
                case AnimationItem.CHANGE_MANA_ANIMATION: shift.Mana += change.life; break;
                case AnimationItem.CHANGE_EXPOWER: shift.Expower += change.life; break;
                default: shift.Stamina += change.life; break;
            }
            Now[id] = shift;
        }
    }
}
