using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class LeftColumn
    {
        private static float _next;
        private static float _look;
        private static LeftBottomMenuScript _menu;
        private static bool _hidMenu;
        private static TopPanelView _top;
        private static GameObject _block;
        private static bool _hidBlock;

        internal const bool On = true;


        internal static bool TopBlockHidden => On;

        internal static void Tick()
        {
            try
            {
                Menu();
                Top();
                Keys();
            }
            catch (Exception e) { Plugin.Trace("[колонка] " + e.Message); }
        }

        private static float _keysAt;
        private static KeyCode _bagKey, _dailyKey;

        private static void Keys()
        {
            if (!_hidMenu || !On) return;
            if (Time.unscaledTime >= _keysAt)
            {
                _keysAt = Time.unscaledTime + 2f;
                var bag = Key(EHotkeyActions.Inventory);
                var daily = Key(EHotkeyActions.DailyTasks);
                if (bag != _bagKey || daily != _dailyKey) Plugin.Trace("[колонка] клавиши: сумка " + bag + ", задания " + daily);
                _bagKey = bag;
                _dailyKey = daily;
            }
            if (_bagKey == KeyCode.None && _dailyKey == KeyCode.None) return;
            if (!Input.anyKeyDown || Typing()) return;
            if (_bagKey != KeyCode.None && Input.GetKeyDown(_bagKey))
            {
                Plugin.Trace("[колонка] сумка по клавише " + _bagKey);
                try { Spells.Menu(UserMenuController.ETabs.Inventory); }
                catch (Exception e) { Plugin.Trace("[колонка] сумка клавишей: " + e.Message); }
            }
            if (_dailyKey != KeyCode.None && Input.GetKeyDown(_dailyKey))
            {
                try { DependencyContainer.GetContainer()?.Resolve<DailyTasksWindowController>()?.OpenByButton(); }
                catch (Exception e) { Plugin.Trace("[колонка] задания клавишей: " + e.Message); }
            }
        }

        private static bool Typing()
        {
            var system = UnityEngine.EventSystems.EventSystem.current;
            var picked = system != null ? system.currentSelectedGameObject : null;
            var field = picked != null ? picked.GetComponent<UnityEngine.UI.InputField>() : null;
            return field != null && field.isFocused;
        }

        private static KeyCode Key(EHotkeyActions action)
        {
            try
            {
                var dispatcher = HotkeyDispatcher.Instance;
                if (dispatcher == null) return KeyCode.None;
                var call = AccessTools.Method(typeof(HotkeyDispatcher), "GetKeyCodeByAction", new[] { typeof(EHotkeyActions) });
                var got = call != null ? call.Invoke(dispatcher, new object[] { action }) : null;
                if (got is KeyCode key) return key;
                if (got is int code) return (KeyCode)code;
            }
            catch (Exception e) { Plugin.Trace("[колонка] клавиша " + action + ": " + e.Message); }
            return KeyCode.None;
        }

        internal static void Keep()
        {
            try
            {
                Menu();
                Top();
            }
            catch (Exception e) { Plugin.Trace("[колонка] поздний кадр: " + e.Message); }
        }

        internal static void Squash(LeftBottomMenuScript menu)
        {
            if (menu == null || !On) return;
            if (menu.gameObject.activeSelf) menu.gameObject.SetActive(false);
            _menu = menu;
            _hidMenu = true;
        }

        private static bool Slow(ref float when)
        {
            if (Time.unscaledTime < when) return false;
            when = Time.unscaledTime + (SideButtons.SceneFresh ? 0.03f : 0.5f);
            return true;
        }

        private static float _tall = 65f;
        private static GameObject _topPanel;
        private static float _panelAt;

        internal static float TopHeight()
        {
            try
            {
                if (_topPanel == null && Slow(ref _panelAt)) _topPanel = GameObject.Find("Canvas/TopPanel");
                var panel = _topPanel;
                if (panel == null || !panel.activeInHierarchy) return _tall;
                if (_hidBlock && _block != null && _block == panel) return _tall;
                var rt = panel.transform as RectTransform;
                if (rt != null && rt.rect.height > 1f) _tall = rt.rect.height;
                return _tall;
            }
            catch { return _tall; }
        }

        private static void Menu()
        {
            var menu = Find();
            if (menu == null || !On || !menu.gameObject.activeSelf) return;
            menu.gameObject.SetActive(false);
            _hidMenu = true;
            Plugin.Trace("[колонка] игровое меню снизу скрыто");
        }

        private static LeftBottomMenuScript Find()
        {
            try
            {
                if (_menu != null) return _menu;
                if (!Slow(ref _next)) return null;
                var view = BaseLocationView.GetInstance();
                if (view != null && view.LeftBottomMenuScript != null) _menu = view.LeftBottomMenuScript;
                if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<LeftBottomMenuScript>();
                return _menu;
            }
            catch { return null; }
        }

        internal static Sprite MenuSprite(string field)
        {
            try
            {
                var menu = Find();
                if (menu == null) return null;
                var button = AccessTools.Field(typeof(LeftBottomMenuScript), field)?.GetValue(menu) as UnityEngine.UI.Button;
                if (button == null) return null;
                var image = button.targetGraphic as UnityEngine.UI.Image;
                if (image == null) image = button.GetComponent<UnityEngine.UI.Image>();
                return image != null ? image.sprite : null;
            }
            catch { return null; }
        }

        private static void Top()
        {
            bool hide = On;
            if (_top != null) { Cover(_top, hide); return; }
            if (!Slow(ref _look)) return;
            foreach (var view in UnityEngine.Object.FindObjectsOfType<TopPanelView>())
            {
                if (view == null) continue;
                if (_top != view) { _top = view; _block = null; }
                Cover(view, hide);
            }
        }

        private static void Cover(TopPanelView view, bool hide)
        {
            var block = _block != null && _block.transform.IsChildOf(view.transform) ? _block : Block(view);
            if (block == null) return;
            _block = block;
            if (block.activeSelf == !hide) return;
            block.SetActive(!hide);
            _hidBlock = hide;
            Plugin.Trace("[колонка] блок персонажа сверху " + (hide ? "скрыт" : "возвращён"));
        }

        private static GameObject Block(TopPanelView view)
        {
            try
            {
                var avatar = AccessTools.Field(typeof(TopPanelView), "AvatarImage")?.GetValue(view) as Component;
                if (avatar == null) return null;

                var root = view.transform;
                var node = avatar.transform;
                GameObject best = null;
                while (node != null)
                {
                    if (node.GetComponentInChildren<UserCurrencyPanel>(true) != null) break;
                    best = node.gameObject;
                    if (node == root) break;
                    node = node.parent;
                }
                return best;
            }
            catch (Exception e) { Plugin.Trace("[колонка] блок персонажа: " + e.Message); return null; }
        }
    }

    [HarmonyPatch(typeof(LeftBottomMenuScript), "Show")]
    internal static class LeftColumnShowPatch
    {
        private static void Postfix(LeftBottomMenuScript __instance) => LeftColumn.Squash(__instance);
    }
}
