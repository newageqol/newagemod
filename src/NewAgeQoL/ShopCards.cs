using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class CardHome : MonoBehaviour
    {
        internal Vector2 Size;
    }

    internal sealed class CardSize : MonoBehaviour
    {
        internal Vector2 Want;
        internal float Small = 1f;

        private RectTransform _rt;
        private bool _busy;

        private void OnEnable() { Apply(); }

        private void OnRectTransformDimensionsChange() { Apply(); }

        internal void Apply()
        {
            if (_busy || Want.y < 1f) return;
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;
            _busy = true;
            try
            {
                var now = _rt.rect.size;
                if (Mathf.Abs(now.x - Want.x) > 0.5f || Mathf.Abs(now.y - Want.y) > 0.5f) _rt.sizeDelta = Want;
                var scale = transform.localScale;
                if (Mathf.Abs(scale.x - Small) > 0.001f || Mathf.Abs(scale.y - Small) > 0.001f)
                    transform.localScale = new Vector3(Small, Small, 1f);
            }
            finally { _busy = false; }
        }
    }

    internal static class ShopCards
    {
        private const float Floor = 0.6f;
        private const BindingFlags Deep = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static float _next;
        private static float _lookAt;
        private static readonly List<Component> Grids = new List<Component>();
        private static readonly Dictionary<Type, FieldInfo> Sizes = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, FieldInfo> Flags = new Dictionary<Type, FieldInfo>();

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.15f;
            if (!SideButtons.InWorld() || SideButtons.InCombat()) { Grids.Clear(); return; }
            try
            {
                Grids.RemoveAll(one => one == null || !one.gameObject.activeInHierarchy);
                bool hot = SideButtons.WindowsHot || SideButtons.SceneFresh;
                if (hot || (Grids.Count == 0 && Time.unscaledTime >= _lookAt))
                {
                    _lookAt = Time.unscaledTime + 1f;
                    Grids.Clear();
                    Grids.AddRange(UnityEngine.Object.FindObjectsOfType<ShopThingGrid>());
                    Grids.AddRange(UnityEngine.Object.FindObjectsOfType<MagicShopGrid>());
                }
                foreach (var grid in Grids) Fit(grid);
            }
            catch (Exception e) { Plugin.Trace("[карточки] " + e.Message); }
        }

        private static void Fit(Component grid)
        {
            if (grid == null || !grid.gameObject.activeInHierarchy) return;
            var layout = grid.GetComponentInChildren<GridLayoutGroup>(true);
            var filter = grid.GetComponentInChildren<RectTransformDimensionsChangeFilter>(true);
            var field = SizeField(grid.GetType());
            var sheet = filter != null ? filter.transform as RectTransform : grid.transform as RectTransform;
            if (layout == null || sheet == null || field == null) return;

            var size = (Vector2)field.GetValue(grid);
            var home = Home(grid, size);
            if (home.y < 1f) return;

            float room = sheet.rect.height;
            if (room < home.y * 0.5f) return;
            float scale = sheet.lossyScale.y > 0.001f ? sheet.lossyScale.y : 1f;
            float hidden = ChatDock.Active ? ChatDock.PanelPixels / scale : 0f;
            float open = Mathf.Max(home.y, room - hidden);
            float gap = layout.spacing.y;
            int fits = Mathf.Max(1, Mathf.FloorToInt((open + gap) / (home.y + gap)));
            float want = Mathf.Clamp(((open + gap) / (fits + 1) - gap) / home.y, Floor, 1f);

            var cell = new Vector2(Mathf.Round(home.x * want), Mathf.Round(home.y * want));
            Dress(layout, home, want);
            if ((layout.cellSize - cell).sqrMagnitude < 1f && (size - cell).sqrMagnitude < 1f) return;

            field.SetValue(grid, cell);
            layout.cellSize = cell;
            Redo(grid);
            Plugin.Trace("[карточки] " + grid.GetType().Name + ": " + Mathf.RoundToInt(home.x) + "x" + Mathf.RoundToInt(home.y)
                + " → " + Mathf.RoundToInt(cell.x) + "x" + Mathf.RoundToInt(cell.y)
                + ", поле " + Mathf.RoundToInt(room) + ", видно " + Mathf.RoundToInt(open));
        }

        private static FieldInfo SizeField(Type type)
        {
            FieldInfo known;
            if (Sizes.TryGetValue(type, out known)) return known;
            known = type.GetField("ItemSize", Deep);
            Sizes[type] = known;
            return known;
        }

        private static FieldInfo FlagField(Type type)
        {
            FieldInfo known;
            if (Flags.TryGetValue(type, out known)) return known;
            for (var walk = type; walk != null; walk = walk.BaseType)
            {
                known = walk.GetField("_needRecreateItems", Deep);
                if (known != null) break;
            }
            Flags[type] = known;
            return known;
        }

        private static void Dress(GridLayoutGroup layout, Vector2 home, float want)
        {
            var host = layout.transform;
            for (int i = 0; i < host.childCount; i++)
            {
                var card = host.GetChild(i) as RectTransform;
                if (card == null) continue;
                var hold = card.GetComponent<CardSize>();
                if (hold == null) hold = card.gameObject.AddComponent<CardSize>();
                if ((hold.Want - home).sqrMagnitude > 1f || Mathf.Abs(hold.Small - want) > 0.001f)
                {
                    hold.Want = home;
                    hold.Small = want;
                    hold.Apply();
                }
            }
        }

        private static Vector2 Home(Component grid, Vector2 size)
        {
            var born = grid.GetComponent<CardHome>();
            if (born != null) return born.Size;
            if (size.y < 1f) return Vector2.zero;
            grid.gameObject.AddComponent<CardHome>().Size = size;
            return size;
        }

        private static void Redo(Component grid)
        {
            var flag = FlagField(grid.GetType());
            if (flag != null) flag.SetValue(grid, true);
        }
    }
}
