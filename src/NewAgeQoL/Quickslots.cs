using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.Combat.Buttons;
using Transport.Messages.Responses.Things.Actions;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Quickslots
    {
        internal const int Window = -104;
        internal const int UseButton = 32;
        internal const int Tab = 2;

        private static readonly string[] Covered = { "UsedThingsButton", "InstantUsedThingsButton", "MoveModeButton" };
        private static readonly Dictionary<string, CanvasGroup> Veils = new Dictionary<string, CanvasGroup>();
        private static readonly Dictionary<int, IGeneralThingInfoDescription> Told = new Dictionary<int, IGeneralThingInfoDescription>();
        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Dictionary<string, Sprite> Loaded = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Asking = new HashSet<string>();
        private static readonly HashSet<int> Blank = new HashSet<int>();

        private static FieldInfo _bag;
        private static FieldInfo _spare;
        private static bool _bagSaid;
        private static float _coverAt;
        private static float _probeAt;

        internal static bool OnMap
        {
            get
            {
                try { return SceneWorkFlow.IsCurrentScene(EUnityScene.GlobalMapLocation); }
                catch { return false; }
            }
        }

        internal static GlobalMapQuickslotController Controller()
        {
            try { return Controllers.Get<GlobalMapQuickslotController>(); }
            catch { return null; }
        }

        internal static void Tick()
        {
            try
            {
                if (Time.unscaledTime < _coverAt) return;
                _coverAt = Time.unscaledTime + 0.25f;
                if (!SideButtons.InWorld() || !OnMap) return;
                foreach (var name in Covered) Cover(name);
            }
            catch (Exception e) { Plugin.Trace("[слоты] " + e.Message); }
        }

        private static void Cover(string name)
        {
            CanvasGroup veil;
            if (Veils.TryGetValue(name, out veil) && veil != null) { Dim(veil); return; }
            var go = GameObject.Find("Canvas/CombatCommandPanel/" + name);
            if (go == null) return;
            veil = go.GetComponent<CanvasGroup>();
            if (veil == null) veil = go.AddComponent<CanvasGroup>();
            Dim(veil);
            Veils[name] = veil;
            Plugin.Trace("[слоты] игровая кнопка " + name + " закрыта своей");
        }

        private static void Dim(CanvasGroup veil)
        {
            if (veil.alpha != 0f) veil.alpha = 0f;
            if (veil.blocksRaycasts) veil.blocksRaycasts = false;
            if (veil.interactable) veil.interactable = false;
        }

        private static QuickButtonStateHolder Holder()
        {
            var ctrl = Controller();
            if (ctrl == null) return null;
            var field = Field();
            if (field == null) return null;
            try { return field.GetValue(ctrl) as QuickButtonStateHolder; }
            catch (Exception e) { Plugin.Trace("[слоты] держатель: " + e.Message); return null; }
        }

        private static FieldInfo Field()
        {
            if (_bag != null) return _bag;
            if (Time.unscaledTime < _probeAt) return _spare;
            _probeAt = Time.unscaledTime + 0.25f;
            string name = Keeps("InstantUsedThingsButton", "_instantQuickSlots")
                       ?? Keeps("UsedThingsButton", "_quickSlots");
            if (name == null)
            {
                if (_spare == null) _spare = AccessTools.Field(typeof(GlobalMapQuickslotController), "_instantQuickSlots");
                return _spare;
            }
            _bag = AccessTools.Field(typeof(GlobalMapQuickslotController), name);
            if (_bag == null) Plugin.Warn("[слоты] поле быстрых слотов не найдено: " + name);
            else if (!_bagSaid)
            {
                _bagSaid = true;
                Plugin.Trace("[слоты] «Помощь» лежит в " + name);
            }
            return _bag;
        }

        private static string Keeps(string button, string field)
        {
            try
            {
                var go = GameObject.Find("Canvas/CombatCommandPanel/" + button);
                var command = go != null ? go.GetComponent<BaseCommandButton>() : null;
                string key = command != null ? command.ButtonText : null;
                if (string.IsNullOrEmpty(key)) return null;
                return key.IndexOf("enchantments", StringComparison.OrdinalIgnoreCase) >= 0 ? field : null;
            }
            catch { return null; }
        }

        internal static List<QuickButton> Help()
        {
            var list = new List<QuickButton>();
            var holder = Holder();
            if (holder == null) return list;
            try
            {
                var part = holder.GetButtonsByRoundType(RoundType.WALK_ROUND);
                if (part == null) return list;
                foreach (var one in part) if (one != null) list.Add(one);
            }
            catch (Exception e) { Plugin.Trace("[слоты] список помощи: " + e.Message); }
            return list;
        }

        internal static string Apply(QuickButton button)
        {
            try
            {
                if (button == null) return null;
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return "Нет соединения";
                var holder = Holder();
                int id = button.Id;
                var context = new ResponseCallbackContext(387);
                nc.SendRequest(context, new ContextActionRequest(id, UseButton, Window, Tab, 0), 416, id, msg =>
                {
                    var answer = msg as ThingContextActionResponseMessage;
                    if (answer == null || answer.WindowId != Window) return;
                    if (!answer.Success)
                    {
                        Plugin.Trace("[слоты] отказ на " + id + ": " + answer.ErrorMessage);
                        try { AirMessageScript.ShowErrorNotification(answer.ErrorMessage); } catch { }
                        return;
                    }
                    if (holder == null || answer.ChangesInTab == null) return;
                    foreach (var change in answer.ChangesInTab) Shrink(holder, button, change);
                });
                Plugin.Trace("[слоты] применяю " + button.Name + ", запись " + id);
                return null;
            }
            catch (Exception e) { Plugin.Fault("[слоты] " + e); return "Ошибка: " + e.Message; }
        }

        private static void Shrink(QuickButtonStateHolder holder, QuickButton source, ChangesInTabMessage change)
        {
            try
            {
                if (change == null) return;
                var button = holder.GetButton(change.Id);
                if (button != null)
                {
                    if (change.Quantity <= 0) holder.RemoveButton(button);
                    else button.Count = change.Quantity;
                    return;
                }
                if (change.Quantity <= 0) return;
                var fresh = holder.AddButton(new UpdateButtonMessage
                {
                    Id = change.Id,
                    Image = change.Image,
                    CanActivate = source.CanActivate,
                    Count = change.Quantity,
                    Phase = source.Phase,
                    Target = (int)source.Target,
                    TargetCondition = (int)source.TargetCondition,
                    StaminaCost = source.StaminaCost,
                    ExpowerCost = source.ExpowerCost,
                    Enabled = source.Enabled,
                    DisableCause = source.DisableCause,
                }, EQuickButtonType.QUICK_SLOT);
                if (fresh == null) return;
                fresh.Name = source.Name;
                fresh.Description = source.Description;
                fresh.Unity3DCombatIconAtlasId = source.Unity3DCombatIconAtlasId;
            }
            catch (Exception e) { Plugin.Trace("[слоты] пересчёт остатка: " + e.Message); }
        }

        internal static Sprite Icon(QuickButton button)
        {
            try
            {
                if (button == null) return null;
                string image = button.Image;
                var sprite = ByName(image);
                if (sprite == null)
                {
                    int key = button.Unity3DCombatIconAtlasId;
                    IGeneralThingInfoDescription known;
                    if (Told.TryGetValue(key, out known) && known != null)
                    {
                        if (string.IsNullOrEmpty(image)) image = known.Image;
                        sprite = ByName(known.Image);
                    }
                    else Learn(key);
                }
                if (sprite == null && InCombatAtlas(AtlasName(button))) sprite = Alive(AtlasUtils.GetQuickButtonSprite(button));
                if (sprite == null) sprite = Web(image);
                if (sprite == null && string.IsNullOrEmpty(image) && Blank.Add(button.Id))
                    Plugin.Trace("[слоты] нет картинки у записи " + button.Id + " «" + button.Name + "», вещь " + button.Unity3DCombatIconAtlasId);
                return sprite;
            }
            catch { return null; }
        }

        private static Sprite ByName(string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            var sprite = InCombatAtlas(image) ? Alive(AtlasUtils.GetQuickButtonSprite(image)) : null;
            if (sprite == null) sprite = Alive(AtlasUtils.GetThingSprite(image));
            return sprite;
        }

        private static bool InCombatAtlas(string name) =>
            name != null && AtlasesDatabase.Instance.GetData(new AtlasDatabaseKey(EAtlasType.COMBAT_ICONS_ATLAS, name)) != null;

        private static string AtlasName(QuickButton button)
        {
            switch (button.QuickButtonType)
            {
                case EQuickButtonType.DODGE: return "dodge" + button.Id;
                case EQuickButtonType.SPELL: return "spell" + button.Id;
                case EQuickButtonType.SKILL: return "ability" + button.Id;
                case EQuickButtonType.QUICK_SLOT: return button.Id == -2 ? "item20000_d" : "item" + button.Unity3DCombatIconAtlasId;
                default: return null;
            }
        }

        private static Sprite Web(string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            Sprite ready;
            if (Loaded.TryGetValue(image, out ready))
            {
                if (ready != null && ready.texture != null) return ready;
                Loaded.Remove(image);
                Asking.Remove(image);
            }
            if (!Asking.Add(image)) return null;
            try
            {
                RemoteImageLoader.Instance.Load(
                    "https://files.nura.biz/site/images/things100x100/" + image + ".png",
                    got => { if (got != null) Loaded[image] = got; },
                    error => Plugin.Trace("[слоты] картинка «" + image + "» не загрузилась: " + error));
                Plugin.Trace("[слоты] тяну картинку «" + image + "» с сайта");
            }
            catch { }
            return null;
        }

        internal static bool Faded(Sprite sprite)
        {
            return sprite == null || sprite.texture == null || sprite.name == "unknown";
        }

        private static Sprite Alive(Sprite sprite)
        {
            return Faded(sprite) ? null : sprite;
        }

        internal static string About(QuickButton button)
        {
            if (button == null) return "";
            int key = button.Unity3DCombatIconAtlasId;
            IGeneralThingInfoDescription known;
            if (Told.TryGetValue(key, out known) && known != null)
                return string.IsNullOrEmpty(known.Description) ? "" : known.Description.Trim();
            Learn(key);
            string plain = button.Description;
            if (string.IsNullOrEmpty(plain) || plain.StartsWith("items.item", StringComparison.Ordinal)) return "";
            return plain.Trim();
        }

        private static void Learn(int thingId)
        {
            try
            {
                if (thingId <= 0 || Asked.Contains(thingId)) return;
                var ctrl = Controller();
                var cache = ctrl != null ? ctrl.GeneralThingInfoDescriptionManager : null;
                if (cache == null) return;
                Asked.Add(thingId);
                cache.Get(thingId, got => { if (got != null) Told[thingId] = got; });
            }
            catch (Exception e) { Plugin.Trace("[слоты] описание " + thingId + ": " + e.Message); }
        }
    }
}
