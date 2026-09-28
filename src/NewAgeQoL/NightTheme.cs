using System;
using System.Collections;
using BepInEx.Configuration;

namespace NewAgeQoL
{
    internal static class NightTheme
    {
        internal enum Pack { Unknown, Failed, Current, Missing, Outdated }

        internal static ConfigEntry<bool> Enabled;

        internal static bool On => Enabled == null || Enabled.Value;

        private static bool _running;
        private static Action _repaint;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Look", "NightTheme", false,
                "Ночная тема: ночной Иллениум, башня магии в 3D и свой экран загрузки. Включается и выключается целиком.");
            Apply();
            Enabled.SettingChanged += (s, e) => Apply();
        }

        internal static void Download() => Start(true);

        internal static void Switched(bool on, Action repaint)
        {
            if (!on) return;
            _repaint = repaint;
            Start(false);
        }

        internal static string ButtonText()
        {
            if (TownFiles.Missing || TowerFiles.Missing) return "Скачать";
            if (TownFiles.Behind || TowerFiles.Behind) return "Обновить";
            return "Проверить";
        }

        internal static string RowTitle()
        {
            if (TownFiles.Missing || TowerFiles.Missing) return "Ночная тема: файлы не скачаны";
            if (TownFiles.Behind || TowerFiles.Behind) return "Ночная тема: на сервере новая версия";
            return "Ночная тема: проверить новую версию";
        }

        private static void Start(bool manual)
        {
            if (Plugin.Instance == null) return;
            if (_running || TownFiles.Busy || TowerFiles.Busy)
            {
                Notice.Show("Ночная тема уже проверяется или качается, подожди немного.", 4f);
                return;
            }
            _running = true;
            Notice.Show("Проверяю ночную тему…", 3f);
            Plugin.Instance.StartCoroutine(Check(manual, false));
        }

        internal static void Offer()
        {
            if (Plugin.Instance == null || _running || TownFiles.Busy || TowerFiles.Busy) return;
            _running = true;
            Plugin.Instance.StartCoroutine(Check(false, true));
        }

        private static IEnumerator Check(bool manual, bool quiet)
        {
            try
            {
                yield return TownFiles.Probe();
                yield return TowerFiles.Probe();
            }
            finally { _running = false; }
            var town = TownFiles.Last;
            var tower = TowerFiles.Last;
            Plugin.Log.LogInfo("[theme] check: town " + town + ", tower " + tower);
            if (town == Pack.Failed || tower == Pack.Failed)
            {
                if (quiet) yield break;
                Notice.Show("Не удалось проверить ночную тему: сервер с файлами не ответил. Попробуй позже кнопкой в настройках мода.", 6f);
                yield break;
            }
            bool needTown = town != Pack.Current;
            bool needTower = tower != Pack.Current;
            if (!needTown && !needTower)
            {
                if (quiet) yield break;
                Notice.Show("У тебя последняя версия ночной темы.", 5f);
                yield break;
            }
            if (manual)
            {
                Plugin.Instance.StartCoroutine(Get(needTown, needTower));
                yield break;
            }
            bool missing = town == Pack.Missing || tower == Pack.Missing;
            long size = (needTown ? TownFiles.Need : 0) + (needTower ? TowerFiles.Need : 0);
            string text = (missing ? "Для ночной темы нужно скачать файлы" : "Вышло обновление ночной темы")
                + ": " + Mb(size) + " МБ на диске. Скачать сейчас? Загрузка идёт в фоне и не мешает игре, в бою встаёт на паузу. "
                + (missing ? "Если откажешься, ночная тема выключится." : "Если откажешься, останется прежняя версия, обновить можно кнопкой в настройках мода.");
            try
            {
                DialogFactory.ShowConfirmMessageBox("dialogs.artworkshop.confirm.caption", result =>
                {
                    if (result == EMessageBoxResult.MB_OK)
                    {
                        if (Enabled != null) Settings.Keep(Enabled);
                        if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Get(needTown, needTower));
                        return;
                    }
                    Plugin.Trace("[theme] player declined theme files");
                    if (needTown) TownFiles.Seen();
                    if (needTower) TowerFiles.Seen();
                    if (!missing || Enabled == null) return;
                    Enabled.Value = false;
                    Settings.Keep(Enabled);
                    try { _repaint?.Invoke(); } catch { }
                    Plugin.Trace("[theme] night theme switched off: files declined");
                }, text);
            }
            catch (Exception e) { Plugin.Trace("[theme] ask dialog: " + e.Message); }
        }

        private static IEnumerator Get(bool town, bool tower)
        {
            if (town) yield return TownFiles.Fetch();
            if (tower) yield return TowerFiles.Fetch();
        }

        private static string Mb(long bytes) => Math.Max(1L, (bytes + 512L * 1024L) / (1024L * 1024L)).ToString();

        private static void Apply()
        {
            bool on = Enabled.Value;
            if (NightTown.Enabled != null && NightTown.Enabled.Value != on) NightTown.Enabled.Value = on;
            if (MagicTower.Enabled != null && MagicTower.Enabled.Value != on) MagicTower.Enabled.Value = on;
            Plugin.Trace("[theme] night theme " + (on ? "on" : "off"));
        }
    }
}
