using System;
using HarmonyLib;
using Transport.Messages.Common.List;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class MoveMode
    {
        internal const int Normal = 0;

        internal static readonly int[] All = { -2, -1, 0, 1, 2 };

        private static int _current = Normal;
        private static int _wanted = Normal;
        private static float _readyAt;
        private static float _sentAt = -100f;

        internal static string Status = "";
        internal static float StatusAt;

        internal static bool Ready => Quickslots.OnMap;

        internal static void Forget()
        {
            _current = Normal;
            _wanted = Normal;
            _readyAt = 0f;
            _sentAt = -100f;
            Status = "";
            StatusAt = 0f;
            Plugin.Trace("[speed] character changed, previous speed forgotten");
        }

        internal static int Current => _current;

        internal static int Wanted => _wanted;

        internal static float Left => Mathf.Max(0f, _readyAt - Time.unscaledTime);

        internal static bool Cooling => Left > 0.05f;

        internal static bool Sending => Time.unscaledTime - _sentAt < 5f && _wanted != _current;

        internal static string Name(int mode)
        {
            string got = Line("move.mode.name." + (mode + 3));
            return got.Length > 0 ? got : Lang.F("режим {0}", mode);
        }

        private static string Line(string key)
        {
            try
            {
                string got = ResourceStrings.GetString(key);
                return string.IsNullOrEmpty(got) || got == key ? "" : got.Trim();
            }
            catch { return ""; }
        }

        internal static string Clock(float seconds)
        {
            int whole = Mathf.CeilToInt(seconds);
            if (whole < 60) return Lang.F("{0}с", whole);
            int minutes = whole / 60;
            int rest = whole % 60;
            return rest == 0 ? Lang.F("{0}м", minutes) : Lang.F("{0}м {1}с", minutes, rest);
        }

        internal static void Say(string text)
        {
            Status = text ?? "";
            StatusAt = Time.unscaledTime;
            if (Status.Length > 0) Plugin.Trace("[speed] " + Status);
        }

        internal static void Send(int mode)
        {
            try
            {
                if (mode == _current) { Say(Lang.F("Скорость уже {0}", Name(mode))); return; }
                if (Cooling) { Say(Lang.F("Смена скорости на перезарядке: ещё {0}", Clock(Left))); return; }
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { Say(Lang.T("Нет соединения")); return; }
                nc.SendRequest(new ChangeMoveModeRequest(mode));
                _wanted = mode;
                _sentAt = Time.unscaledTime;
                Status = "";
                Plugin.Trace("[speed] requesting " + Name(mode) + ", number " + mode);
            }
            catch (Exception e) { Plugin.Fault("[speed] " + e); Say(Lang.F("Ошибка: {0}", e.Message)); }
        }

        internal static void Note(int mode, int leftMs)
        {
            bool mine = Time.unscaledTime - _sentAt < 10f;
            bool other = mode != _current;
            _current = mode;
            _wanted = mode;
            _readyAt = leftMs > 0 ? Time.unscaledTime + leftMs / 1000f : 0f;
            if (mine)
            {
                _sentAt = -100f;
                if (other) Status = "";
                else Say(Lang.F("Скорость осталась прежней: {0}", Name(mode)));
            }
            Plugin.Trace("[speed] server: " + Name(mode)
                         + (leftMs > 0 ? ", cooldown " + Clock(leftMs / 1000f) : ""));
        }
    }

    [HarmonyPatch(typeof(GlobalMapQuickslotController), "OnMoveModeResponse")]
    public static class MoveModeResponsePatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                var three = msg as ThreeIntMessage;
                if (three == null) return;
                MoveMode.Note(three.Value1, three.Value3);
            }
            catch (Exception e) { Plugin.Trace("[speed] server response: " + e.Message); }
        }
    }
}
