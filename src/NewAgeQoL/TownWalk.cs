using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class TownWalk
    {
        internal static bool Busy { get; private set; }

        private static bool Onward => Plugin.CfgTownTournament != null && Plugin.CfgTownTournament.Value;

        internal static void Go()
        {
            if (Busy || Plugin.Instance == null) return;
            if (SideButtons.InCombat()) { Plugin.Trace("[город] из боя не уходим"); return; }
            Plugin.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            Busy = true;
            try
            {
                if (!Travel.Connected()) { Travel.Say("Нет соединения — телепорт недоступен."); yield break; }

                if (!Travel.InTown())
                {
                    Travel.Say(Onward ? "Возвращаюсь в город…" : "Телепорт в город…");
                    int load = Travel.LoadStamp;
                    if (!Send(new ReturnToIlleniumRequest(true))) yield break;
                    yield return Wait(() => Travel.LoadStamp != load && Travel.InTown(), 90f);
                    if (!_ok) { Travel.Say("Не дождался города."); yield break; }
                    yield return Settle();
                }

                if (!Onward) yield break;

                yield return Hop("арену", Plugin.CfgTownArenaId, Plugin.CfgTownArenaWords);
                if (!_ok) yield break;

                yield return Hop("турниры", Plugin.CfgTownTournamentId, Plugin.CfgTownTournamentWords);
                if (!_ok) yield break;

                Travel.Say("На месте — турниры.");
            }
            finally { Busy = false; }
        }

        private static bool _ok;

        private static IEnumerator Hop(string what, BepInEx.Configuration.ConfigEntry<int> byId,
                                       BepInEx.Configuration.ConfigEntry<string> byWords)
        {
            int door = byId != null && byId.Value > 0 ? byId.Value : Door(byWords);
            if (door <= 0)
            {
                Travel.Say("Не нашёл вход в " + what + " — список дверей в логе.");
                _ok = false;
                yield break;
            }

            Travel.Say("Иду в " + what + "…");
            int load = Travel.LoadStamp;
            if (!Send(new ChangeMapRequest(door))) { _ok = false; yield break; }
            yield return Wait(() => Travel.LoadStamp != load, 60f);
            if (!_ok) { Travel.Say("Не дождался перехода в " + what + "."); yield break; }
            yield return Settle();
            _ok = true;
        }

        private static int Door(BepInEx.Configuration.ConfigEntry<string> byWords)
        {
            var map = Travel.LastMap;
            if (map == null || map.SceneObjects == null) return 0;

            var words = new List<string>();
            foreach (var word in (byWords != null ? byWords.Value : "").Split(new[] { ',', ';' },
                                                                             StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = word.Trim();
                if (trimmed.Length > 0) words.Add(trimmed);
            }

            int found = 0;
            var seen = new StringBuilder();
            foreach (var door in map.SceneObjects)
            {
                if (door == null) continue;
                seen.Append(door.ObjectType).Append(" id=").Append(door.Id)
                    .Append(" name=").Append(door.SceneObjectName)
                    .Append(" text=").Append(door.Text)
                    .Append(" sprite=").Append(door.SpriteName).Append("; ");
                if (found == 0 && door.ObjectType == EObjectType.Link && Matches(door, words)) found = door.Id;
            }

            if (found > 0) Plugin.Trace("[town] дверь найдена: id=" + found + "; двери: " + seen);
            else Plugin.Warn("[town] дверь не найдена по словам «"
                                        + (byWords != null ? byWords.Value : "") + "»; двери: " + seen);
            return found;
        }

        private static bool Matches(SceneObjectInfo door, List<string> words)
        {
            foreach (var word in words)
                if (Has(door.SceneObjectName, word) || Has(door.Text, word) || Has(door.SpriteName, word))
                    return true;
            return false;
        }

        private static bool Has(string text, string word) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool Send(BaseRequest request)
        {
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) { Travel.Say("Нет соединения."); return false; }
                conn.SendRequest(request);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Fault("[town] отправка: " + e.Message);
                Travel.Say("Ошибка отправки: " + e.Message);
                return false;
            }
        }

        private static IEnumerator Settle()
        {
            float t = 0f;
            while (t < 0.2f) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator Wait(Func<bool> done, float timeout)
        {
            _ok = false;
            float t = 0f;
            while (true)
            {
                if (done()) { _ok = true; yield break; }
                if (Travel.InCombat() || !Travel.Connected()) yield break;
                yield return null;
                t += Time.unscaledDeltaTime;
                if (t >= timeout) yield break;
            }
        }
    }
}
