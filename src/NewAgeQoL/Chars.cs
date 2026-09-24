using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Chars
    {
        private abstract class Slot
        {
            internal abstract void Move(ConfigFile file, bool fresh, bool all);
        }

        private sealed class Slot<T> : Slot
        {
            internal string Section;
            internal string Key;
            internal string Note;
            internal T Blank;
            internal Func<T, T> Carry;
            internal Action<ConfigEntry<T>> Put;
            internal ConfigEntry<T> Now;
            internal ConfigEntry<T> Base;

            internal override void Move(ConfigFile file, bool fresh, bool all)
            {
                T had = Now != null ? Now.Value : Blank;
                var made = file.Bind(Section, Key, Blank, Note);
                if (fresh)
                {
                    if (all && Base != null) made.Value = Base.Value;
                    else if (!all && Carry != null) made.Value = Carry(had);
                }
                Now = made;
                Put(made);
            }
        }

        private sealed class Mark : Slot
        {
            internal string Section;
            internal string Key;
            internal string Note;
            internal Action Act;

            internal override void Move(ConfigFile file, bool fresh, bool all)
            {
                var done = file.Bind(Section, Key, false, Note);
                if (done.Value) return;
                if (!fresh) Act();
                done.Value = true;
            }
        }

        private static readonly List<Slot> Slots = new List<Slot>();
        private static ConfigFile _home;
        private static ConfigFile _file;
        private static EventHandler<SettingChangedEventArgs> _watch;
        private static int _who;
        private static int _legacy;
        private static float _lookAt;

        internal static int Who => _who;

        internal static void Home(ConfigFile file)
        {
            _home = file;
        }

        internal static void Watch(EventHandler<SettingChangedEventArgs> watch)
        {
            if (watch == null) return;
            _watch = watch;
            if (_home != null) _home.SettingChanged += watch;
            if (_file != null) _file.SettingChanged += watch;
        }

        internal static void Legacy(int userId)
        {
            _legacy = userId;
        }

        internal static ConfigEntry<T> Own<T>(string section, string key, T blank, string note, Action<ConfigEntry<T>> put)
        {
            return Keep(section, key, blank, note, put, value => value);
        }

        internal static ConfigEntry<T> Fresh<T>(string section, string key, T blank, string note, Action<ConfigEntry<T>> put)
        {
            return Keep(section, key, blank, note, put, null);
        }

        internal static ConfigEntry<T> Own<T>(string section, string key, T blank, string note, Action<ConfigEntry<T>> put, Func<T, T> carry)
        {
            return Keep(section, key, blank, note, put, carry);
        }

        private static ConfigEntry<T> Keep<T>(string section, string key, T blank, string note, Action<ConfigEntry<T>> put, Func<T, T> carry)
        {
            var slot = new Slot<T> { Section = section, Key = key, Note = note, Blank = blank, Put = put, Carry = carry };
            Slots.Add(slot);
            slot.Move(_home, false, false);
            slot.Base = slot.Now;
            return slot.Now;
        }

        internal static void Once(string section, string key, string note, Action act)
        {
            var mark = new Mark { Section = section, Key = key, Note = note, Act = act };
            Slots.Add(mark);
            mark.Move(_home, false, false);
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _lookAt) return;
            _lookAt = Time.unscaledTime + 1f;
            if (!SideButtons.InWorld()) return;
            int id = Playing();
            if (id > 0) Use(id);
        }

        private static int Playing()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null ? info.UserId : 0;
            }
            catch { return 0; }
        }

        internal static void Use(int userId)
        {
            if (userId <= 0 || userId == _who || _home == null) return;
            try
            {
                string folder = Path.Combine(BepInEx.Paths.ConfigPath, "newage.qol");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, userId + ".cfg");
                bool fresh = !File.Exists(path);
                bool all = fresh && userId == _legacy;
                if (all) _legacy = 0;
                var file = new ConfigFile(path, true);
                foreach (var slot in Slots) slot.Move(file, fresh, all);
                file.Save();
                if (_watch != null)
                {
                    if (_file != null) _file.SettingChanged -= _watch;
                    file.SettingChanged += _watch;
                }
                _file = file;
                _who = userId;
                Forget();
                Plugin.Log?.LogInfo("[персонаж] настройки " + (all ? "переехали в свой файл" : fresh ? "заведены" : "подхвачены") + ": " + userId + ".cfg");
            }
            catch (Exception e) { Plugin.Fault("[персонаж] настройки: " + e); }
        }

        private static void Forget()
        {
            try { Settings.Close(); } catch { }
            try { Hotkeys.Forget(); } catch { }
            try { Storage.Forget(); } catch { }
            try { MoveMode.Forget(); } catch { }
            try { Manikin.Forget(); } catch { }
            try { MapLabels.Refresh(); } catch { }
        }
    }
}
