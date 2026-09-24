using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

internal static class SpellMarks
{
    private const float Patience = 3.5f;
    private const float Longest = 6f;
    private const float FloorDepth = -0.06f;
    private const float FloorHalf = 15f;

    private static readonly System.Reflection.FieldInfo Extra = AccessTools.Field(typeof(AnimationItem), "_additionalTargets");
    private static readonly System.Reflection.FieldInfo Selector = AccessTools.Field(typeof(TargetMagicSelector), "_instance");
    private static readonly System.Reflection.FieldInfo Prefabs = AccessTools.Field(typeof(TargetMagicSelector), "targetPrefabs");

    private sealed class Mark
    {
        public AbstractCharacter Caster;
        public AbstractCharacter Target;
        public string Name;
        public int Spell;
        public float Asked;
        public float Start = -1f;
        public bool Built;
        public GameObject Root;
        public GameObject Effect;
        public GameObject Floor;
        public Material Tinted;
        public Renderer[] Parts;
        public bool[] Front;
    }

    private static readonly List<Mark> Live = new();
    private static readonly Dictionary<int, Texture2D> Icons = new();
    private static readonly HashSet<string> Missing = new();
    private static Mesh _plate;
    private static Material _floorSkin;
    private static bool _noFloor;

    internal static void Show(AnimationItem item, AbstractCharacter caster, string name)
    {
        if (!Plugin.FlashFight || Plugin.CfgSpellMarks == null || !Plugin.CfgSpellMarks.Value || item == null || caster == null) return;
        if (item.VisualEffect != EAnimationVisualEffect.SPELL && item.VisualEffect != EAnimationVisualEffect.ENCHANTMENT_EFFECT) return;
        try
        {
            var targets = new List<AbstractCharacter>();
            if (Extra?.GetValue(item) is AbstractCharacter[] many)
                foreach (var one in many) Add(targets, one);
            if (targets.Count == 0) Add(targets, item.Target);
            if (targets.Count == 0) return;
            int spell = item is KickAnimationItem kick ? kick.SubId : 0;
            float now = Time.time;
            foreach (var target in targets)
                Live.Add(new Mark { Caster = caster, Target = target, Name = name, Spell = spell, Asked = now });
            if (Trace.On) Trace.Write($"«{caster.Login}» {name}, заклинание {spell}: значок цели над {string.Join(", ", targets.Select(one => one.Login))}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[цели заклинания] " + ex.Message); }
    }

    private static void Add(List<AbstractCharacter> list, AbstractCharacter one)
    {
        if (one == null || !one.Initialized) return;
        foreach (var had in list)
            if (ReferenceEquals(had, one)) return;
        list.Add(one);
    }

