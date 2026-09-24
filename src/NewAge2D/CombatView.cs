namespace NewAge2D;

internal static class CombatView
{
    internal static CombatLocationView Get()
    {
        var view = BaseLocationView.GetInstance() as CombatLocationView;
        return view != null ? view : null;
    }
}
