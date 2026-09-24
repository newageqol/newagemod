using System.Collections;
using System.Collections.Generic;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Store
    {
        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Queue<int> Waiting = new Queue<int>();
        private static bool _listener, _draining;

        internal static void AskNow(int thingId)
        {
            if (Ask(thingId)) Drain();
        }

        private static bool Ask(int thingId)
        {
            if (thingId <= 0 || Text(thingId) != null) return false;
            lock (Asked)
            {
                if (Asked.Contains(thingId)) return false;
                Asked.Add(thingId);
                Waiting.Enqueue(thingId);
            }
            return true;
        }

        private static void Drain()
        {
            if (_draining || Plugin.Instance == null) return;
            _draining = true;
            Plugin.Instance.StartCoroutine(DrainRoutine());
        }

        private static IEnumerator DrainRoutine()
        {
            try
            {
                while (true)
                {
                    var batch = new List<int>();
                    lock (Asked)
                        while (Waiting.Count > 0 && batch.Count < 8) batch.Add(Waiting.Dequeue());
                    if (batch.Count == 0) yield break;
                    if (!EnsureListener()) { Rewind(batch, 0); yield break; }
                    int sent = 0;
                    bool broke = false;
                    while (sent < batch.Count && !broke)
                    {
                        try { NetworkConnection.Instance.SendRequest(new GeneralThingHintRequest(batch[sent])); sent++; }
                        catch (System.Exception e) { broke = true; Plugin.Trace("[названия] запрос " + batch[sent] + " не ушёл: " + e.Message); }
                    }
                    if (broke) { Rewind(batch, sent); yield break; }
                    float t = 0f;
                    while (t < 0.05f) { yield return null; t += Time.unscaledDeltaTime; }
                }
            }
            finally { _draining = false; }
        }

        private static void Rewind(List<int> batch, int from)
        {
            lock (Asked)
                for (int i = from; i < batch.Count; i++) Waiting.Enqueue(batch[i]);
        }

        private static bool EnsureListener()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return false;
                if (_listener) return true;
                nc.AddMessageListener(385, OnThingInfo);
                nc.AddMessageListener(388, OnRecipeInfo);
                _listener = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnThingInfo(object msg)
        {
            var info = msg as GeneralThingInfoMessage;
            if (info == null || info.ThingId <= 0) return;
            Remember(info.ThingId, info.Name);
        }

        private static void OnRecipeInfo(object msg)
        {
            var info = msg as GeneralPrescriptionInfoMessage;
            if (info == null || info.ThingId <= 0) return;
            Remember(info.ThingId, info.Name);
        }

        private static string Flat(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var box = new System.Text.StringBuilder(value.Length);
            foreach (char c in value) box.Append(c == (char)13 || c == (char)10 || c == (char)9 ? ' ' : c);
            return box.ToString();
        }

        private static void Remember(int thingId, string name)
        {
            string text = string.IsNullOrEmpty(name) ? "" : Flat(name).Trim();
            SetText(thingId, text);
            ContractNumbers.Learn(thingId, text);
        }

        private class Row
        {
            internal string Image = "";
            internal string Text = "";
        }

        private static readonly Dictionary<int, Row> Rows = new Dictionary<int, Row>();

        internal static string Image(int thingId)
        {
            lock (Rows)
                return Rows.TryGetValue(thingId, out var r) && r.Image.Length > 0 ? r.Image : null;
        }

        internal static string Text(int thingId)
        {
            lock (Rows)
                return Rows.TryGetValue(thingId, out var r) && r.Text.Length > 0 ? r.Text : null;
        }

        internal static void SetImage(int thingId, string image)
        {
            if (string.IsNullOrEmpty(image)) return;
            lock (Rows) Get(thingId).Image = image;
        }

        internal static void SetText(int thingId, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (Rows)
            {
                var row = Get(thingId);
                if (row.Text.Length < text.Length) row.Text = text;
            }
        }

        private static Row Get(int thingId)
        {
            if (!Rows.TryGetValue(thingId, out var row)) Rows[thingId] = row = new Row();
            return row;
        }
    }
}
