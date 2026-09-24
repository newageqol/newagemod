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

    private static readonly Dictionary<int, string> ImageByThing = new();
    private static readonly Dictionary<int, string> ImageBySlot = new();
    private static readonly FieldInfo CharacterField = AccessTools.Field(typeof(UserMenuSlotsPanel3DCharacter), "_character");
    private static readonly FieldInfo SlotsField = AccessTools.Field(typeof(UserMenuSlotsPanel3DCharacter), "_slots");
    private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(AbstractCharacter), "_containerGameObject");

    private static readonly HashSet<Renderer> Hidden = new();

    private static string _signature;
    private static int _job;
    private static float _worldHeight;

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
        catch (Exception ex) { Plugin.Log.LogError("[кукла] " + ex); }
    }

    internal static void Set(bool on)
    {
        foreach (var panel in UnityEngine.Object.FindObjectsOfType<UserMenuSlotsPanel3DCharacter>())
        {
            if (on)
            {
                try { Refresh(panel); }
                catch (Exception ex) { Plugin.Log.LogError("[кукла] " + ex); }
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
        Hidden.RemoveWhere(r => r == null);
        foreach (var renderer in container.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.name == DollName) continue;
            if (renderer.enabled)
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
                Hidden.Add(renderer);
            }
            renderer.enabled = false;
        }
        if (any && bounds.size.y > 0.05f) _worldHeight = bounds.size.y;
        if (_worldHeight <= 0f) _worldHeight = 1.8f;

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
        float worldHeight = _worldHeight;
        if (Plugin.CfgVerbose.Value)
            Plugin.Log.LogInfo($"[кукла] раса {request.Race}, пол {request.Gender}, вещей {request.Wear.Count}"
                               + (missing.Count > 0 ? ", без картинки: " + string.Join(",", missing) : "")
                               + $", высота 3D {worldHeight:0.00}");

        DollWorker.Enqueue(request, picture => MainThread.Post(() =>
        {
            if (job != _job) return;
            if (!Show(panel, container, picture, worldHeight)) Restore();
        }));
    }

    private static void Restore()
    {
        foreach (var renderer in Hidden)
            if (renderer != null) renderer.enabled = true;
        Hidden.Clear();
        _signature = null;
    }

    private static bool Show(UserMenuSlotsPanel3DCharacter panel, GameObject container, DollPicture picture, float worldHeight)
    {
        if (panel == null || container == null) return true;
        if (picture.Error != null)
        {
            Plugin.Log.LogWarning("[кукла] " + picture.Error + " — показываю 3D-модель");
            return false;
        }
        if (Plugin.CfgVerbose.Value)
            foreach (string note in picture.Notes) Plugin.Log.LogInfo("[кукла] " + note);

        var texture = new Texture2D(picture.Width, picture.Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.LoadRawTextureData(picture.Rgba);
        texture.Apply(false, true);

        float pixelsPerUnit = (picture.BodyHeight > 1f ? picture.BodyHeight : picture.Height) / worldHeight;
        var sprite = Sprite.Create(texture, new Rect(0, 0, picture.Width, picture.Height),
            new Vector2(picture.PivotX, picture.PivotY), pixelsPerUnit, 0, SpriteMeshType.FullRect);

        var doll = Find(container) ?? Make(container);
        var view = doll.GetComponent<SpriteRenderer>();
        var old = view.sprite;
        view.sprite = sprite;
        if (old != null)
        {
            if (old.texture != null) UnityEngine.Object.Destroy(old.texture);
            UnityEngine.Object.Destroy(old);
        }
        Place(doll, container, panel);
        Plugin.Log.LogInfo($"[кукла] показана {picture.Width}x{picture.Height}, тело {picture.BodyHeight:0} px, {pixelsPerUnit:0} px на единицу");
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
        else Plugin.Log.LogWarning("[кукла] шейдер спрайтов не найден, останется материал по умолчанию");
        return doll;
    }

    private static void Place(GameObject doll, GameObject container, UserMenuSlotsPanel3DCharacter panel)
    {
        int layer = LayerMask.NameToLayer("WindowGameObjectsLayer");
        doll.layer = layer >= 0 ? layer : container.layer;
        doll.transform.localPosition = Vector3.zero;
        var billboard = doll.GetComponent<DollBillboard>();
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
