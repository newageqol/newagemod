using System;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class Windows
    {
        internal static void Shut(string who)
        {
            try
            {
                bool menu = who == "QoLBagButton" || who == "QoLSpellButton";
                if (!menu) CloseMenu();
                if (who != "QoLDailyButton") CloseDaily();
                if (who != "QoLOnlineButton") OnlineWindow.EscapeClose();
                if (who != "QoLQuestButton") QuestWindow.EscapeClose();
                if (who != "QoLSetupButton") Settings.Close();
                if (who != Workshop.ButtonName) Workshop.Shut();
            }
            catch (Exception e) { Plugin.Trace("[окна] закрытие соседних: " + e.Message); }
        }

        private static void CloseMenu()
        {
            try
            {
                var ctrl = Controllers.Get<UserMenuController>();
                if (ctrl == null || !ctrl.IsWindowOpened) return;
                var call = AccessTools.Method(typeof(UserMenuController), "CloseWindow");
                if (call == null) { Plugin.Trace("[окна] у меню персонажа нет закрытия"); return; }
                call.Invoke(ctrl, null);
                Plugin.Trace("[окна] меню персонажа закрыто");
            }
            catch (Exception e) { Plugin.Trace("[окна] меню персонажа: " + e.Message); }
        }

        private static void CloseDaily()
        {
            try
            {
                var ctrl = DependencyContainer.GetContainer()?.Resolve<DailyTasksWindowController>();
                if (ctrl == null) return;
                ctrl.Clear();
            }
            catch (Exception e) { Plugin.Trace("[окна] задания дня: " + e.Message); }
        }
    }
}
