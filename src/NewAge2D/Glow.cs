using UnityEngine;

namespace NewAge2D;

internal static class Glow
{
    private static readonly Color Plain = new Color(0f, 1f, 0f, 1f);
    private static readonly Color Wrong = new Color(1f, 0.15f, 0.1f, 1f);
    private static FighterDoll _lit;

    internal static Color Paint = Plain;

    internal static void Hint(int mode)
    {
        Paint = mode == 2 ? Wrong : Plain;
    }

    internal static void Tick()
    {
        FighterDoll want = null;
        try
        {
            if (Plugin.CfgHoverGlow != null && Plugin.CfgHoverGlow.Value && Plugin.FlashFight && Fighters.Combat() != null && !BodyClick.OverUi())
            {
                var body = BodyClick.Body();
                if (body != null) want = Fighters.DollOf(body);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[подсветка] " + ex.Message); }
        if (ReferenceEquals(want, _lit)) return;
        if (_lit != null) _lit.Glow(false);
        _lit = want;
        if (_lit != null) _lit.Glow(true);
    }
}
