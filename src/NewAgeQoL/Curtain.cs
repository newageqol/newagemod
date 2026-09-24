using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Curtain
    {
        private const float Longest = 3f;

        private sealed class Veil
        {
            internal CanvasGroup Group;
            internal float Was;
            internal float At;
            internal bool Own;
            internal bool Patient;
        }

        private static readonly Dictionary<int, Veil> Held = new Dictionary<int, Veil>();
        private static readonly List<int> Gone = new List<int>();

        internal static void Hide(Component part)
        {
            Hide(part, false);
        }

        internal static void Hide(Component part, bool patient)
        {
            if (part == null) return;
            try
            {
                int key = part.GetInstanceID();
                Veil had;
                if (Held.TryGetValue(key, out had))
                {
                    had.At = Time.unscaledTime;
                    if (patient) had.Patient = true;
                    return;
                }
                var group = part.GetComponent<CanvasGroup>();
                bool own = group == null;
                if (own) group = part.gameObject.AddComponent<CanvasGroup>();
                Held[key] = new Veil { Group = group, Was = group.alpha, At = Time.unscaledTime, Own = own, Patient = patient };
                group.alpha = 0f;
            }
            catch (Exception e) { Plugin.Trace("[занавес] " + e.Message); }
        }

        internal static void Show(Component part)
        {
            if (part == null) return;
            Drop(part.GetInstanceID());
        }

        private const float Settle = 12f;
        private const int Calm = 6;
        private const float Brief = 1.2f;

        private static readonly List<Transform> Staged = new List<Transform>();
        private static bool _staging, _keyed;
        private static float _stageAt;
        private static int _seen, _still;

        internal static void Raise()
        {
            for (int i = 0; i < Staged.Count; i++) Show(Staged[i]);
            if (Staged.Count > 0) Plugin.Trace("[занавес] прошлая сцена не досчиталась, снял панелей: " + Staged.Count);
            Staged.Clear();
            _staging = true;
            _keyed = false;
            _stageAt = Time.unscaledTime;
            _seen = 0;
            _still = 0;
        }

        internal static void Stage(GameObject go)
        {
            Stage(go, false);
        }

        internal static void Stage(GameObject go, bool key)
        {
            if (!_staging || go == null) return;
            Hide(go.transform, true);
            Staged.Add(go.transform);
            if (key) _keyed = true;
            _still = 0;
        }

        internal static void Tick()
        {
            Settling();
            if (Held.Count == 0) return;
            Gone.Clear();
            foreach (var pair in Held)
                if (pair.Value.Group == null || (!pair.Value.Patient && Time.unscaledTime - pair.Value.At > Longest)) Gone.Add(pair.Key);
            for (int i = 0; i < Gone.Count; i++) Drop(Gone[i]);
        }

        private static bool Ready
        {
            get
            {
                try { return ChatDock.Active; }
                catch { return true; }
            }
        }

        private static void Settling()
        {
            if (!_staging) return;
            if (Staged.Count == _seen) _still++;
            else { _seen = Staged.Count; _still = 0; }
            bool waited = Time.unscaledTime - _stageAt > Brief;
            bool ripe = Staged.Count > 0 && _still >= Calm && (_keyed || Ready || waited);
            if (!ripe && Time.unscaledTime - _stageAt < Settle) return;

            _staging = false;
            for (int i = 0; i < Staged.Count; i++) Show(Staged[i]);
            if (Staged.Count > 0) Plugin.Trace("[занавес] показано разом панелей: " + Staged.Count);
            Staged.Clear();
        }

        private static void Drop(int key)
        {
            Veil veil;
            if (!Held.TryGetValue(key, out veil)) return;
            Held.Remove(key);
            try
            {
                if (veil.Group == null) return;
                if (veil.Own) UnityEngine.Object.Destroy(veil.Group);
                else veil.Group.alpha = veil.Was;
            }
            catch (Exception e) { Plugin.Trace("[занавес] снятие: " + e.Message); }
        }
    }
}