    internal static void Tick()
    {
        if (Live.Count == 0) return;
        var location = CombatView.Get();
        var eye = location != null ? location.CombatCamera : null;
        float now = Time.time;
        for (int i = 0; i < Live.Count; i++)
        {
            var mark = Live[i];
            bool gone = eye == null || !Plugin.FlashFight || mark.Target == null || !mark.Target.Initialized;
            if (!gone && mark.Start < 0f && !Ready(mark, now)) continue;
            if (!gone && now < mark.Start) continue;
            try
            {
                if (!gone && !mark.Built) gone = !Build(mark, eye);
                if (!gone) gone = mark.Root == null || mark.Effect == null || now - mark.Start >= Longest;
                if (!gone)
                {
                    Place(mark, eye);
                    continue;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[цели заклинания] " + ex.Message); }
            Drop(mark);
            Live.RemoveAt(i--);
        }
    }

    private static bool Ready(Mark mark, float now)
    {
        float began = -1f;
        var doll = Fighters.DollOf(mark.Caster);
        if (doll != null && !doll.Broken)
        {
            bool waiting = doll.CastWaiting(mark.Asked, out began);
            if (waiting && now - mark.Asked <= Patience) return false;
            if (!waiting && began < 0f && now - mark.Asked <= 0.25f) return false;
        }
        mark.Start = began >= 0f ? began : now;
        return true;
    }

    private static void Drop(Mark mark)
    {
        if (mark.Tinted != null) UnityEngine.Object.Destroy(mark.Tinted);
        if (mark.Root != null) UnityEngine.Object.Destroy(mark.Root);
        if (mark.Floor != null) UnityEngine.Object.Destroy(mark.Floor);
        mark.Tinted = null;
        mark.Root = null;
        mark.Effect = null;
        mark.Floor = null;
    }

    private static bool Build(Mark mark, Camera eye)
    {
        mark.Built = true;
        var prefab = Prefab(mark.Name);
        if (prefab == null) return false;
        var spot = Spot(mark, eye, out _);
        var root = new GameObject("NewAge2D.SpellMark");
        root.transform.position = spot;
        mark.Root = root;
        var effect = UnityEngine.Object.Instantiate(prefab);
        effect.transform.position = spot;
        effect.transform.SetParent(root.transform, true);
        mark.Effect = effect;
        var icon = Child(effect.transform, "icon");
        if (icon != null)
        {
            var picture = Icon(mark.Spell);
            var view = icon.GetComponent<Renderer>();
            if (picture == null) icon.gameObject.SetActive(false);
            else if (view != null)
            {
                mark.Tinted = view.material;
                mark.Tinted.mainTexture = picture;
            }
        }
        mark.Parts = effect.GetComponentsInChildren<Renderer>(true);
        mark.Front = new bool[mark.Parts.Length];
        for (int i = 0; i < mark.Parts.Length; i++)
            mark.Front[i] = Under(mark.Parts[i].transform, effect.transform, "icon");
        mark.Floor = Floor();
        effect.SetActive(true);
        return true;
    }

    private static GameObject Floor()
    {
        var grid = CombatView.Get()?.HexGrid;
        var skin = FloorSkin();
        if (grid == null || skin == null) return null;
        var go = new GameObject("NewAge2D.SpellFloor", typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = grid.gameObject.layer;
        go.GetComponent<MeshFilter>().sharedMesh = Plate();
        var view = go.GetComponent<MeshRenderer>();
        view.sharedMaterial = skin;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        return go;
    }

    private static Material FloorSkin()
    {
        if (_floorSkin != null) return _floorSkin;
        if (_noFloor) return null;
        var shader = Shader.Find("Custom/Transparent2Sides");
        if (shader == null)
        {
            _noFloor = true;
            Plugin.Log.LogInfo("[цели заклинания] у игры нет шейдера для земли под лучом, луч остаётся как есть");
            return null;
        }
        _floorSkin = new Material(shader) { name = "NewAge2D.SpellFloor", renderQueue = 2000, color = new Color(0f, 0f, 0f, 1f / 255f) };
        return _floorSkin;
    }

    private static Mesh Plate()
    {
        if (_plate != null) return _plate;
        _plate = new Mesh { name = "NewAge2D.SpellFloor" };
        _plate.vertices = new[]
        {
            new Vector3(-FloorHalf, 0f, -FloorHalf), new Vector3(FloorHalf, 0f, -FloorHalf),
            new Vector3(FloorHalf, 0f, FloorHalf), new Vector3(-FloorHalf, 0f, FloorHalf),
        };
        _plate.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _plate.RecalculateBounds();
        return _plate;
    }

    private static GameObject Prefab(string name)
    {
        var selector = Selector?.GetValue(null) as TargetMagicSelector;
        if (selector != null && Prefabs?.GetValue(selector) is GameObject[] all)
            foreach (var one in all)
                if (one != null && one.name == name) return one;
        if (Missing.Add(name ?? "")) Plugin.Log.LogInfo($"[цели заклинания] у игры нет значка цели для «{name}»");
        return null;
    }

    private static Texture2D Icon(int spell)
    {
        if (spell <= 0) return null;
        if (Icons.TryGetValue(spell, out var had) && had != null) return had;
        Texture2D made = null;
        try
        {
            var sprite = AtlasUtils.GetSpellEffectSprite(spell);
            if (sprite != null)
            {
                var rect = sprite.textureRect;
                int width = (int)rect.width;
                int height = (int)rect.height;
                made = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "NewAge2D.SpellIcon" };
                made.SetPixels(sprite.texture.GetPixels((int)rect.x, (int)rect.y, width, height));
                made.Apply();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[цели заклинания] значок заклинания {spell}: {ex.Message}");
            if (made != null) UnityEngine.Object.Destroy(made);
            made = null;
        }
        Icons[spell] = made;
        return made;
    }

    private static Transform Child(Transform root, string name)
    {
        foreach (var one in root.GetComponentsInChildren<Transform>(true))
            if (one.name == name) return one;
        return null;
    }

    private static bool Under(Transform node, Transform root, string name)
    {
        for (; node != null && node != root; node = node.parent)
            if (node.name == name) return true;
        return false;
    }

    private static Vector3 Spot(Mark mark, Camera eye, out int order)
    {
        var doll = Fighters.DollOf(mark.Target);
        if (doll != null && doll.Placed)
        {
            order = doll.SortingOrder;
            return doll.Feet + doll.HeadShift(eye);
        }
        var spot = mark.Target.position;
        order = Fighters.Layer(eye, spot, mark.Target.HexGridPosition);
        return spot;
    }

    private static void Place(Mark mark, Camera eye)
    {
        mark.Root.transform.position = Spot(mark, eye, out int order);
        var grid = mark.Floor != null ? CombatView.Get()?.HexGrid : null;
        if (grid != null)
        {
            var under = grid.transform.InverseTransformPoint(mark.Root.transform.position);
            under.y = FloorDepth;
            mark.Floor.transform.SetPositionAndRotation(grid.transform.TransformPoint(under), grid.transform.rotation);
        }
        for (int i = 0; i < mark.Parts.Length; i++)
        {
            var part = mark.Parts[i];
            if (part == null) continue;
            int want = mark.Front[i] ? order + 1 : order - 1;
            if (part.sortingOrder != want) part.sortingOrder = want;
        }
    }
}
