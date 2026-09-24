using System;
using System.Collections.Generic;
using Transport.Messages.Common.User;
using Transport.Messages.Responses.User.Info;
using UnityEngine;

namespace NewAgeQoL
{
    internal sealed class CardRec
    {
        internal int ClassId;
        internal string ClanIcon;
        internal int ClanCode;
        internal bool Coded;
        internal int Rank;
        internal float At;
    }

    internal static class Cards
    {
        private const float Again = 60f;
        private const float Fresh = 300f;
        private const float Pace = 0.25f;

        private static readonly Dictionary<int, CardRec> Known = new Dictionary<int, CardRec>();
        private static readonly Dictionary<int, float> AskedAt = new Dictionary<int, float>();
        private static readonly List<int> Queue = new List<int>();
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        private static object _on;
        private static float _sentAt;

        internal static int Stamp { get; private set; }

        internal static void Tick()
        {
            try
            {
                Listen();
                Drain();
            }
            catch (Exception e) { Plugin.Trace("[карточка] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(433, OnCard);
            nc.AddMessageListener(433, OnCard);
            _on = nc;
        }

        private static void OnCard(object message)
        {
            try
            {
                var m = message as Unity3DUserInfoResponseMessage;
                if (m == null || m.UserId <= 0) return;
                var rec = new CardRec
                {
                    ClassId = m.ClassId,
                    ClanIcon = m.ClanIcon,
                    ClanCode = m.ClanIconCode ?? 0,
                    Coded = m.ClanIconCode.HasValue,
                    Rank = m.Rank,
                    At = Time.unscaledTime,
                };
                CardRec was;
                bool other = !Known.TryGetValue(m.UserId, out was) || Differs(was, rec);
                Known[m.UserId] = rec;
                Queue.Remove(m.UserId);
                if (!other) return;
                Stamp++;
                Plugin.Trace("[карточка] " + (m.Login ?? m.UserId.ToString()) + ": класс " + m.ClassId
                             + ", клан " + (m.ClanName ?? "нет"));
            }
            catch (Exception e) { Plugin.Trace("[карточка] ответ: " + e.Message); }
        }

        private static void Drain()
        {
            if (Queue.Count == 0) return;
            if (Time.unscaledTime - _sentAt < Pace) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;

            int userId = Queue[0];
            Queue.RemoveAt(0);
            if (userId <= 0 || Ripe(userId)) return;
            string login;
            if (!Names.TryGetValue(userId, out login)) login = null;
            _sentAt = Time.unscaledTime;
            AskedAt[userId] = Time.unscaledTime;
            nc.SendRequest(new UserInfoRequest(userId, login));
        }

        private static bool Ripe(int userId)
        {
            CardRec rec;
            return Known.TryGetValue(userId, out rec) && Time.unscaledTime - rec.At < Fresh;
        }

        private static bool Differs(CardRec was, CardRec now)
        {
            return was.ClassId != now.ClassId || was.Rank != now.Rank
                || was.Coded != now.Coded || was.ClanCode != now.ClanCode
                || was.ClanIcon != now.ClanIcon;
        }

        internal static void Ask(int userId, string login)
        {
            if (userId <= 0 || Ripe(userId) || Queue.Contains(userId)) return;
            float was;
            if (AskedAt.TryGetValue(userId, out was) && Time.unscaledTime - was < Again) return;
            if (!string.IsNullOrEmpty(login)) Names[userId] = login;
            Queue.Add(userId);
        }

        internal static bool Dress(UserRowInfoMessage row)
        {
            if (row == null) return false;
            Ask(row.UserId, row.Login);
            CardRec rec;
            if (!Known.TryGetValue(row.UserId, out rec)) return false;
            if (rec.ClassId > 0) row.ClassId = rec.ClassId;
            if (!string.IsNullOrEmpty(rec.ClanIcon)) row.ClanIcon = rec.ClanIcon;
            if (rec.Coded) row.ClanIconCode = rec.ClanCode;
            if (rec.Rank > 0) row.Rank = rec.Rank;
            return true;
        }
    }
}
