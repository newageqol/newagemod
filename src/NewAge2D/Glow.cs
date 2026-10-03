using UnityEngine;

namespace NewAge2D;

internal static class Glow
{
    private static readonly Color Plain = new Color(0f, 1f, 0f, 1f);
    private static readonly Color Wrong = new Color(1f, 0.15f, 0.1f, 1f);
    private static FighterDoll _lit;
    private static int _mode;

    internal static Color Paint = Plain;

    internal static readonly Color Foe = new Color(1f, 0.18f, 0.12f, 1f);

    internal static readonly Color Friend = new Color(0.25f, 1f, 0.35f, 1f);

    internal static readonly Color Reach = new Color(1f, 0.32f, 0.12f, 1f);

    internal static float Pulse => 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);

    internal static void Hint(int mode)
    {
        _mode = mode;
        Paint = mode == 2 ? Wrong : Plain;
    }

    internal static Color Side(AbstractCharacter body)
    {
        var me = Fighters.Combat()?.MyCharacter;
        return me != null && body.Team == me.Team ? Friend : Foe;
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
                if (_mode == 0) Paint = body != null ? Side(body) : Plain;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[highlight] " + ex.Message); }
        if (ReferenceEquals(want, _lit)) return;
        if (_lit != null) _lit.Glow(false);
        _lit = want;
        if (_lit != null) _lit.Glow(true);
    }
}
