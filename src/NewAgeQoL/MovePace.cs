using System;
using System.Reflection;
using HarmonyLib;
using SWS;
using Transport.Messages.Common.List;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class MovePace
    {
        private const float Margin = 0.5f;

        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(GlobalMapController), "_target");
        private static readonly FieldInfo MoverField = AccessTools.Field(typeof(GlobalMapController), "_mover");
        private static readonly MethodInfo ToggleMethod = AccessTools.Method(typeof(GlobalMapController), "ToggleAnimation");

        private static float _due;

        internal static void Heard(ThreeIntMessage msg)
        {
            if (msg == null) return;
            int left = msg.Value3;
            Plugin.Trace("[скорость] режим " + msg.Value1 + ", таймер " + left + " из " + msg.Value2 + " мс");
            if (left > 0)
            {
                _due = Time.unscaledTime + left / 1000f + Margin;
                return;
            }
            if (_due <= 0f) return;
            _due = 0f;
            Resend("сервер прислал режим без таймера");
        }

        internal static void Tick()
        {
            if (_due <= 0f || Time.unscaledTime < _due) return;
            _due = 0f;
            Resend("таймер режима вышел");
        }

        private static void Resend(string why)
        {
            try
            {
                var map = Controllers.Get<GlobalMapController>();
                if (map == null || !map.IsMoving()) return;
                var target = TargetField?.GetValue(map) as GlobalMapVertex;
                if (target == null) return;
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) return;
                (MoverField?.GetValue(map) as splineMove)?.Stop();
                ToggleMethod?.Invoke(map, new object[] { false });
                conn.SendRequest(new BeginMoveRequest(target.Id));
                Plugin.Trace("[скорость] " + why + ": иду к v" + target.Id + " заново, с новой скоростью");
            }
            catch (Exception e) { Plugin.Trace("[скорость] повтор хода: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GlobalMapQuickslotController), "OnMoveModeResponse")]
    internal static class MovePacePatch
    {
        private static void Postfix(object msg)
        {
            try { MovePace.Heard(msg as ThreeIntMessage); }
            catch (Exception e) { Plugin.Trace("[скорость] ответ 155: " + e.Message); }
        }
    }
}
