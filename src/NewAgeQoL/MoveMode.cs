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
            Plugin.Trace("[скорость] персонаж сменился, прежнюю скорость забыл");
        }

        internal static int Current => _current;

        internal static int Wanted => _wanted;

        internal static float Left => Mathf.Max(0f, _readyAt - Time.unscaledTime);

        internal static bool Cooling => Left > 0.05f;

        internal static bool Sending => Time.unscaledTime - _sentAt < 5f && _wanted != _current;

        internal static string Name(int mode)
        {
            string got = Line("move.mode.name." + (mode + 3));
            return got.Length > 0 ? got : "режим " + mode;
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
            if (whole < 60) return whole + "с";
            int minutes = whole / 60;
            int rest = whole % 60;
            return rest == 0 ? minutes + "м" : minutes + "м " + rest + "с";
        }

        internal static void Say(string text)
        {
            Status = text ?? "";
            StatusAt = Time.unscaledTime;
            if (Status.Length > 0) Plugin.Trace("[скорость] " + Status);
        }

        internal static void Send(int mode)
        {
            try
            {
                if (mode == _current) { Say("Скорость уже " + Name(mode)); return; }
                if (Cooling) { Say("Смена скорости на перезарядке: ещё " + Clock(Left)); return; }
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { Say("Нет соединения"); return; }
                nc.SendRequest(new ChangeMoveModeRequest(mode));
                _wanted = mode;
                _sentAt = Time.unscaledTime;
                Status = "";
                Plugin.Trace("[скорость] прошу " + Name(mode) + ", номер " + mode);
            }
            catch (Exception e) { Plugin.Fault("[скорость] " + e); Say("Ошибка: " + e.Message); }
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
                else Say("Скорость осталась прежней: " + Name(mode));
            }
            Plugin.Trace("[скорость] сервер: " + Name(mode)
                         + (leftMs > 0 ? ", перезарядка " + Clock(leftMs / 1000f) : ""));
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
            catch (Exception e) { Plugin.Trace("[скорость] ответ сервера: " + e.Message); }
        }
    }
}
