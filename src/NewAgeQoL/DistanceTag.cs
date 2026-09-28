using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class DistanceTag
    {
        private const string TagName = "QoLDistance";
        private const float Every = 0.15f;

        private static readonly Color Near = new Color32(255, 176, 96, 255);
        private static readonly Color Far = new Color32(150, 220, 255, 255);

        private static readonly List<GameObject> Shown = new List<GameObject>();
        private static readonly List<GameObject> Now = new List<GameObject>();
        private static float _next;
        private static bool _fighting;
        internal static readonly HashSet<int> Skip = new HashSet<int>();

        private static ICombatData Combat()
        {
            try { return Controllers.User?.CombatData; }
            catch { return null; }
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            try
            {
                Now.Clear();
                bool fighting = SideButtons.InCombat();
                if (_fighting && !fighting) Skip.Clear();
                _fighting = fighting;
                var cd = fighting ? Combat() : null;
                var me = cd != null ? cd.MyCharacter : null;
                if (me != null && cd.Characters != null && cd.Characters.TryGetValue(me.UserId, out var mine) && ReferenceEquals(mine, me))
                {
                    int reach = 0;
                    try { reach = me.StrikeRange; } catch { }
                    foreach (var pair in cd.Characters)
                    {
                        var ch = pair.Value;
                        if (!(ch is PlayerCharacter) || ch.UserId == me.UserId || Skip.Contains(ch.UserId)) continue;
                        var caption = cd.GetCharacterCaption(ch.UserId);
                        if (caption == null) continue;
                        int far;
                        try { far = HexUtils.range(me.HexGridPosition, ch.HexGridPosition); }
                        catch { continue; }
                        var tag = Put(caption, far, !ch.Dead && ch.Team != me.Team && reach > 0 && far <= reach);
                        if (tag != null) Now.Add(tag);
                    }
                }
                foreach (var go in Shown)
                    if (go != null && !Now.Contains(go) && go.activeSelf) go.SetActive(false);
                Shown.Clear();
                Shown.AddRange(Now);
            }
            catch (Exception e) { Plugin.Trace("[distance] " + e.Message); }
        }

        private static GameObject Put(LifeManaEnergyDisplay caption, int far, bool near)
        {
            var loginGo = Unity3DHelper.FindInChild(caption.gameObject, "LoginText");
            var login = loginGo != null ? loginGo.GetComponent<Text>() : null;
            if (login == null) return null;
            foreach (var stray in caption.GetComponentsInChildren<RectTransform>(true))
                if (stray.name == TagName && stray.parent != login.transform) UnityEngine.Object.Destroy(stray.gameObject);
            var t = login.transform.Find(TagName);
            var go = t != null ? t.gameObject : Build(login);
            if (go == null) return null;
            var text = go.GetComponentInChildren<Text>(true);
            string line = far.ToString();
            if (text.text != line) text.text = line;
            var color = near ? Near : Far;
            if (text.color != color) text.color = color;
            int size = Mathf.Max(12, login.fontSize + 3);
            if (text.fontSize != size) text.fontSize = size;
            var rt = (RectTransform)go.transform;
            float w = Mathf.Max(size * 1.9f, text.preferredWidth + size * 1.1f);
            float h = size * 1.45f;
            var want = new Vector2(w, h);
            if ((rt.sizeDelta - want).sqrMagnitude > 0.25f) rt.sizeDelta = want;
            var at = new Vector2(0f, login.preferredHeight * 0.5f + 2f);
            if ((rt.anchoredPosition - at).sqrMagnitude > 0.25f) rt.anchoredPosition = at;
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        private static GameObject Build(Text login)
        {
            var go = new GameObject(TagName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(login.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.localScale = Vector3.one;

            var textGo = new GameObject("value", typeof(RectTransform), typeof(Text), typeof(Outline));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = login.font;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.text = "";
            var shade = textGo.GetComponent<Outline>();
            shade.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shade.effectDistance = new Vector2(1.6f, -1.6f);
            return go;
        }
    }

    [HarmonyPatch(typeof(CombatController), "OnPlayersDescResponse")]
    internal static class DistanceTagNeutralPatch
    {
        private static void Prefix(object response)
        {
            try
            {
                var desc = response as PlayersDescResponseMessage;
                if (desc == null || desc.Players == null) return;
                foreach (var one in desc.Players)
                    if (one != null && one.Fitment == true && DistanceTag.Skip.Add(one.UserId))
                        Plugin.Trace("[distance] no distance over neutral object " + one.UserId);
            }
            catch (Exception e) { Plugin.Trace("[distance] fighters: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatController), "OnSummonCreatureResponse")]
    internal static class DistanceTagSummonPatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                var one = msg as SummonCreatureResponseMessage;
                if (one == null) return;
                if (DistanceTag.Skip.Add(one.UserId)) Plugin.Trace("[distance] no distance over summoned " + one.UserId);
            }
            catch (Exception e) { Plugin.Trace("[distance] summon: " + e.Message); }
        }
    }
}
