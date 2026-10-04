using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Spells
    {
        private const int BookTab = 4;
        private const float Patience = 3f;

        private static bool _solo;

        internal static void Open()
        {
            try
            {
                var ctrl = Controllers.Get<UserMenuController>();
                if (ctrl == null) { Plugin.Trace("[spellbook] character menu not found"); return; }
                if (ctrl.IsWindowOpened)
                {
                    bool ours = _solo;
                    Shut();
                    if (ours) return;
                    if (ctrl.IsWindowOpened) { Plugin.Trace("[spellbook] menu window did not close"); return; }
                }
                _solo = true;
                ctrl.Open(UserMenuController.ETabs.SpellBook);
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Bare(ctrl));
            }
            catch (Exception e) { _solo = false; Plugin.Warn("[spellbook] open: " + e.Message); }
        }

        internal static void Forget()
        {
            _solo = false;
        }

        internal static void Menu(UserMenuController.ETabs tab)
        {
            try
            {
                var ctrl = Controllers.Get<UserMenuController>();
                if (ctrl == null) return;
                if (ctrl.IsWindowOpened)
                {
                    bool same = !_solo && Current(ctrl) == (int)tab;
                    Shut();
                    if (same || ctrl.IsWindowOpened) return;
                }
                ctrl.Open(tab);
            }
            catch (Exception e) { Plugin.Warn("[menu] open tab " + tab + ": " + e.Message); }
        }

        private static int Current(UserMenuController ctrl)
        {
            try
            {
                var panel = AccessTools.PropertyGetter(typeof(UserMenuController), "MainPanel")?.Invoke(ctrl, null) as StandardContentWindowPanel;
                var tabs = panel != null ? AccessTools.Field(typeof(StandardContentWindowPanel), "TabPanel")?.GetValue(panel) as TabPanel : null;
                return tabs != null && tabs.SelectedItem != null ? tabs.SelectedItem.Id : -1;
            }
            catch { return -1; }
        }

        private static IEnumerator Bare(UserMenuController ctrl)
        {
            for (int wait = 0; wait < 30; wait++)
            {
                yield return null;
                if (!_solo || ctrl == null || !ctrl.IsWindowOpened) yield break;
                var panel = AccessTools.PropertyGetter(typeof(UserMenuController), "MainPanel")?.Invoke(ctrl, null)
                            as StandardContentWindowPanel;
                if (panel == null) continue;
                var tabs = AccessTools.Field(typeof(StandardContentWindowPanel), "TabPanel")?.GetValue(panel) as TabPanel;
                var buttons = tabs != null
                    ? AccessTools.Field(typeof(TabPanel), "_tabButtons")?.GetValue(tabs) as IList<TabButton>
                    : null;
                if (tabs == null || tabs.Items == null || buttons == null || buttons.Count == 0) continue;

                if (tabs.SelectedItem == null || tabs.SelectedItem.Id != BookTab) tabs.SetSelectedTabByTabId(BookTab);
                int hidden = 0;
                foreach (var button in buttons)
                {
                    if (button == null || !button.gameObject.activeSelf) continue;
                    button.gameObject.SetActive(false);
                    hidden++;
                }
                foreach (var arrow in tabs.GetComponentsInChildren<TabScrollButton>(true))
                    if (arrow != null && arrow.gameObject.activeSelf) { arrow.gameObject.SetActive(false); hidden++; }
                Plugin.Trace("[spellbook] tab bar removed, hidden " + hidden);
                yield break;
            }
            Plugin.Trace("[spellbook] tabs not found, leaving as is");
        }

        internal static bool Pick(int spellId)
        {
            if (spellId <= 0 || !SideButtons.InCombat()) return false;
            try
            {
                Shut();
                if (SkillList.UseSpell(spellId))
                {
                    Plugin.Trace("[spellbook] spell " + spellId + " armed via the skills bar, pick the target with the mouse");
                    return true;
                }
                var ctrl = Controllers.Get<CombatButtonsController>();
                var arm = AccessTools.Method(typeof(CombatButtonsController), "ActivateSpellButtonHandler");
                if (ctrl == null || arm == null) { Plugin.Trace("[spellbook] nothing to select the spell with"); return true; }
                arm.Invoke(ctrl, new object[] { spellId });
                Plugin.Trace("[spellbook] spell " + spellId + " selected");
                return true;
            }
            catch (Exception e) { Plugin.Warn("[spellbook] spell select: " + e.Message); return true; }
        }

        private static UserMenuSpellBookPanelContent Book(object resolver)
        {
            return AccessTools.Field(typeof(UserMenuSpellBookPanelContentResolver), "_panelContent")?.GetValue(resolver)
                   as UserMenuSpellBookPanelContent;
        }

        internal static void Veil(object resolver)
        {
            try
            {
                var panel = Book(resolver);
                if (panel == null) return;
                var go = AccessTools.Field(typeof(BaseGridContentPanel<UserMenuSpellBookPanelContentDto>), "GoTabContentGrid")?.GetValue(panel)
                         as GameObject;
                if (go == null) { Plugin.Trace("[spellbook] spell grid not found, not hiding placeholders"); return; }
                var veil = go.GetComponent<CanvasGroup>();
                if (veil == null) veil = go.AddComponent<CanvasGroup>();
                veil.alpha = 0f;
                veil.blocksRaycasts = false;
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Unveil(panel, go, veil));
                else { veil.alpha = 1f; veil.blocksRaycasts = true; }
            }
            catch (Exception e) { Plugin.Trace("[spellbook] hide placeholders: " + e.Message); }
        }

        private static IEnumerator Unveil(UserMenuSpellBookPanelContent panel, GameObject go, CanvasGroup veil)
        {
            float until = Time.unscaledTime + Patience;
            var grid = go.GetComponent<Grid<UserMenuSpellBookPanelContentDto>>();
            var reset = AccessTools.Field(typeof(Grid<UserMenuSpellBookPanelContentDto>), "_needResetScroll");
            var made = AccessTools.Field(typeof(Grid<UserMenuSpellBookPanelContentDto>), "_itemsRecreated");
            bool late = false;
            while (true)
            {
                yield return null;
                if (veil == null || panel == null) yield break;
                if (Time.unscaledTime >= until) { late = true; break; }
                if (panel.TabContent == null) continue;
                if (grid == null) break;
                bool pending = reset != null && (bool)reset.GetValue(grid);
                bool ready = made == null || (bool)made.GetValue(grid);
                if (!pending && ready) break;
            }
            yield return null;
            if (veil == null) yield break;
            veil.alpha = 1f;
            veil.blocksRaycasts = true;
            Plugin.Trace(late ? "[spellbook] spell list did not arrive in time, showing as is" : "[spellbook] spell list laid out, showing");
        }

        internal static void Trim(object resolver)
        {
            try
            {
                if (!SideButtons.InCombat()) return;
                var panel = Book(resolver);
                if (panel == null) { Plugin.Trace("[spellbook] book panel not found, dodges stay"); return; }

                var toggle = Grab<Toggle>(panel, "DodgesToggle");
                var image = Grab<Image>(panel, "DodgesImage");
                var label = Grab<Text>(panel, "DodgesLabelText");
                var row = toggle != null ? toggle.gameObject : null;
                if (row == null && image != null) row = Owner(image.transform);
                if (row == null && label != null) row = Owner(label.transform);
                if (row == null) { Plugin.Trace("[spellbook] dodges entry not found"); return; }

                if (panel.CurrentButton == UserMenuSpellBookPanelContentResolver.EUserMenuSpellBookSelectionButton.DODGES)
                    panel.CurrentButton = UserMenuSpellBookPanelContentResolver.EUserMenuSpellBookSelectionButton.SPELL_SCHOOL_WHITE;

                row.SetActive(false);
                if (image != null) Tuck(image.transform, row.transform);
                if (label != null) Tuck(label.transform, row.transform);
                Plugin.Trace("[spellbook] dodges entry removed, in combat the book has spells only");
            }
            catch (Exception e) { Plugin.Trace("[spellbook] dodges: " + e.Message); }
        }

        private static T Grab<T>(UserMenuSpellBookPanelContent panel, string name) where T : Component
        {
            return AccessTools.Field(typeof(UserMenuSpellBookPanelContent), name)?.GetValue(panel) as T;
        }

        private static GameObject Owner(Transform one)
        {
            return one.parent != null ? one.parent.gameObject : one.gameObject;
        }

        private static void Tuck(Transform one, Transform row)
        {
            for (var step = one; step != null; step = step.parent)
                if (step == row) return;
            if (one.gameObject.activeSelf) one.gameObject.SetActive(false);
        }

        private static void Shut()
        {
            try
            {
                _solo = false;
                var ctrl = Controllers.Get<UserMenuController>();
                if (ctrl == null || !ctrl.IsWindowOpened) return;
                AccessTools.Method(typeof(UserMenuController), "CloseWindow")?.Invoke(ctrl, null);
            }
            catch (Exception e) { Plugin.Trace("[spellbook] close: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(UserMenuSpellBookPanelContentResolver), "InternalActivatePanel")]
    internal static class SpellsPanelPatch
    {
        private static void Postfix(object __instance, bool __result)
        {
            if (!__result) return;
            try { Spells.Veil(__instance); }
            catch { }
            try { Spells.Trim(__instance); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UserMenuSpellBookPanelContentResolver), "OnItemClick")]
    internal static class SpellsClickPatch
    {
        private static bool Prefix(IItemRenderer<UserMenuSpellBookPanelContentDto> itemRenderer)
        {
            try
            {
                var data = itemRenderer != null ? itemRenderer.Data : null;
                if (data == null) return true;
                if (data.ContentType == UserMenuSpellBookPanelContentResolver.EUserMenuSpellBookSelectionButton.DODGES) return true;
                return !Spells.Pick(data.Id);
            }
            catch (Exception e) { Plugin.Trace("[spellbook] click: " + e.Message); return true; }
        }
    }

    internal sealed class SpellBookDim : MonoBehaviour
    {
        private UserMenuSpellBookItemRenderer _cell;
        private CanvasGroup _veil;
        private float _at;

        private void Update()
        {
            if (Time.unscaledTime < _at) return;
            _at = Time.unscaledTime + 0.25f;
            if (_cell == null) _cell = GetComponent<UserMenuSpellBookItemRenderer>();
            var data = _cell != null ? _cell.Data : null;
            bool ready = data == null
                         || data.ContentType == UserMenuSpellBookPanelContentResolver.EUserMenuSpellBookSelectionButton.DODGES
                         || SkillList.SpellReady(data.Id);
            if (_veil == null)
            {
                if (ready) return;
                _veil = gameObject.AddComponent<CanvasGroup>();
            }
            float want = ready ? 1f : 0.4f;
            if (_veil.alpha != want) _veil.alpha = want;
        }
    }

    [HarmonyPatch(typeof(UserMenuSpellBookItemRenderer), "Data", MethodType.Setter)]
    internal static class SpellBookDimPatch
    {
        private static void Postfix(UserMenuSpellBookItemRenderer __instance)
        {
            try
            {
                if (__instance != null && __instance.GetComponent<SpellBookDim>() == null)
                    __instance.gameObject.AddComponent<SpellBookDim>();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(UserMenuController), "CloseWindow")]
    internal static class SpellsShutPatch
    {
        private static void Postfix()
        {
            try { Spells.Forget(); }
            catch { }
        }
    }
}
