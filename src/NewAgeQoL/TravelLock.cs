using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new System.Type[] { typeof(BaseRequest) })]
    public static class TravelLockPatch
    {
        private static bool Prefix(BaseRequest request)
        {
            if (!Artifacts.Busy || Artifacts.Internal) return true;
            if (!Blocked(request)) return true;

            Plugin.Trace("[art] не пускаю " + request.GetType().Name + ", идёт работа с хранилищем");
            Refuse();
            return false;
        }

        internal static void Refuse()
        {
            try { Preloader.Close(); } catch { }
            try { AirMessageScript.ShowInformationNotification("Подожди, идёт работа с хранилищем"); }
            catch { }
        }

        private static bool Blocked(BaseRequest request)
        {
            return request is ChangeMapRequest
                || request is ReturnToIlleniumRequest
                || request is BeginMoveRequest
                || request is GlobalMapActionRequest
                || request is LeaveTownRequest;
        }
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "CheckChangeLocationEvent")]
    public static class TravelLockLinkPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result) return;
            if (!Artifacts.Busy || Artifacts.Internal) return;
            __result = false;
            Plugin.Trace("[art] не пускаю смену локации, идёт работа с хранилищем");
            TravelLockPatch.Refuse();
        }
    }
}
