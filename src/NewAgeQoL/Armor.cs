using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Transport.Messages.Responses.Gui.Dialogs.Monsterinfo;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class ArmorRec
    {
        internal int Head, Body, Left, Right, Legs;
        internal int Black, White, Astral;
    }

    internal static class Armor
    {
        private const float Again = 30f;

        private static readonly Dictionary<int, ArmorRec> Known = new Dictionary<int, ArmorRec>();
        private static readonly Dictionary<int, int> Beasts = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> AskedAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> HeardAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> AskedRace = new Dictionary<int, float>();
        private static object _on;

        internal static int Version;

        internal static bool On
        {
            get { return Plugin.CfgArmorZones == null || Plugin.CfgArmorZones.Value; }
        }

        internal static void Tick()
        {
            try { Listen(); }
            catch (Exception e) { Plugin.Trace("[броня] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(433, OnInfo);
            nc.AddMessageListener(433, OnInfo);
            nc.RemoveMessageListener(472, OnBeast);
            nc.AddMessageListener(472, OnBeast);
            _on = nc;
        }

        private static void OnInfo(object message)
        {
            try { Remember(message as Unity3DUserInfoResponseMessage); }
            catch (Exception e) { Plugin.Trace("[броня] ответ: " + e.Message); }
        }

        internal static void Remember(Unity3DUserInfoResponseMessage m)
        {
            if (m == null || m.UserId <= 0) return;
            var rec = new ArmorRec
            {
                Head = m.HeadArmor,
                Body = m.BodyArmor,
                Left = m.LeftHandArmor,
                Right = m.RightHandArmor,
                Legs = m.LagsArmor,
                Black = m.BlackMagicProtection,
                White = m.WhiteMagicProtection,
                Astral = m.AstralMagicProtection,
            };
            bool had = Known.ContainsKey(m.UserId);
            Known[m.UserId] = rec;
            HeardAt[m.UserId] = Time.unscaledTime;
            Version++;
            if (!had) Plugin.Trace("[броня] " + (m.Login ?? m.UserId.ToString()) + ": " + Zones(m.UserId));
        }

        private static void OnBeast(object message)
        {
            try
            {
                var m = message as Unity3DMonsterInfoResponseMessage;
                if (m == null || m.Id <= 0) return;
                bool had = Beasts.ContainsKey(m.Id);
                Beasts[m.Id] = m.Armor;
                Version++;
                if (!had) Plugin.Trace("[броня] порода " + m.Id + ": " + m.Armor + " по всем зонам");
            }
            catch (Exception e) { Plugin.Trace("[броня] монстр: " + e.Message); }
        }

        internal static void AskBeast(int race)
        {
            if (race <= 0) return;
            float was;
            if (AskedRace.TryGetValue(race, out was) && Time.unscaledTime - was < Again) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            Listen();
            AskedRace[race] = Time.unscaledTime;
            nc.SendRequest(new Unity3DMonsterInfoRequest(race));
        }

        internal static int Beast(int race)
        {
            int armor;
            return Beasts.TryGetValue(race, out armor) ? armor : -1;
        }

        internal static void Ask(int userId, string login)
        {
            if (userId <= 0) return;
            float was;
            if (AskedAt.TryGetValue(userId, out was) && Time.unscaledTime - was < Again) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            AskedAt[userId] = Time.unscaledTime;
            nc.SendRequest(new UserInfoRequest(userId, login));
        }

        internal static bool Has(int userId)
        {
            return Known.ContainsKey(userId);
        }

        internal static void AskNow(int userId, string login)
        {
            if (userId <= 0) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            Listen();
            AskedAt[userId] = Time.unscaledTime;
            nc.SendRequest(new UserInfoRequest(userId, login));
        }

        internal static bool HeardSince(int userId, float since)
        {
            float at;
            return HeardAt.TryGetValue(userId, out at) && at >= since;
        }

        internal static int Zone(int userId, TargetBody part)
        {
            ArmorRec r;
            if (!Known.TryGetValue(userId, out r)) return -1;
            switch (part)
            {
                case TargetBody.Head: return r.Head;
                case TargetBody.Body: return r.Body;
                case TargetBody.LeftHand: return r.Left;
                case TargetBody.RightHand: return r.Right;
                case TargetBody.Legs: return r.Legs;
                default: return -1;
            }
        }

        internal static string Zones(int userId)
        {
            ArmorRec r;
            if (!Known.TryGetValue(userId, out r)) return "";
            var sb = new StringBuilder();
            sb.Append("голова ").Append(r.Head);
            sb.Append("   корпус ").Append(r.Body);
            sb.Append("   руки ").Append(r.Left).Append('/').Append(r.Right);
            sb.Append("   ноги ").Append(r.Legs);
            return sb.ToString();
        }

        internal static string Magic(int userId)
        {
            ArmorRec r;
            if (!Known.TryGetValue(userId, out r)) return "";
            var sb = new StringBuilder();
            sb.Append("полнолуние ").Append(r.Black);
            sb.Append("   рассвет ").Append(r.White);
            sb.Append("   астрал ").Append(r.Astral);
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(GeneralUserInfoPanelContent), "Init")]
    internal static class UserInfoCardPatch
    {
        private static void Postfix(GeneralUserInfoPanelContent __instance, Unity3DUserInfoResponseMessage message)
        {
            try
            {
                Armor.Remember(message);
                PlayerCard.Show(__instance, message);
            }
            catch (Exception e) { Plugin.Trace("[броня] карточка: " + e.Message); }
        }
    }
}
