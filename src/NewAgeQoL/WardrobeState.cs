using System;
using System.Collections.Generic;

namespace NewAgeQoL
{
    internal sealed class WardrobeState
    {
        internal int RaceId = 13;
        internal int Gender = 1;
        internal int Level;
        internal int ClassId = 1;
        internal int SubN;
        internal int Rank;
        internal readonly int[] Dist = new int[7];
        internal readonly Dictionary<int, WardrobeThing> Worn = new Dictionary<int, WardrobeThing>();
        internal readonly Dictionary<int, string> Unknown = new Dictionary<int, string>();
        internal bool Short;

        internal WardrobeState Clone()
        {
            var copy = new WardrobeState
            {
                RaceId = RaceId,
                Gender = Gender,
                Level = Level,
                ClassId = ClassId,
                SubN = SubN,
                Rank = Rank,
                Short = Short
            };
            Array.Copy(Dist, copy.Dist, Dist.Length);
            foreach (var pair in Worn) copy.Worn[pair.Key] = pair.Value;
            foreach (var pair in Unknown) copy.Unknown[pair.Key] = pair.Value;
            return copy;
        }

        internal static string Ref(WardrobeThing thing)
        {
            if (thing.Art) return WardrobeArt.Code(thing);
            if (thing.Id > 0) return thing.Id.ToString();
            return "~" + Uri.EscapeDataString(thing.Name) + "~" + Uri.EscapeDataString(thing.Image) + "~" + thing.ItemLevel;
        }

        internal static string Title(string reference)
        {
            try
            {
                if (reference.StartsWith("~"))
                {
                    var bits = reference.Split('~');
                    if (bits.Length >= 2 && bits[1].Length > 0) return Uri.UnescapeDataString(bits[1]);
                }
                if (reference.StartsWith("@")) return "артефакт";
            }
            catch { }
            return "вещь " + reference;
        }

        private static bool Plain(string reference)
        {
            if (reference.Length == 0 || reference.Length > 300) return false;
            foreach (char ch in reference) if (ch < ' ' || ch == 127) return false;
            return true;
        }

        internal void Undress()
        {
            Worn.Clear();
            Unknown.Clear();
        }

        internal void Forget(int slot, WardrobeThing old)
        {
            Worn.Remove(slot);
            if (old != null) Unknown[slot] = Ref(old);
        }

        internal int Recall()
        {
            int back = 0;
            foreach (int slot in new List<int>(Unknown.Keys))
            {
                if (Worn.ContainsKey(slot)) { Unknown.Remove(slot); continue; }
                var thing = Resolve(Unknown[slot]);
                if (thing == null || !WardrobeData.Fits(slot, thing.Sub)) continue;
                WardrobeThing right;
                if (slot == 11 && Worn.TryGetValue(12, out right) && WardrobeData.IsTwoHand(right.Sub)) { Unknown.Remove(slot); continue; }
                if (slot == 12 && WardrobeData.IsTwoHand(thing.Sub) && Worn.ContainsKey(11)) { Unknown.Remove(slot); continue; }
                Worn[slot] = thing;
                Unknown.Remove(slot);
                back++;
            }
            return back;
        }

        internal string Pack()
        {
            var text = new System.Text.StringBuilder();
            text.Append("r=").Append(RaceId).Append(";g=").Append(Gender).Append(";l=").Append(Level)
                .Append(";c=").Append(ClassId).Append(";s=").Append(SubN).Append(";k=").Append(Rank)
                .Append(";d=").Append(string.Join(",", Dist)).Append(";w=");
            bool first = true;
            foreach (var pair in Worn)
            {
                if (pair.Value == null) continue;
                if (!first) text.Append(',');
                first = false;
                text.Append(pair.Key).Append(':').Append(Ref(pair.Value));
            }
            foreach (var pair in Unknown)
            {
                if (Worn.ContainsKey(pair.Key)) continue;
                if (!first) text.Append(',');
                first = false;
                text.Append(pair.Key).Append(':').Append(pair.Value);
            }
            return text.ToString();
        }

