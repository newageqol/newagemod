using System;
using System.Collections.Generic;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class TeamFights
    {
        internal const int Room = 19;
        internal const int Arena = 3;
        internal const int Chaos = 14;
        internal const int Tournament = 334;
        internal const string Button = "Командные";
        internal const string Caption = "Командные бои";

        internal static int Here()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null ? ud.MapId : 0;
            }
            catch { return 0; }
        }

        internal static bool Back(SceneObjectInfo info) =>
            info != null && info.ObjectType == EObjectType.Link && info.InteractionType == EInteractionType.BottomPanelReturnButton;
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "RegisterInteractionHandlers")]
    internal static class TeamFightsDoorPatch
    {
        private static void Prefix(StaticLocationLoadController __instance)
        {
            try
            {
                var field = Traverse.Create(__instance).Field("_sceneObjectInfos");
                var infos = field.GetValue<IList<SceneObjectInfo>>();
                if (infos == null) return;
                int here = TeamFights.Here();
                if (here == TeamFights.Room) { Leave(field, infos); return; }
                SceneObjectInfo pattern = null;
                bool chaos = false;
                bool door = false;
                foreach (var info in infos)
                {
                    if (info == null || info.ObjectType != EObjectType.Link) continue;
                    if (info.Id == TeamFights.Room) door = true;
                    if (info.Id == TeamFights.Chaos) chaos = true;
                    if (info.Id == TeamFights.Tournament) pattern = info;
                }
                if (!chaos || pattern == null || door || pattern.InteractionType != EInteractionType.BottomPanelButton) return;
                var list = new List<SceneObjectInfo>(infos);
                list.Insert(list.IndexOf(pattern) + 1,
                    new SceneObjectInfo(TeamFights.Room, pattern.SceneObjectName, pattern.InteractionType, EObjectType.Link,
                        TeamFights.Button, pattern.AtlasType, pattern.SpriteName) { HintId = pattern.HintId });
                field.SetValue(list);
                Plugin.Trace("[командные] на арене добавлена кнопка в локацию " + TeamFights.Room);
            }
            catch (Exception e) { Plugin.Trace("[командные] кнопка на арене: " + e.Message); }
        }

        private static void Leave(Traverse field, IList<SceneObjectInfo> infos)
        {
            const int arena = TeamFights.Arena;
            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                if (!TeamFights.Back(info)) continue;
                if (info.Id == arena) return;
                var list = new List<SceneObjectInfo>(infos);
                list[i] = new SceneObjectInfo(arena, info.SceneObjectName, info.InteractionType, EObjectType.Link,
                    info.Text, info.AtlasType, info.SpriteName) { HintId = info.HintId };
                field.SetValue(list);
                Plugin.Trace("[командные] выход ведёт на арену " + arena + " вместо " + info.Id);
                return;
            }
            Plugin.Trace("[командные] кнопки выхода среди объектов локации нет");
        }
    }

    [HarmonyPatch(typeof(AbstractEnterFightController), "OnInjected")]
    internal static class TeamFightsCaptionPatch
    {
        private static void Postfix(AbstractEnterFightController __instance)
        {
            try
            {
                var ud = Controllers.User;
                if (ud == null || ud.MapId != TeamFights.Room) return;
                if (ResourceStrings.IsKeyPresent("location.caption." + TeamFights.Room)) return;
                var view = Traverse.Create(__instance).Property("EnterfightView").GetValue<BaseEnterfightView>();
                if (view != null) view.SetCaption(TeamFights.Caption);
            }
            catch (Exception e) { Plugin.Trace("[командные] заголовок: " + e.Message); }
        }
    }
}
