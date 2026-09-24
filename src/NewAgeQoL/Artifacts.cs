using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Shop;
using Transport.Messages.Responses.Things.Thingtabs;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(LocationMapBuilder), "Build")]
    public static class ArtifactsMapPatch
    {
        private static void Postfix(LocationMap __result)
        {
            if (__result != null) Artifacts.LastMap = __result;
        }
    }

    internal static class Artifacts
    {
        private const int IlleniumLoc = 2, BankLoc = 21;
        private const int WinInventory = -104, WinBox = -100, WinByLocation = -105;
        private const int BtnDress = 1, BtnDressExtra = 2, BtnTakeOff = 4, BtnGetFromBox = 0x4000, BtnPutToBox = 0x8000;
        private const int ArtRarity = 4;

        internal static bool Busy;
        internal static bool Internal;
        internal static string Status = "";
        internal static float StatusAt;

        private struct Item
        {
            internal int Inv, Tab, ThingId, Qty, Rarity;
            internal string Name;
        }

        private static readonly List<Item> Scanned = new List<Item>();
        private static readonly List<InventoryWearResponseMessageItem> Worn = new List<InventoryWearResponseMessageItem>();
        private static readonly Dictionary<int, (bool Ok, string Err)> Ctx = new Dictionary<int, (bool, string)>();
        private static readonly HashSet<int> GotTabs = new HashSet<int>();
        private static List<int> _tabs;
        private static int _tabsWindow = int.MinValue;
        private static int _tabsAltWindow = int.MinValue;
        private static bool _gotWear;
        private static bool _lastOk;
        private static bool _exact;
        private static string _lastErr = "";
        private static bool _put;
        private static bool _listeners;

        private static string Saved
        {
            get { return Plugin.CfgStash != null ? Plugin.CfgStash.Value ?? "" : ""; }
            set { if (Plugin.CfgStash != null) Plugin.CfgStash.Value = value ?? ""; }
        }

        internal static bool HasStash => !string.IsNullOrEmpty(Saved.Trim());

        private static readonly Dictionary<int, InventoryWearResponseMessageItem> Slots = new Dictionary<int, InventoryWearResponseMessageItem>();
        private static bool _slotsFresh;

        internal static Dictionary<int, InventoryWearResponseMessageItem> WornSlots()
        {
            if (_slotsFresh) return Slots;
            _slotsFresh = true;
            Slots.Clear();
            foreach (var w in Worn)
                if (w != null && w.SlotId > 0 && !Slots.ContainsKey(w.SlotId)) Slots[w.SlotId] = w;
            return Slots;
        }

        internal static Dictionary<int, int> WornMap()
        {
            var map = new Dictionary<int, int>();
            foreach (var w in Worn)
                if (w != null && w.ThingId > 0 && !map.ContainsKey(w.ThingId)) map[w.ThingId] = w.SlotId;
            return map;
        }

        private static void Forget() => Saved = "";

        internal static int Pending => Recall().Count;

        internal static void ForgetStash()
        {
            Forget();
            ManikinOn = false;
            KeepTaken(new Dictionary<int, int>());
            Say("список возврата очищен");
        }

        internal static void Stash()
        {
            if (Busy || Plugin.Instance == null) return;
            if (SideButtons.ClaimLocked()) { Plugin.Trace("[art] из заявки на бой в хранилище не ходим"); return; }
            Plugin.Instance.StartCoroutine(StashRoutine());
        }

        internal static void Restore()
        {
            if (Busy || Plugin.Instance == null) return;
            if (SideButtons.ClaimLocked()) { Plugin.Trace("[art] из заявки на бой в хранилище не ходим"); return; }
            Plugin.Instance.StartCoroutine(RestoreRoutine());
        }

        private static IEnumerator StashRoutine()
        {
            Busy = true;
            try
            {
                if (ManikinOn && HasStash)
                {
                    Say("запасной набор уже надет, сначала верни вещи");
                    yield break;
                }

                yield return Plugin.Instance.StartCoroutine(EnsureStorage());
                if (!AtStorage()) { Say("не дошёл до хранилища (см. лог)"); yield break; }

                Say("смотрю, что надето");
                yield return Plugin.Instance.StartCoroutine(ScanWear());
                var worn = Worn.Where(w => w.Rarity == ArtRarity).ToList();

                var memory = worn.Select(w => new Slot { ThingId = w.ThingId, SlotId = w.SlotId }).ToList();

                if (worn.Count > 0)
                {
                    Say("снимаю надетое: " + worn.Count);
                    int off = 0;
                    foreach (var w in worn)
                    {
                        yield return Plugin.Instance.StartCoroutine(
                            Act(w.InventoryId, BtnTakeOff, WinInventory, 1, 1));
                        off++;
                        Step("снимаю надетое: " + off + " из " + worn.Count);
                    }
                    yield return Wait(0.2f);
                }

                Say("собираю вещи для хранилища");
                yield return Plugin.Instance.StartCoroutine(Scan(WinBox, 380));
                var art = Scanned.Where(i => i.Rarity == ArtRarity).ToList();
                if (art.Count == 0)
                {
                    if (memory.Count > 0) Remember(memory);
                    else Say("вещей для хранилища не нашёл, старый список цел");
                    yield return Plugin.Instance.StartCoroutine(DressManikin());
                    yield break;
                }

                Say("кладу в хранилище: " + art.Count);
                int done = 0;
                foreach (var it in art)
                {
                    if (!memory.Any(m => m.ThingId == it.ThingId))
                        memory.Add(new Slot { ThingId = it.ThingId, SlotId = 0 });
                    yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnPutToBox, WinBox, it.Tab, it.Qty));
                    done++;
                    Step("кладу в хранилище: " + done + " из " + art.Count);
                }

                foreach (var o in Recall())
                    if (!memory.Any(m => m.ThingId == o.ThingId && m.SlotId == o.SlotId))
                        memory.Add(o);

                Remember(memory);
                Say("сдал в хранилище: " + done);
                yield return Plugin.Instance.StartCoroutine(DressManikin());
            }
            finally { Busy = false; }
        }

        private static IEnumerator RestoreRoutine()
        {
            Busy = true;
            try
            {
                var memory = Recall();
                if (memory.Count == 0) { Say("список пуст — сначала сдай вещи"); yield break; }

                yield return Plugin.Instance.StartCoroutine(EnsureStorage());
                if (!AtStorage()) { Say("не дошёл до хранилища (см. лог)"); yield break; }

                yield return Plugin.Instance.StartCoroutine(ScanWear());
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));

                var have = new Dictionary<int, int>();
                foreach (var w in Worn)
                {
                    if (w == null || w.ThingId <= 0) continue;
                    have.TryGetValue(w.ThingId, out int had);
                    have[w.ThingId] = had + 1;
                }
                foreach (var it in Scanned)
                {
                    if (it.ThingId <= 0 || it.Qty <= 0) continue;
                    have.TryGetValue(it.ThingId, out int had);
                    have[it.ThingId] = had + it.Qty;
                }

                Say("смотрю хранилище");
                yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                KeepStorage();
                Plugin.Trace("[art] в хранилище видно " + Scanned.Count + ": "
                                    + string.Join(", ", Scanned.Select(i => i.ThingId + "/стак" + i.Inv + "/вкл" + i.Tab + "/шт" + i.Qty).ToArray()));

                var need = new Dictionary<int, int>();
                foreach (var m in memory)
                {
                    need.TryGetValue(m.ThingId, out int n);
                    need[m.ThingId] = n + 1;
                }
                foreach (int thing in new List<int>(need.Keys))
                {
                    if (!have.TryGetValue(thing, out int mine) || mine <= 0) continue;
                    need[thing] = need[thing] > mine ? need[thing] - mine : 0;
                }

                var take = new List<Item>();
                foreach (var it in Scanned)
                {
                    if (!need.TryGetValue(it.ThingId, out int left) || left <= 0) continue;
                    int qty = it.Qty < left ? it.Qty : left;
                    need[it.ThingId] = left - qty;
                    var copy = it;
                    copy.Qty = qty;
                    take.Add(copy);
                }

                int done = 0;
                if (take.Count > 0)
                {
                    Say("забираю из хранилища: " + take.Count);
                    foreach (var it in take)
                    {
                        yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnGetFromBox, WinByLocation, it.Tab, it.Qty));
                        done++;
                        Step("забираю из хранилища: " + done + " из " + take.Count);
                    }
                    yield return Wait(0.25f);
                    yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                }
                else Say("в хранилище нет вещей из списка, смотрю сумку");

                yield return Plugin.Instance.StartCoroutine(UndressManikin(memory));

                var already = new Dictionary<int, int>();
                foreach (var w in Worn)
                {
                    if (w == null || w.ThingId <= 0) continue;
                    already.TryGetValue(w.ThingId, out int had);
                    already[w.ThingId] = had + 1;
                }

                var dress = new List<Slot>();
                foreach (var m in memory.Where(x => x.SlotId > 0).OrderBy(x => x.SlotId))
                {
                    if (_exact)
                    {
                        bool inPlace = false;
                        foreach (var w in Worn)
                            if (w != null && w.SlotId == m.SlotId && w.ThingId == m.ThingId) { inPlace = true; break; }
                        if (!inPlace) dress.Add(m);
                        continue;
                    }
                    if (already.TryGetValue(m.ThingId, out int left) && left > 0)
                    {
                        already[m.ThingId] = left - 1;
                        continue;
                    }
                    dress.Add(m);
                }
                if (dress.Count > 0)
                {
                    var plan = new System.Text.StringBuilder();
                    foreach (var d in dress) plan.Append(Name(d.ThingId)).Append(" -> ").Append(Manikin.SlotName(d.SlotId)).Append("; ");
                    Plugin.Trace("[возврат] надо надеть: " + plan);
                }

                int back = 0;
                if (dress.Count > 0)
                {
                    var busySlots = Worn
                        .Where(w => dress.Any(d => d.SlotId == w.SlotId && d.ThingId != w.ThingId))
                        .ToList();

                    if (busySlots.Count > 0)
                    {
                        Say("освобождаю слоты: " + busySlots.Count);
                        foreach (var w in busySlots)
                            yield return Plugin.Instance.StartCoroutine(
                                Act(w.InventoryId, BtnTakeOff, WinInventory, 1, 1));
                        yield return Wait(0.2f);
                        yield return Plugin.Instance.StartCoroutine(ScanWear());
                        yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                    }

                    Say("надеваю: " + dress.Count);
                    var used = new HashSet<int>();
                    foreach (var d in dress)
                    {
                        yield return Plugin.Instance.StartCoroutine(Put(d.ThingId, d.SlotId, used));
                        if (_put) back++;
                        Step("надеваю: " + back + " из " + dress.Count);
                    }
                }

                yield return Wait(0.2f);
                yield return Plugin.Instance.StartCoroutine(ReturnTaken(memory));
                yield return Plugin.Instance.StartCoroutine(ScanWear());
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));

                var wornLeft = Worn.Select(w => w.ThingId).ToList();
                var bagLeft = Scanned.Select(i => i.ThingId).ToList();
                var missed = new List<Slot>();
                foreach (var m in memory)
                {
                    bool ok = m.SlotId > 0 ? wornLeft.Remove(m.ThingId) : bagLeft.Remove(m.ThingId);
                    if (!ok) missed.Add(m);
                }

                if (missed.Count > 0)
                {
                    var lost = new System.Text.StringBuilder();
                    foreach (var m in missed) lost.Append(Name(m.ThingId)).Append(" (").Append(Manikin.SlotName(m.SlotId)).Append("); ");
                    Plugin.Warn("[возврат] не вернулось: " + lost);
                    var now = new System.Text.StringBuilder();
                    foreach (var w in Worn)
                        if (w != null && w.ThingId > 0)
                            now.Append(Manikin.SlotName(w.SlotId)).Append('=').Append(Name(w.ThingId)).Append("; ");
                    Plugin.Warn("[возврат] по итогу надето: " + now);
                }

                if (missed.Count == 0)
                {
                    ManikinOn = false;
                    Forget();
                    Say("вернул " + memory.Count + ", надел " + back);
                }
                else
                {
                    Remember(missed);
                    Say("вернул " + (memory.Count - missed.Count) + ", осталось " + missed.Count
                        + " — нажми ещё раз");
                }
            }
            finally { Busy = false; }
        }

        private static bool ManikinOn
        {
            get { return Plugin.CfgManikinOn != null && Plugin.CfgManikinOn.Value; }
            set { if (Plugin.CfgManikinOn != null) Plugin.CfgManikinOn.Value = value; }
        }

        private static IEnumerator DressManikin()
        {
            var plan = Manikin.Saved();
            if (plan.Count == 0) yield break;

            yield return Plugin.Instance.StartCoroutine(ScanWear());
            var worn = WornSlots();

            if (!ManikinOn)
            {
                var before = Recall();
                foreach (var pair in worn)
                {
                    var w = pair.Value;
                    if (w == null || w.ThingId <= 0) continue;
                    if (!before.Any(m => m.ThingId == w.ThingId && m.SlotId == pair.Key))
                        before.Add(new Slot { ThingId = w.ThingId, SlotId = pair.Key });
                }
                Remember(before);
            }

            var need = new Dictionary<int, int>();
            foreach (var pair in plan)
            {
                int n;
                need.TryGetValue(pair.Value, out n);
                need[pair.Value] = n + 1;
            }

            var stay = new HashSet<int>();
            foreach (var pair in plan)
            {
                InventoryWearResponseMessageItem item;
                if (!worn.TryGetValue(pair.Key, out item) || item == null || item.ThingId != pair.Value) continue;
                int n;
                if (!need.TryGetValue(pair.Value, out n) || n <= 0) continue;
                need[pair.Value] = n - 1;
                stay.Add(pair.Key);
            }
            var free = new List<InventoryWearResponseMessageItem>();
            foreach (var pair in worn)
            {
                var item = pair.Value;
                if (item == null || stay.Contains(pair.Key)) continue;
                free.Add(item);
            }

            int missing = 0;
            foreach (var pair in need) if (pair.Value > 0) missing += pair.Value;
            if (free.Count == 0 && missing == 0) { ManikinOn = true; Say("запасной набор уже надет"); yield break; }

            var wish = new System.Text.StringBuilder();
            foreach (var pair in need)
                if (pair.Value > 0) wish.Append(Name(pair.Key)).Append(" x").Append(pair.Value).Append("; ");
            Plugin.Trace("[набор] надо надеть: " + (wish.Length > 0 ? wish.ToString() : "ничего")
                                + " | снять лишнего: " + free.Count);
            Say("переодеваюсь в запасной набор");

            if (free.Count > 0)
            {
                Say("освобождаю слоты: " + free.Count);
                foreach (var w in free)
                    yield return Plugin.Instance.StartCoroutine(
                        Act(w.InventoryId, BtnTakeOff, WinInventory, 1, 1));
                yield return Wait(0.2f);
            }
            yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));

            var lack = new Dictionary<int, int>();
            foreach (var pair in need)
            {
                if (pair.Value <= 0) continue;
                int have = 0;
                foreach (var it in Scanned) if (it.ThingId == pair.Key) have += it.Qty;
                if (have < pair.Value) lack[pair.Key] = pair.Value - have;
            }

            if (lack.Count > 0 && AtStorage())
            {
                yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                var take = new List<Item>();
                foreach (var it in Scanned)
                {
                    int left;
                    if (!lack.TryGetValue(it.ThingId, out left) || left <= 0) continue;
                    int qty = it.Qty < left ? it.Qty : left;
                    lack[it.ThingId] = left - qty;
                    var copy = it;
                    copy.Qty = qty;
                    take.Add(copy);
                }
                var missed = new System.Text.StringBuilder();
                foreach (var pair in lack)
                    if (pair.Value > 0) missed.Append(Name(pair.Key)).Append(" x").Append(pair.Value).Append("; ");
                if (missed.Length > 0)
                    Plugin.Warn("[набор] в хранилище не нашлось: " + missed);

                if (take.Count > 0)
                {
                    var out_of_box = TakenOut();
                    foreach (var it in take)
                    {
                        out_of_box.TryGetValue(it.ThingId, out int had);
                        out_of_box[it.ThingId] = had + it.Qty;
                    }
                    KeepTaken(out_of_box);

                    Say("беру из хранилища: " + take.Count);
                    foreach (var it in take)
                        yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnGetFromBox, WinByLocation, it.Tab, it.Qty));
                    yield return Wait(0.25f);
                    yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                    KeepStorage();
                }
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
            }

            var order = new List<int>(plan.Keys);
            order.Sort();
            var used = new HashSet<int>();
            int on = 0, lost = 0;
            foreach (int slot in order)
            {
                int thing = plan[slot];
                int n;
                if (!need.TryGetValue(thing, out n) || n <= 0) continue;
                need[thing] = n - 1;
                yield return Plugin.Instance.StartCoroutine(Put(thing, slot, used));
                if (_put) { on++; Step("надеваю запасной набор: " + on); }
                else lost++;
            }

            yield return Wait(0.2f);
            yield return Plugin.Instance.StartCoroutine(ScanWear());
            ManikinOn = true;
            var notOn = new System.Text.StringBuilder();
            foreach (var pair in plan)
            {
                bool onMe = false;
                foreach (var w in Worn) if (w != null && w.ThingId == pair.Value) { onMe = true; break; }
                if (!onMe) notOn.Append(Name(pair.Value)).Append(" (").Append(Manikin.SlotName(pair.Key)).Append("); ");
            }
            if (notOn.Length > 0)
            {
                var now = new System.Text.StringBuilder();
                foreach (var w in Worn)
                    if (w != null && w.ThingId > 0)
                        now.Append(Manikin.SlotName(w.SlotId)).Append('=').Append(Name(w.ThingId)).Append("; ");
                Plugin.Warn("[набор] осталось ненадетым: " + notOn);
                Plugin.Warn("[набор] по итогу надето: " + now);
            }
            Say(lost > 0 ? "переоделся: надел " + on + ", не нашёл " + lost : "переоделся: надел " + on);
        }

        private static string Name(int thingId)
        {
            string name = Flasks.DisplayName(thingId);
            return (string.IsNullOrEmpty(name) ? "вещь" : "«" + name + "»") + " " + thingId;
        }

        private static Dictionary<int, int> TakenOut()
        {
            var map = new Dictionary<int, int>();
            string raw = Plugin.CfgManikinTaken != null ? Plugin.CfgManikinTaken.Value ?? "" : "";
            foreach (var part in raw.Split(','))
            {
                var pair = part.Split(':');
                if (pair.Length != 2) continue;
                if (!int.TryParse(pair[0].Trim(), out int thing) || !int.TryParse(pair[1].Trim(), out int qty)) continue;
                if (thing > 0 && qty > 0) map[thing] = qty;
            }
            return map;
        }

        private static void KeepTaken(Dictionary<int, int> map)
        {
            if (Plugin.CfgManikinTaken == null) return;
            var text = new System.Text.StringBuilder();
            foreach (var pair in map)
            {
                if (pair.Value <= 0) continue;
                if (text.Length > 0) text.Append(',');
                text.Append(pair.Key).Append(':').Append(pair.Value);
            }
            Plugin.CfgManikinTaken.Value = text.ToString();
        }

        private static IEnumerator ReturnTaken(List<Slot> memory)
        {
            var taken = TakenOut();
            if (taken.Count == 0 || !AtStorage()) yield break;

            var mine = new Dictionary<int, int>();
            foreach (var m in memory)
            {
                if (m.SlotId != 0) continue;
                mine.TryGetValue(m.ThingId, out int had);
                mine[m.ThingId] = had + 1;
            }

            yield return Plugin.Instance.StartCoroutine(Scan(WinBox, 380));
            var back = new List<Item>();
            foreach (var it in Scanned)
            {
                if (!taken.TryGetValue(it.ThingId, out int left) || left <= 0) continue;
                int spare = it.Qty;
                if (mine.TryGetValue(it.ThingId, out int hold) && hold > 0)
                {
                    int stay = hold < spare ? hold : spare;
                    mine[it.ThingId] = hold - stay;
                    spare -= stay;
                }
                if (spare <= 0) continue;
                int qty = spare < left ? spare : left;
                taken[it.ThingId] = left - qty;
                var copy = it;
                copy.Qty = qty;
                back.Add(copy);
            }

            if (back.Count > 0)
            {
                Say("возвращаю в хранилище: " + back.Count);
                foreach (var it in back)
                    yield return Plugin.Instance.StartCoroutine(Act(it.Inv, BtnPutToBox, WinBox, it.Tab, it.Qty));
                yield return Wait(0.25f);
                yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                KeepStorage();
            }

            KeepTaken(new Dictionary<int, int>());
            yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
        }

        private static IEnumerator Put(int thing, int slot, HashSet<int> used)
        {
            _put = false;
            for (int attempt = 0; attempt < 2 && !_put; attempt++)
            {
                var found = default(Item);
                foreach (var it in Scanned)
                    if (it.ThingId == thing && !used.Contains(it.Inv)) { found = it; break; }
                if (found.Inv == 0)
                {
                    if (attempt > 0) break;
                    yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                    used.Clear();
                    continue;
                }
                yield return Plugin.Instance.StartCoroutine(
                    Act(found.Inv, ExtraSlot(slot) ? BtnDressExtra : BtnDress, WinInventory, found.Tab, 1));
                if (_lastOk) { used.Add(found.Inv); _put = true; break; }
                yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
                used.Clear();
            }
            if (!_put)
                Plugin.Warn("[набор] не надел " + Name(thing) + " в слот " + slot + " (" + Manikin.SlotName(slot) + ")"
                                       + (_lastErr.Length > 0 ? ": " + _lastErr : ": вещи нет в сумке"));
        }

        private static IEnumerator UndressManikin(List<Slot> memory)
        {
            _exact = false;
            var plan = Manikin.Saved();
            if (plan.Count == 0) yield break;
            _exact = true;

            var off = new List<InventoryWearResponseMessageItem>();
            foreach (var w in Worn)
            {
                if (w == null || w.ThingId <= 0) continue;
                bool keep = false;
                foreach (var m in memory)
                    if (m.SlotId == w.SlotId && m.ThingId == w.ThingId) { keep = true; break; }
                if (!keep) off.Add(w);
            }
            if (off.Count == 0) yield break;

            Say("снимаю запасной набор: " + off.Count);
            foreach (var w in off)
                yield return Plugin.Instance.StartCoroutine(
                    Act(w.InventoryId, BtnTakeOff, WinInventory, 1, 1));
            yield return Wait(0.2f);
            yield return Plugin.Instance.StartCoroutine(ScanWear());
            yield return Plugin.Instance.StartCoroutine(Scan(WinInventory, 380));
        }

        private static IEnumerator EnsureStorage()
        {
            if (AtStorage()) yield break;

            if (Loc() != IlleniumLoc && Loc() != BankLoc)
            {
                Say("возвращаюсь в город");
                Send(new ReturnToIlleniumRequest(true));
                float t = 0f;
                while (t < 60f && Loc() != IlleniumLoc) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }

            if (Loc() == IlleniumLoc)
            {
                Say("иду в банк");
                Send(new ChangeMapRequest(BankLoc));
                float t = 0f;
                while (t < 60f && Loc() != BankLoc) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }

            if (Loc() == BankLoc)
            {
                int door = StorageDoor();
                if (door <= 0) { Say("в банке не нашёл дверь хранилища"); yield break; }
                Say("иду в хранилище");
                Send(new ChangeMapRequest(door));
                float t = 0f;
                while (t < 60f && Loc() != door) { yield return null; t += Time.unscaledDeltaTime; }
                yield return Wait(0.6f);
            }
        }

        private static void KeepStorage()
        {
            var seen = new Dictionary<int, int>();
            foreach (var it in Scanned)
            {
                if (it.ThingId <= 0 || it.Qty <= 0) continue;
                int had;
                seen.TryGetValue(it.ThingId, out had);
                seen[it.ThingId] = had + it.Qty;
            }
            Storage.Remember(seen);
            Plugin.Trace("[art] запомнил хранилище: видов вещей " + seen.Count);
        }

        internal static void RequestStorage()
        {
            if (Busy || Plugin.Instance == null || !NearStorage()) return;
            Plugin.Instance.StartCoroutine(StorageRoutine());
        }

        private static IEnumerator StorageRoutine()
        {
            Busy = true;
            try
            {
                yield return Plugin.Instance.StartCoroutine(Scan(WinByLocation, 381));
                KeepStorage();
            }
            finally { Busy = false; }
        }

        internal static void RequestWear()
        {
            try
            {
                EnsureListeners();
                Send(new InventoryWearRequest());
            }
            catch (System.Exception e) { Plugin.Trace("[art] запрос надетого: " + e.Message); }
        }

        internal static bool NearStorage()
        {
            try { return AtStorage(); }
            catch { return false; }
        }

        private static bool AtStorage()
        {
            var map = LastMap;
            if (map == null || map.SceneObjects == null) return false;
            foreach (var so in map.SceneObjects)
                if (so != null && (so.ObjectType == EObjectType.PersonalStoragePut || so.ObjectType == EObjectType.PersonalStorageGet))
                    return true;
            return false;
        }

        internal static LocationMap LastMap;

        private static int StorageDoor()
        {
            var map = LastMap;
            if (map == null || map.SceneObjects == null) { Plugin.Warn("[art] карта банка не разобрана"); return 0; }
            int found = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var so in map.SceneObjects)
            {
                if (so == null || so.ObjectType != EObjectType.Link) continue;
                sb.Append("id=").Append(so.Id).Append(" sn=").Append(so.SceneObjectName)
                  .Append(" txt=").Append(so.Text).Append(" img=").Append(so.SpriteName).Append("; ");
                string n = (so.SceneObjectName ?? "") + " " + (so.Text ?? "") + " " + (so.SpriteName ?? "");
                if (found == 0 && (Has(n, "safe") || Has(n, "storage") || Has(n, "vault") || Has(n, "box")))
                    found = so.Id;
            }
            Plugin.Trace("[art] двери локации " + Loc() + ": " + sb + "-> хранилище: " + (found > 0 ? found.ToString() : "не найдено"));
            return found;
        }

        private static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static IEnumerator Scan(int window, short response)
        {
            EnsureListeners();
            Scanned.Clear();
            GotTabs.Clear();
            _tabs = null;
            _tabsWindow = window;
            _tabsAltWindow = window == WinByLocation ? Loc() : window;

            Send(new GetThingsTabsRequest(window));
            float t = 0f;
            while (t < 5f && _tabs == null) { yield return null; t += Time.unscaledDeltaTime; }
            if (_tabs == null) yield break;

            var tabs = new List<int>(_tabs);
            foreach (int tab in tabs)
            {
                Send(new GetTabContentRequest(tab, window));
                yield return null;
            }

            t = 0f;
            while (t < 5f && GotTabs.Count < tabs.Count) { yield return null; t += Time.unscaledDeltaTime; }
        }

        internal static IEnumerator FetchWear() => ScanWear();

        internal static bool WearArrived => _gotWear;

        private static IEnumerator ScanWear()
        {
            EnsureListeners();
            Worn.Clear();
            _slotsFresh = false;
            _gotWear = false;
            Send(new InventoryWearRequest());
            float t = 0f;
            while (t < 5f && !_gotWear) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator Act(int inv, int button, int window, int tab, int qty)
        {
            lock (Ctx) Ctx.Remove(inv);
            Send(new ContextActionRequest(inv, button, window, tab, qty));
            float t = 0f;
            while (t < 5f)
            {
                bool has;
                lock (Ctx) has = Ctx.ContainsKey(inv);
                if (has) break;
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            lock (Ctx)
            {
                if (Ctx.TryGetValue(inv, out var r))
                {
                    _lastOk = r.Ok;
                    _lastErr = r.Ok ? "" : (r.Err ?? "");
                    if (r.Ok)
                        Plugin.Trace("[art] кнопка " + button + " стак " + inv + " окно " + window + " вкладка " + tab + " -> ок");
                    else
                        Plugin.Warn("[art] кнопка " + button + " стак " + inv + " окно " + window + " вкладка " + tab
                                               + " -> отказ: " + r.Err);
                }
                else
                {
                    _lastOk = false;
                    _lastErr = "сервер не ответил";
                    Plugin.Warn("[art] кнопка " + button + " стак " + inv + " окно " + window + " вкладка " + tab
                                           + " -> сервер не ответил");
                }
                Ctx.Remove(inv);
            }
        }

        private static void EnsureListeners()
        {
            if (_listeners) return;
            var nc = NetworkConnection.Instance;
            if (nc == null) return;
            nc.AddMessageListener(359, OnTabs);
            nc.AddMessageListener(380, OnBagTab);
            nc.AddMessageListener(381, OnShopTab);
            nc.AddMessageListener(412, OnWear);
            nc.AddMessageListener(416, OnAction);
            _listeners = true;
        }

        private static void OnTabs(object m)
        {
            if (m is ThingTypeTabsResponseMessage t && t.TabIds != null && (t.WindowId == _tabsWindow || t.WindowId == _tabsAltWindow))
                _tabs = t.TabIds.Distinct().ToList();
        }

        private static void OnBagTab(object m)
        {
            if (!(m is ThingTabInventoryResponseMessage inv) || inv.WindowId != _tabsWindow && inv.WindowId != _tabsAltWindow) return;
            GotTabs.Add(inv.TabNumber);
            if (inv.Things == null) return;
            foreach (var th in inv.Things)
            {
                if (th.Inventories == null) continue;
                foreach (var ii in th.Inventories)
                {
                    Add(new Item { Inv = ii.InventoryId, Tab = inv.TabNumber, ThingId = th.ThingId, Qty = ii.Quantity, Rarity = th.Rarity, Name = th.Name ?? "" });
                    Flasks.NoteUse(th.ThingId, ii.CanUse ?? 0);
                    Flasks.Note(th.ThingId, th.Image, th.SubType, th.Rarity, th.Level);
                }
            }
        }

        private static void OnShopTab(object m)
        {
            if (!(m is ShopTabContentResponseMessage shop) || shop.WindowId != _tabsWindow && shop.WindowId != _tabsAltWindow) return;
            int tab = shop.TabId.GetValueOrDefault();
            GotTabs.Add(tab);
            if (shop.Items == null) return;
            foreach (var it in shop.Items)
            {
                if (it == null || it.ThingInfo == null) continue;
                Add(new Item
                {
                    Inv = it.ShopItemId.GetValueOrDefault(),
                    Tab = tab,
                    ThingId = it.ThingInfo.ThingId,
                    Qty = it.Quantity.GetValueOrDefault(1),
                    Rarity = it.ThingInfo.Rarity,
                    Name = it.ThingInfo.Name ?? "",
                });
                Flasks.NoteUse(it.ThingInfo.ThingId, it.CanUse);
                Flasks.Note(it.ThingInfo.ThingId, it.ThingInfo.Image, it.ThingInfo.SubType,
                            it.ThingInfo.Rarity, it.ThingInfo.Level.GetValueOrDefault());
            }
        }

        private static void Add(Item item)
        {
            if (item.Inv == 0 || item.Qty <= 0) return;
            foreach (var s in Scanned) if (s.Inv == item.Inv) return;
            Scanned.Add(item);
        }

        private static void OnWear(object m)
        {
            if (!(m is InventoryWearResponseMessage w)) return;
            Worn.Clear();
            if (w.Items != null) Worn.AddRange(w.Items);
            _slotsFresh = false;
            _gotWear = true;
        }

        private static void OnAction(object m)
        {
            if (m is ThingContextActionResponseMessage r)
                lock (Ctx) Ctx[r.Id] = (r.Success, r.ErrorMessage);
        }

        private struct Slot
        {
            internal int ThingId, SlotId;
        }

        private static void Remember(List<Slot> slots)
        {
            var parts = slots.Select(x => x.ThingId + ";" + x.SlotId).ToArray();
            Saved = string.Join(",", parts);
            Plugin.Trace("[art] запомнил " + parts.Length + ": " + Saved);
        }

        private static List<Slot> Recall()
        {
            var list = new List<Slot>();
            foreach (var pair in Saved.Split(','))
            {
                var parts = pair.Split(';');
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0].Trim(), out int id) || !int.TryParse(parts[1].Trim(), out int slot)) continue;
                list.Add(new Slot { ThingId = id, SlotId = slot });
            }
            return list;
        }

        private static bool ExtraSlot(int slot) => slot >= 19 && slot <= 24;

        private static void Send(BaseRequest request)
        {
            Internal = true;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc != null && nc.IsConnected()) nc.SendRequest(request);
            }
            catch (System.Exception e) { Plugin.Fault("[art] " + e.Message); }
            finally { Internal = false; }
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static int Loc()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null ? ud.CurrentLocationId : -1;
            }
            catch { return -1; }
        }

        private static void Say(string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
            Plugin.Trace("[art] " + text + " (loc=" + Loc() + ")");
            try { AirMessageScript.ShowInformationNotification("Вещи: " + text); } catch { }
        }

        internal static void Step(string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
        }
    }
}