        internal int Unpack(string body)
        {
            RaceId = 13;
            Gender = 1;
            Level = 0;
            ClassId = 1;
            SubN = 0;
            Rank = 0;
            for (int i = 0; i < 7; i++) Dist[i] = 0;
            Undress();
            int lost = 0;
            foreach (var part in (body ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string name = part.Substring(0, eq);
                string value = part.Substring(eq + 1);
                int number;
                int.TryParse(value, out number);
                switch (name)
                {
                    case "r": RaceId = number; break;
                    case "g": Gender = number == 2 ? 2 : 1; break;
                    case "l": Level = number; break;
                    case "c": ClassId = number; break;
                    case "s": SubN = number; break;
                    case "k": Rank = number; break;
                    case "d":
                    {
                        var cells = value.Split(',');
                        for (int i = 0; i < 7 && i < cells.Length; i++)
                        {
                            int d;
                            if (int.TryParse(cells[i], out d) && d > 0 && d < 10000) Dist[i] = d;
                        }
                        break;
                    }
                    case "w":
                        foreach (var one in value.Split(','))
                        {
                            int colon = one.IndexOf(':');
                            int slot;
                            if (colon <= 0 || !int.TryParse(one.Substring(0, colon), out slot) || !WardrobeData.IsSlot(slot)) continue;
                            string reference = one.Substring(colon + 1);
                            var thing = Resolve(reference);
                            if (thing == null || !WardrobeData.Fits(slot, thing.Sub))
                            {
                                if (Plain(reference)) Unknown[slot] = reference;
                                lost++;
                                continue;
                            }
                            Worn[slot] = thing;
                        }
                        break;
                }
            }
            WardrobeThing right;
            if (Worn.TryGetValue(12, out right) && WardrobeData.IsTwoHand(right.Sub))
            {
                if (Worn.Remove(11)) lost++;
                Unknown.Remove(11);
            }
            if (WardrobeData.Race(RaceId) == null || WardrobeData.Race(RaceId).Id != RaceId) RaceId = 13;
            if (Rank < 0 || Rank >= WardrobeData.Ranks.Count) Rank = 0;
            if (SubN < 0 || SubN > 6) SubN = 0;
            Settle(true);
            return lost;
        }

        private static WardrobeThing Resolve(string reference)
        {
            if (reference.StartsWith("@")) return WardrobeArt.Parse(reference);
            if (reference.StartsWith("~"))
            {
                var bits = reference.Split('~');
                if (bits.Length < 4) return null;
                int level;
                int.TryParse(bits[3], out level);
                return WardrobeData.Named(Uri.UnescapeDataString(bits[1]), Uri.UnescapeDataString(bits[2]), level);
            }
            int id;
            return int.TryParse(reference, out id) ? WardrobeData.Thing(id) : null;
        }

        internal WardrobeRace Race => WardrobeData.Race(RaceId);
        internal WardrobeClass Klass => WardrobeData.Class(ClassId);

        internal WardrobeSub Sub
        {
            get
            {
                var klass = Klass;
                return SubN > 0 && klass != null && SubN <= klass.Subs.Count ? klass.Subs[SubN - 1] : null;
            }
        }

        internal WardrobeRank RankInfo => Rank >= 0 && Rank < WardrobeData.Ranks.Count ? WardrobeData.Ranks[Rank] : null;

        internal static int Points(int level) => level <= 0 ? 0 : (level + 2) * (level + 3) / 2 - 3;

        internal int MaxLevel
        {
            get
            {
                var race = Race;
                int top = 0;
                if (race != null) foreach (var key in race.Magic.Keys) if (key > top) top = key;
                return top > 0 ? top : 30;
            }
        }

        internal int Spent
        {
            get
            {
                int sum = 0;
                foreach (int d in Dist) sum += d;
                return sum;
            }
        }

        internal int Free => Points(Level) - Spent;

        internal int Base(int i) => Race.Base[i] + Dist[i];

        internal int Need(int i)
        {
            var sub = Sub;
            return sub != null ? sub.Req[i] : 0;
        }

        internal int Floor(int i) => Math.Max(Race.Base[i], Need(i));

        internal int TopSub
        {
            get
            {
                var klass = Klass;
                int top = 0;
                if (klass != null) foreach (var sub in klass.Subs) if (sub.Level <= Level && sub.N > top) top = sub.N;
                return top;
            }
        }

        internal bool Fits(WardrobeThing thing)
        {
            if (thing == null || thing.Level > Level) return false;
            for (int i = 0; i < 7; i++) if (Base(i) < thing.Req[i]) return false;
            return true;
        }

        internal int Missing(WardrobeThing thing, int[] add)
        {
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                int gap = Math.Max(0, thing.Req[i] - Base(i));
                if (add != null) add[i] = gap;
                sum += gap;
            }
            return sum;
        }

        internal bool Grant(WardrobeThing thing)
        {
            if (thing == null || thing.Level > Level) return false;
            var add = new int[7];
            int need = Missing(thing, add);
            if (need > Free) return false;
            for (int i = 0; i < 7; i++) Dist[i] += add[i];
            return true;
        }

        internal IEnumerable<KeyValuePair<int, WardrobeThing>> Active()
        {
            foreach (var pair in Worn)
                if (Fits(pair.Value)) yield return pair;
        }

        internal int Total(int i)
        {
            var sub = Sub;
            int sum = Base(i) + (sub != null ? sub.Bonus[i] : 0);
            foreach (var pair in Active()) sum += pair.Value.Bonus[i];
            return sum;
        }

        internal int Energy
        {
            get
            {
                int sum = 100;
                foreach (var pair in Active()) sum += pair.Value.Energy;
                return sum;
            }
        }

        internal int Armor(int p)
        {
            var sub = Sub;
            int sum = sub != null ? sub.Armor[p] : 0;
            foreach (var pair in Active()) sum += pair.Value.Armor[p];
            return sum;
        }

