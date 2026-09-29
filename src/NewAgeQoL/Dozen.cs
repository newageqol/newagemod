using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Dozen
    {
        private const int Kind = 4;
        private const int Room = 335;
        private const int Each = 2;
        private const float Icon = 16f;
        private const float Row = 20f;
        private const float Thin = 46f;

        private sealed class Slot
        {
            internal int Class;
            internal Image Pic;
            internal Text Left;
        }

        private static bool _joined;
        private static bool _inside;
        private static GameObject _go;
        private static readonly List<Slot> Slots = new List<Slot>();
        private static string _mark = "";
        private static float _next;

        internal static void Selected(AbstractEnterFightController ctrl, int announceId)
        {
            try
            {
                int kind = KindOf(ctrl, announceId);
                _joined = kind == Kind;
                _inside = false;
                Plugin.Trace("[dozen] joining claim " + announceId + ", kind " + kind + (_joined ? " - Dozen" : ""));
            }
            catch (Exception e) { Plugin.Trace("[dozen] join: " + e.Message); }
        }

        private static readonly System.Reflection.FieldInfo AnnounceField = AccessTools.Field(typeof(BaseEnterfightWidget), "_announce");

        private static int KindOf(AbstractEnterFightController ctrl, int announceId)
        {
            var view = Traverse.Create(ctrl).Property("EnterfightView").GetValue<BaseEnterfightView>();
            if (view == null || view.Widgets == null || AnnounceField == null) return -1;
            foreach (var widget in view.Widgets)
            {
                if (widget == null || widget.Id != announceId) continue;
                var one = AnnounceField.GetValue(widget) as FightAnnounce;
                if (one != null) return one.ClaimType;
            }
            return -1;
        }

        internal static void Moved(int map, int type)
        {
            if (_joined) Plugin.Trace("[dozen] map " + map + ", type " + type);
            bool was = _inside;
            _inside = _joined && type <= 0 && map == Room;
            if (!_inside) _joined = false;
            if (was != _inside) Plugin.Trace("[dozen] " + (_inside ? "in the Dozen waiting room" : "left the Dozen waiting room"));
            _mark = "";
            _next = 0f;
        }

        internal static void Tick()
        {
            try
            {
                var host = _inside ? ChatDock.ListStrip : null;
                if (host == null) { Hide(); return; }
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.3f;
                if (_go == null || _go.transform.parent != host) Build(host);
                if (!_go.activeSelf) _go.SetActive(true);
                Fill();
            }
            catch (Exception e) { Plugin.Trace("[dozen] " + e.Message); }
        }

        private static void Hide()
        {
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }

        private static void Fill()
        {
            var have = new int[7];
            var chat = DependencyContainer.GetContainer()?.Resolve<IChat>();
            var list = chat?.PlayersOnLocation;
            if (list != null && list.Content != null)
                foreach (var row in list.Content)
                {
                    int id = row != null ? row.ClassId.GetValueOrDefault() : 0;
                    if (id >= 1 && id <= 6) have[id]++;
                }

            var mark = new System.Text.StringBuilder();
            for (int id = 1; id <= 6; id++) mark.Append(have[id]).Append(',');
            foreach (var slot in Slots)
                if (slot.Pic != null && (slot.Pic.sprite == null || slot.Pic.sprite.texture == null))
                {
                    Sprite art = null;
                    try { art = AtlasUtils.GetSmallClassIcon((ERPGClass)slot.Class); } catch { art = null; }
                    if (art != null) slot.Pic.sprite = art;
                    mark.Append('+');
                }
            string now = mark.ToString();
            if (now == _mark) return;
            _mark = now;

            int at = 0;
            foreach (var slot in Slots)
            {
                int count = have[slot.Class];
                slot.Left.text = count.ToString();
                slot.Left.color = count >= Each ? WardrobeLook.Good : WardrobeLook.Accent;
                Put(slot, at++);
                if (slot.Pic != null)
                    slot.Pic.enabled = slot.Pic.sprite != null && slot.Pic.sprite.texture != null;
            }
        }

        private static void Put(Slot slot, int index)
        {
            float top = -(4f + index * Row) - (Row - Icon) / 2f;
            if (slot.Pic != null)
                OnlineWindow.Place(slot.Pic.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f, top - Icon), new Vector2(8f + Icon, top));
            OnlineWindow.Place(slot.Left.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(8f + Icon + 5f, top - Icon), new Vector2(-4f, top));
        }

        private static float Tall(int rows)
        {
            return 8f + rows * Row;
        }

        private static void Build(Transform host)
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            Slots.Clear();
            _mark = "";

            _go = new GameObject("QoLDozen", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            _go.transform.SetParent(host, false);
            _go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)_go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(0f, 3f);
            rt.sizeDelta = new Vector2(Thin, Tall(6));
            var back = _go.GetComponent<Image>();
            back.color = WardrobeLook.Card;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            for (int i = 0; i < 6; i++)
            {
                var slot = new Slot { Class = i + 1 };
                float top = -(4f + i * Row) - (Row - Icon) / 2f;
                var picGo = new GameObject("class" + slot.Class, typeof(RectTransform), typeof(Image));
                picGo.transform.SetParent(_go.transform, false);
                OnlineWindow.Place((RectTransform)picGo.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f, top - Icon), new Vector2(8f + Icon, top));
                slot.Pic = picGo.GetComponent<Image>();
                slot.Pic.preserveAspect = true;
                slot.Pic.raycastTarget = false;
                slot.Pic.enabled = false;

                slot.Left = OnlineWindow.Label(_go.transform, "", 11, FontStyle.Bold, WardrobeLook.Accent);
                slot.Left.alignment = TextAnchor.MiddleLeft;
                slot.Left.raycastTarget = false;
                slot.Left.horizontalOverflow = HorizontalWrapMode.Overflow;
                OnlineWindow.Place(slot.Left.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f + Icon + 5f, top - Icon), new Vector2(-4f, top));
                Slots.Add(slot);
            }
            Plugin.Trace("[dozen] class counter built");
        }
    }

    [HarmonyPatch(typeof(AbstractEnterFightController), "OnAnnounceSelected")]
    internal static class DozenJoinPatch
    {
        private static void Prefix(AbstractEnterFightController __instance, int announceId)
        {
            Dozen.Selected(__instance, announceId);
        }
    }
}
