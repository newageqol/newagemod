using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Sounds
    {
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static AudioSource _own;
        private static bool _wasCombat;
        private static RoundType _phase = RoundType.UNKNOWN_ROUND;
        private static float _at;
        private static float _fightAt;
        private static float _sampleAt;

        private static float Volume =>
            Plugin.CfgSoundVolume == null ? 1f : Mathf.Clamp01(Plugin.CfgSoundVolume.Value);

        internal static bool Active => Volume > 0.001f;

        private static object _chatOn;
        private static string _lastKey = "";
        private static float _lastAt;

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _chatOn = null; return; }
            if (ReferenceEquals(_chatOn, nc)) return;
            nc.RemoveMessageListener(4, OnChat);
            nc.AddMessageListener(4, OnChat);
            _chatOn = nc;
        }

        private static void OnChat(object m)
        {
            try
            {
                var msg = m as Transport.Messages.Responses.Chat.ChatResponseMessage;
                if (msg == null) return;
                bool team = msg.Type == 9;
                if (msg.Type != 2 && !team) return;
                if (string.IsNullOrEmpty(msg.Sender) || string.IsNullOrEmpty(msg.Text)) return;
                try { if (GameSettings.Instance != null && GameSettings.Instance.IsIgnored(msg.Sender)) return; } catch { }

                int me = 0;
                try { me = Controllers.User.UserInfo.UserId; } catch { }
                bool mine = me > 0 && msg.SenderId == me;
                bool toMe = team ? !mine
                    : (msg.ReceiverId.HasValue ? (me <= 0 || msg.ReceiverId.Value == me) : (me <= 0 || !mine));
                if (!mine && !toMe) return;

                string key = msg.Type + "|" + msg.SenderId + "|" + msg.Text;
                if (key == _lastKey && Time.unscaledTime - _lastAt < 1.5f) return;
                _lastKey = key;
                _lastAt = Time.unscaledTime;

                if (team) Team(); else Pm();
            }
            catch (Exception e) { Plugin.Trace("[звук] чат: " + e.Message); }
        }

        internal static void Tick()
        {
            try
            {
                Listen();
                if (Time.unscaledTime < _at) return;
                _at = Time.unscaledTime + 0.1f;
                bool combat = SideButtons.InCombat();
                if (combat && !_wasCombat)
                {
                    _phase = RoundType.UNKNOWN_ROUND;
                    _fightAt = Time.unscaledTime;
                    Play("fight_start");
                }
                _wasCombat = combat;
                if (!combat) { _phase = RoundType.UNKNOWN_ROUND; return; }
                var cd = FighterHint.Cd();
                if (cd == null) return;
                var t = cd.RoundType;
                if (t == _phase) return;
                bool fightEnded = (_phase == RoundType.COMBAT_ROUND || _phase == RoundType.CALCULATING_COMBAT) && t == RoundType.WALK_ROUND;
                _phase = t;
                if (fightEnded && Time.unscaledTime - _fightAt > 2f) Play("round_end");
            }
            catch (Exception e) { Plugin.Trace("[звук] " + e.Message); }
        }

        internal static void Pm()
        {
            Play("pm");
        }

        internal static void Sample()
        {
            if (Time.unscaledTime < _sampleAt) return;
            _sampleAt = Time.unscaledTime + 0.25f;
            Play("pm");
        }

        internal static void Team()
        {
            Play("pm");
        }

        private static void Play(string name)
        {
            Fire(Clip(name));
        }

        private static void Fire(AudioClip clip)
        {
            try
            {
                if (clip == null) return;
                float volume = Volume;
                if (volume <= 0.001f) return;
                if (_own == null)
                {
                    var go = new GameObject("QoLSounds", typeof(AudioSource));
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _own = go.GetComponent<AudioSource>();
                    _own.playOnAwake = false;
                    _own.spatialBlend = 0f;
                    _own.bypassListenerEffects = true;
                }
                _own.volume = 1f;
                _own.PlayOneShot(clip, volume);
            }
            catch (Exception e) { Plugin.Trace("[звук] " + e.Message); }
        }

        private static AudioClip Clip(string name)
        {
            AudioClip clip;
            if (Clips.TryGetValue(name, out clip)) return clip;
            clip = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string res = null;
                foreach (var r in asm.GetManifestResourceNames())
                    if (r.EndsWith("." + name + ".wav", StringComparison.OrdinalIgnoreCase)) { res = r; break; }
                if (res == null) { Plugin.Warn("[звук] ресурс " + name + " не найден в dll"); Clips[name] = null; return null; }
                using (var s = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    clip = FromWav(name, ms.ToArray());
                }
            }
            catch (Exception e) { Plugin.Warn("[звук] " + name + ": " + e.Message); }
            Clips[name] = clip;
            return clip;
        }

        private static AudioClip FromWav(string name, byte[] wav)
        {
            if (wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') throw new Exception("не WAV");
            int p = 12;
            int channels = 1, rate = 22050, bits = 16;
            int dataAt = -1, dataLen = 0;
            while (p + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, p, 4);
                int len = BitConverter.ToInt32(wav, p + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(wav, p + 10);
                    rate = BitConverter.ToInt32(wav, p + 12);
                    bits = BitConverter.ToInt16(wav, p + 22);
                }
                else if (id == "data") { dataAt = p + 8; dataLen = Math.Min(len, wav.Length - dataAt); break; }
                p += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16) throw new Exception("нужен PCM16");
            int count = dataLen / 2;
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = BitConverter.ToInt16(wav, dataAt + i * 2) / 32768f;
            var clip = AudioClip.Create("QoL_" + name, count / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    [HarmonyPatch(typeof(CombatData), "PlayPhasesound")]
    public static class GamePhaseSoundPatch
    {
        private static bool Prefix(CombatData __instance)
        {
            try
            {
                if (!Sounds.Active) return true;
                if (__instance == null) return true;
                return false;
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(AudioManager), "PlayUISound")]
    public static class GamePmSoundPatch
    {
        private static bool Prefix(AudioClip audioClip)
        {
            try
            {
                if (!Sounds.Active) return true;
                if (audioClip == null) return true;
                var holder = VisualPrefabsHolder.Instance;
                if (holder != null && holder.PrivateMessageSound != null && audioClip == holder.PrivateMessageSound) return false;
            }
            catch { }
            return true;
        }
    }
}
