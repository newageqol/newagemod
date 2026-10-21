using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D
{
    public sealed class ArenaCrowd : MonoBehaviour
    {
        const int Frames = 12;

        internal sealed class Kind
        {
            public Mesh[][] Baked;
            public float Length;
            public float[] Scale;
        }

        public sealed class Fan
        {
            public GameObject Body;
            public Animation Anim;
            public float Excite;
            public float BusyUntil;
            public bool Live;
            internal Kind Kind;
            internal Matrix4x4[] Matrices;
            internal Material[][] Materials;
            internal int Phase;
        }

        public readonly List<Fan> Fans = new List<Fan>();
        public readonly List<string> Acts = new List<string>();
        readonly Dictionary<(Mesh, int, Material), List<Matrix4x4>> _groups = new Dictionary<(Mesh, int, Material), List<Matrix4x4>>();
        readonly List<Mesh> _owned = new List<Mesh>();
        int _layer;
        Transform _crowd;

        public void Init(Transform crowd)
        {
            if (crowd == null) return;
            _layer = crowd.gameObject.layer;
            _crowd = crowd;
            var kinds = new Dictionary<string, Kind>();
            var rnd = new System.Random(5);
            foreach (var anim in crowd.GetComponentsInChildren<Animation>(true))
            {
                var smrs = anim.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0) continue;
                string key = smrs[0].sharedMesh != null ? smrs[0].sharedMesh.name + "_" + smrs[0].sharedMesh.vertexCount : anim.name;
                AnimationState idle = null;
                foreach (AnimationState st in anim) if (st.name.Contains("Idle")) idle = st;
                if (!kinds.TryGetValue(key, out var kind))
                {
                    kind = new Kind { Baked = new Mesh[Frames][], Length = idle != null ? idle.length : 1f, Scale = new float[smrs.Length] };
                    for (int s = 0; s < smrs.Length; s++) kind.Scale[s] = smrs[s].transform.lossyScale.x;
                    for (int k = 0; k < Frames; k++)
                    {
                        if (idle != null) idle.clip.SampleAnimation(anim.gameObject, kind.Length * k / Frames);
                        kind.Baked[k] = new Mesh[smrs.Length];
                        for (int s = 0; s < smrs.Length; s++)
                        {
                            var m = new Mesh { name = key + "_" + k + "_" + s };
                            smrs[s].BakeMesh(m);
                            kind.Baked[k][s] = m;
                            _owned.Add(m);
                        }
                    }
                    kinds[key] = kind;
                }
                if (Acts.Count == 0)
                    foreach (AnimationState st in anim) if (!st.name.Contains("Idle")) Acts.Add(st.name);
                float excite = 0.5f;
                int cut = anim.name.LastIndexOf('_');
                if (cut > 0) float.TryParse(anim.name.Substring(cut + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out excite);
                var fan = new Fan
                {
                    Body = anim.gameObject, Anim = anim, Excite = excite, Kind = kind, Phase = rnd.Next(Frames),
                    Matrices = new Matrix4x4[smrs.Length], Materials = new Material[smrs.Length][],
                };
                for (int s = 0; s < smrs.Length; s++)
                {
                    fan.Matrices[s] = Matrix4x4.TRS(smrs[s].transform.position, smrs[s].transform.rotation, Vector3.one * (s < kind.Scale.Length && kind.Scale[s] != 0f ? smrs[s].transform.lossyScale.x / kind.Scale[s] : 1f));
                    fan.Materials[s] = smrs[s].sharedMaterials;
                    foreach (var mat in fan.Materials[s]) if (mat != null) mat.enableInstancing = true;
                }
                anim.gameObject.SetActive(false);
                Fans.Add(fan);
            }
        }

        public bool Wake(Fan fan, string act, float delay)
        {
            if (fan.Anim == null || fan.Anim[act] == null) return false;
            StartCoroutine(Play(fan, act, delay));
            return true;
        }

        System.Collections.IEnumerator Play(Fan fan, string act, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (fan.Body == null) yield break;
            fan.Live = true;
            fan.Body.SetActive(true);
            string idle = null;
            foreach (AnimationState st in fan.Anim) if (st.name.Contains("Idle")) idle = st.name;
            if (idle != null) { fan.Anim.Play(idle); fan.Anim[idle].time = fan.Kind.Length * fan.Phase / Frames; }
            fan.Anim.CrossFade(act, 0.25f);
            float len = fan.Anim[act].length;
            yield return new WaitForSeconds(len);
            if (idle != null) fan.Anim.CrossFade(idle, 0.3f);
            yield return new WaitForSeconds(0.35f);
            if (fan.Body == null) yield break;
            fan.Body.SetActive(false);
            fan.Live = false;
        }

        void LateUpdate()
        {
            foreach (var list in _groups.Values) list.Clear();
            if (_crowd == null || !_crowd.gameObject.activeInHierarchy) return;
            float t = Time.time;
            foreach (var fan in Fans)
            {
                if (fan.Live) continue;
                int k = (fan.Phase + (int)(t / fan.Kind.Length * Frames)) % Frames;
                var baked = fan.Kind.Baked[k];
                for (int s = 0; s < baked.Length; s++)
                {
                    var mats = fan.Materials[s];
                    for (int sub = 0; sub < mats.Length && sub < baked[s].subMeshCount; sub++)
                    {
                        if (mats[sub] == null) continue;
                        var key = (baked[s], sub, mats[sub]);
                        if (!_groups.TryGetValue(key, out var list)) _groups[key] = list = new List<Matrix4x4>();
                        list.Add(fan.Matrices[s]);
                    }
                }
            }
            var chunk = new List<Matrix4x4>(1023);
            foreach (var kv in _groups)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i += 1023)
                {
                    chunk.Clear();
                    for (int j = i; j < Mathf.Min(list.Count, i + 1023); j++) chunk.Add(list[j]);
                    Graphics.DrawMeshInstanced(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, chunk, null, ShadowCastingMode.Off, true, _layer);
                }
            }
        }

        void OnDestroy()
        {
            foreach (var m in _owned) if (m != null) Destroy(m);
            _owned.Clear();
        }
    }
}
