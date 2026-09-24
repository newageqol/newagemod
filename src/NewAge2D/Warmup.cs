using UnityEngine;

namespace NewAge2D;

internal static class Warmup
{
    private static readonly int[] Races = { 8, 9, 10, 11, 12, 13 };
    private static readonly int[] Genders = { 1, 2 };

    private static float _at;
    private static bool _done;

    internal static void Forget()
    {
        _done = false;
        _at = 0f;
    }

    internal static void Tick()
    {
        if (_done || Plugin.Store == null || !Plugin.FlashFight) return;
        if (Time.unscaledTime < _at) return;
        _at = Time.unscaledTime + 5f;
        if (BaseLocationView.GetInstance() == null) return;

        var want = new List<string> { "client.swf", "effects.swf" };
        foreach (int race in Races)
            foreach (int gender in Genders)
            {
                string body = Doll.BodyFile(race, gender);
                if (body != null && !want.Contains(body)) want.Add(body);
            }

        _done = true;
        Plugin.Store.Pull(want);
        Plugin.Log.LogInfo($"[файлы] вход в игру: проверяю {want.Count} файлов, чего нет — качаю заранее, чтобы к бою всё было на месте");
    }
}
