using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CanvasFactory), "InternalGenerateCanvas")]
    internal static class SideButtonsWindowOpened
    {
        private static void Postfix() => SideButtons.WindowsMoved();
    }

    [HarmonyPatch(typeof(CanvasFactory), nameof(CanvasFactory.ReleaseCanvas))]
    internal static class SideButtonsWindowClosed
    {
        private static void Postfix() => SideButtons.WindowsMoved();
    }
}