        internal int[] RaceMagic()
        {
            var race = Race;
            int[] found = null;
            if (race != null)
                foreach (var pair in race.Magic)
                    if (pair.Key <= Level) found = pair.Value;
            return found ?? new int[3];
        }

        internal int Magic(int m)
        {
            var sub = Sub;
            int sum = RaceMagic()[m] + (sub != null ? sub.Magic[m] : 0);
            foreach (var pair in Active()) sum += pair.Value.Magic[m];
            return sum;
        }

        internal int Life
        {
            get
            {
                long raw = ((long)Total(2) * (10 + Level) + 1) / 2;
                var rank = RankInfo;
                long mult = rank != null ? (long)Math.Round(rank.Mult * 100.0) : 100;
                return (int)((raw * mult + 50) / 100);
            }
        }

        internal int Mana => Total(4) * 6;

        internal int Rating
        {
            get
            {
                var sub = Sub;
                long stats = 0, armor = 0, magic = 0;
                for (int i = 0; i < 7; i++) stats += Total(i) - (sub != null ? sub.Bonus[i] : 0);
                for (int p = 0; p < 5; p++) armor += Armor(p) - (sub != null ? sub.Armor[p] : 0);
                for (int m = 0; m < 3; m++) magic += Magic(m) - (sub != null ? sub.Magic[m] : 0);
                long energy = Math.Max(0, Energy - 100);
                long num = (3 * stats + armor + 2 * magic + 36 * energy) * (15 + Level);
                if (num <= 0) return (int)(num / 150);
                return (int)((num + 149) / 150);
            }
        }

        internal int MinRank
        {
            get
            {
                var race = Race;
                return race != null ? Math.Max(0, Math.Min(race.Fortress, WardrobeData.Ranks.Count - 1)) : 0;
            }
        }

        internal bool CanRank(int n)
        {
            int floor = MinRank;
            if (n < floor || n >= WardrobeData.Ranks.Count) return false;
            if (n <= 0 || n == floor) return true;
            if (ClassId == WardrobeData.Ranger) return false;
            return Base(2) >= WardrobeData.Ranks[n].MinCon;
        }

        internal string Blocker(int i, int value)
        {
            foreach (var pair in Active())
                if (pair.Value.Req[i] > value) return "«" + pair.Value.Name + "» требует " + WardrobeData.StatNames[i].ToLowerInvariant() + " " + pair.Value.Req[i];
            var rank = RankInfo;
            if (i == 2 && rank != null && rank.N > MinRank && rank.MinCon > value)
                return "крепость «" + rank.Name + "» требует сложение " + rank.MinCon;
            return null;
        }

        internal void Wear(int slot, WardrobeThing thing)
        {
            Unknown.Remove(slot);
            if (thing == null) { Worn.Remove(slot); return; }
            WardrobeThing right;
            if (slot == 12 && WardrobeData.IsTwoHand(thing.Sub)) { Worn.Remove(11); Unknown.Remove(11); }
            if (slot == 11 && Worn.TryGetValue(12, out right) && WardrobeData.IsTwoHand(right.Sub)) Worn.Remove(12);
            Worn[slot] = thing;
        }

        internal void Settle(bool keepSub)
        {
            var race = Race;
            if (race == null) return;
            if (Level < 0) Level = 0;
            if (Level > MaxLevel) Level = MaxLevel;
            if (!race.Classes.Contains(ClassId) && race.Classes.Count > 0) ClassId = race.Classes[0];
            if (race.Id == 9) Gender = 1;
            int top = TopSub;
            if (!keepSub || SubN > top) SubN = top;

            var drop = new List<int>();
            foreach (var pair in Worn) if (pair.Value.Level > Level) drop.Add(pair.Key);
            foreach (int slot in drop) Worn.Remove(slot);

            Lift();
            if (Spent > Points(Level))
            {
                for (int i = 0; i < 7; i++) Dist[i] = 0;
                Lift();
            }
            Short = Spent > Points(Level);
            if (Rank >= WardrobeData.Ranks.Count) Rank = WardrobeData.Ranks.Count - 1;
            if (Rank < MinRank) Rank = MinRank;
            while (Rank > MinRank && !CanRank(Rank)) Rank--;
        }

        internal void Minimum()
        {
            for (int i = 0; i < 7; i++) Dist[i] = 0;
            Lift();
            Short = Spent > Points(Level);
            if (Rank >= WardrobeData.Ranks.Count) Rank = WardrobeData.Ranks.Count - 1;
            if (Rank < MinRank) Rank = MinRank;
            while (Rank > MinRank && !CanRank(Rank)) Rank--;
        }

        private void Lift()
        {
            var race = Race;
            for (int i = 0; i < 7; i++)
            {
                int need = Floor(i) - race.Base[i];
                if (Dist[i] < need) Dist[i] = need;
            }
        }
    }
}
