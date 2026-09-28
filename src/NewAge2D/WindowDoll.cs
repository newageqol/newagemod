using System.Reflection;
using HarmonyLib;
using Transport.Messages.Common.User.Wear;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class WindowDoll
{
    private const string DollName = "NewAge2D.Doll";
    private const float Room = 0.1f;

    private static readonly Dictionary<int, string> ImageByThing = new();
    private static readonly Dictionary<int, string> ImageBySlot = new();
    private static readonly FieldInfo CharacterField = AccessTools.Field(typeof(UserMenuSlotsPanel3DCharacter), "_character");
    private static readonly FieldInfo SlotsField = AccessTools.Field(typeof(UserMenuSlotsPanel3DCharacter), "_slots");
    private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(AbstractCharacter), "_containerGameObject");

    private static readonly HashSet<Renderer> Hidden = new();

    private static string _signature;
    private static int _job;
    private static float _worldLow;
    private static float _worldHigh;

    [HarmonyPrefix, HarmonyPatch(typeof(UserMenuCharacterSlotsPanelContent), "UpdateView")]
    private static void RememberImages(IDictionary<ESlots.SlotType, InventoryWearResponseMessageItem> wearedSlots)
    {
        if (wearedSlots == null) return;
        ImageBySlot.Clear();
        foreach (var pair in wearedSlots)
        {
            var item = pair.Value;
            if (item == null || string.IsNullOrEmpty(item.Image)) continue;
            ImageByThing[item.ThingId] = item.Image;
            ImageBySlot[(int)pair.Key] = item.Image;
            ThingImages.Learn(item.ThingId, item.Image);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(UserMenuSlotsPanel3DCharacter), "SetCharacterCameraLayer")]
    private static void AfterLayer(UserMenuSlotsPanel3DCharacter __instance)
    {
        try { Refresh(__instance); }
        catch (Exception ex) { Plugin.Log.LogError("[doll] " + ex); }
    }

    internal static void Set(bool on)
    {
        foreach (var panel in UnityEngine.Object.FindObjectsOfType<UserMenuSlotsPanel3DCharacter>())
        {
            if (on)
            {
                try { Refresh(panel); }
                catch (Exception ex) { Plugin.Log.LogError("[doll] " + ex); }
                continue;
            }
            var character = CharacterField.GetValue(panel) as PlayerCharacter;
            var container = character != null ? ContainerField.GetValue(character) as GameObject : null;
            var doll = container != null ? Find(container) : null;
            if (doll != null) doll.SetActive(false);
        }
        if (!on) Restore();
    }

    private static void Refresh(UserMenuSlotsPanel3DCharacter panel)
    {
        if (!Plugin.FlashLook) return;
        var character = CharacterField.GetValue(panel) as PlayerCharacter;
        if (character == null || !character.Initialized) return;
        var container = ContainerField.GetValue(character) as GameObject;
        if (container == null) return;

        bool any = false;
        var bounds = new Bounds();
        var weapons = new HashSet<Transform>();
        AddWeapons(weapons, character.LeftHandWeapon);
        AddWeapons(weapons, character.RightHandWeapon);
        Hidden.RemoveWhere(r => r == null);
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name == DollName) continue;
            if (renderer.enabled)
            {
                if (!InWeapon(renderer.transform, weapons))
                {
                    if (!any) { bounds = renderer.bounds; any = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                Hidden.Add(renderer);
            }
            renderer.enabled = false;
        }
        if (any)
        {
            var root = container.transform;
            var center = bounds.center;
            float low = root.InverseTransformPoint(new Vector3(center.x, bounds.min.y, center.z)).y;
            float high = root.InverseTransformPoint(new Vector3(center.x, bounds.max.y, center.z)).y;
            if (high - low > 0.05f)
            {
                _worldLow = low;
                _worldHigh = high;
            }
        }
        if (_worldHigh - _worldLow <= 0.05f)
        {
            _worldLow = 0f;
            _worldHigh = 1.8f;
        }

        var request = new DollRequest
        {
            Race = character.Race,
            Gender = (int)character.Gender,
            Scale = Mathf.Clamp(Plugin.CfgScale.Value, 0.5f, 8f),
        };
        var missing = new List<string>();
        if (SlotsField.GetValue(panel) is IList<InventorySlotMessage> slots)
        {
            foreach (var slot in slots)
            {
                if (slot == null || !Doll.IsVisualSlot(slot.SlotId)) continue;
                string image = null;
                if (slot.ThingId > 0) ImageByThing.TryGetValue(slot.ThingId, out image);
                if (image == null) ImageBySlot.TryGetValue(slot.SlotId, out image);
                if (image == null)
                {
                    missing.Add($"{slot.SlotId}:{slot.ThingId}");
                    continue;
                }
                request.Wear.Add(new DollWear { Slot = slot.SlotId, ThingId = slot.ThingId, Image = image });
            }
        }

        string signature = request.Signature;
        var doll = Find(container);
        if (signature == _signature && doll != null)
        {
            Place(doll, container, panel);
            return;
        }

        _signature = signature;
        int job = ++_job;
        float worldLow = _worldLow;
        float worldHigh = _worldHigh;
        float worldHeight = worldHigh - worldLow;
        if (Plugin.CfgVerbose.Value)
            Plugin.Log.LogInfo($"[doll] race {request.Race}, gender {request.Gender}, items {request.Wear.Count}"
                               + (missing.Count > 0 ? ", no image: " + string.Join(",", missing) : "")
                               + $", 3D height {worldHeight:0.00}");

        DollWorker.Enqueue(request, picture => MainThread.Post(() =>
        {
            if (job != _job) return;
            if (!Show(panel, container, picture, worldLow, worldHigh)) Restore();
        }));
    }

    private static void Restore()
    {
        foreach (var renderer in Hidden)
            if (renderer != null) renderer.enabled = true;
        Hidden.Clear();
        _signature = null;
    }

    private static void AddWeapons(HashSet<Transform> weapons, Weapon weapon)
    {
        var parts = weapon?.WeaponGameObjects;
        if (parts == null) return;
        foreach (var part in parts)
            if (part != null) weapons.Add(part.transform);
    }

    private static bool InWeapon(Transform item, HashSet<Transform> weapons)
    {
        if (weapons.Count == 0) return false;
        for (var t = item; t != null; t = t.parent)
            if (weapons.Contains(t)) return true;
        return false;
    }

    private static bool Show(UserMenuSlotsPanel3DCharacter panel, GameObject container, DollPicture picture, float worldLow, float worldHigh)
    {
        if (panel == null || container == null) return true;
        if (picture.Error != null)
        {
            Plugin.Log.LogWarning("[doll] " + picture.Error + " - showing 3D model");
            return false;
        }
        if (Plugin.CfgVerbose.Value)
            foreach (string note in picture.Notes) Plugin.Log.LogInfo("[doll] " + note);

        var texture = new Texture2D(picture.Width, picture.Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.LoadRawTextureData(picture.Rgba);
        texture.Apply(false, true);

        float worldHeight = worldHigh - worldLow;
        float pixelsPerUnit = (picture.BodyHeight > 1f ? picture.BodyHeight : picture.Height) / worldHeight;
        float spriteLow = -picture.PivotY * picture.Height / pixelsPerUnit;
        float spriteHigh = (1f - picture.PivotY) * picture.Height / pixelsPerUnit;
        float boxLow = worldLow - worldHeight * Room;
        float boxHigh = worldHigh + worldHeight * Room;
        float fit = 1f;
        if (spriteHigh - spriteLow > boxHigh - boxLow)
        {
            fit = (boxHigh - boxLow) / (spriteHigh - spriteLow);
            pixelsPerUnit /= fit;
            spriteLow *= fit;
            spriteHigh *= fit;
        }
        float lift = 0f;
        if (spriteHigh > boxHigh) lift = boxHigh - spriteHigh;
        else if (spriteLow < boxLow) lift = boxLow - spriteLow;
        var sprite = Sprite.Create(texture, new Rect(0, 0, picture.Width, picture.Height),
            new Vector2(picture.PivotX, picture.PivotY), pixelsPerUnit, 0, SpriteMeshType.FullRect);

        var doll = Find(container) ?? Make(container);
        doll.GetComponent<DollBillboard>().Lift = lift;
        var view = doll.GetComponent<SpriteRenderer>();
        var old = view.sprite;
        view.sprite = sprite;
        if (old != null)
        {
            if (old.texture != null) UnityEngine.Object.Destroy(old.texture);
            UnityEngine.Object.Destroy(old);
        }
        Place(doll, container, panel);
        Plugin.Log.LogInfo($"[doll] shown {picture.Width}x{picture.Height}, body {picture.BodyHeight:0} px, {pixelsPerUnit:0} px per unit, fit {fit:0.00}, lift {lift:0.00}");
        return true;
    }

    private static GameObject Find(GameObject container)
    {
        var child = container.transform.Find(DollName);
        return child != null ? child.gameObject : null;
    }

    private static GameObject Make(GameObject container)
    {
        var doll = new GameObject(DollName);
        doll.transform.SetParent(container.transform, false);
        var view = doll.AddComponent<SpriteRenderer>();
        var billboard = doll.AddComponent<DollBillboard>();
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            billboard.Skin = new Material(shader);
            view.sharedMaterial = billboard.Skin;
        }
        else Plugin.Log.LogWarning("[doll] sprite shader not found, default material stays");
        return doll;
    }

    private static void Place(GameObject doll, GameObject container, UserMenuSlotsPanel3DCharacter panel)
    {
        int layer = LayerMask.NameToLayer("WindowGameObjectsLayer");
        doll.layer = layer >= 0 ? layer : container.layer;
        var billboard = doll.GetComponent<DollBillboard>();
        doll.transform.localPosition = new Vector3(0f, billboard.Lift, 0f);
        billboard.Eye = panel.GetComponentInChildren<Camera>(true);
        billboard.Body = container.transform;
        doll.SetActive(true);
    }
}

internal sealed class DollBillboard : MonoBehaviour
{
    public Camera Eye;
    public Transform Body;
    public Material Skin;
    public float Lift;

    private SpriteRenderer _view;
    private bool _baseKnown;
    private bool _baseFront;

    private void Awake()
    {
        _view = GetComponent<SpriteRenderer>();
    }

    private void OnDestroy()
    {
        var sprite = _view != null ? _view.sprite : null;
        if (sprite != null)
        {
            if (sprite.texture != null) Destroy(sprite.texture);
            Destroy(sprite);
        }
        if (Skin != null) Destroy(Skin);
    }

    private void LateUpdate()
    {
        if (Eye == null) return;
        transform.rotation = Eye.transform.rotation;
        if (Body == null || _view == null) return;
        bool front = Vector3.Dot(Body.forward, Eye.transform.forward) < 0f;
        if (!_baseKnown)
        {
            _baseKnown = true;
            _baseFront = front;
        }
        _view.flipX = front != _baseFront;
    }
}
