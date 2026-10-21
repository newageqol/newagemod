using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NewAge2D
{
    public sealed class ArenaOcclude : MonoBehaviour
    {
        sealed class Block
        {
            public Renderer[] Renderers;
            public Bounds Bounds;
            public bool Hidden;
            public bool Hit;
        }

        readonly List<Block> _blocks = new List<Block>();
        readonly Dictionary<Collider, Block> _owner = new Dictionary<Collider, Block>();
        readonly List<(BoxCollider box, Block block)> _boxes = new List<(BoxCollider, Block)>();
        readonly RaycastHit[] _hits = new RaycastHit[64];
        readonly Vector3[] _points = new Vector3[5];
        Transform _root;
        Camera _eye;
        Scene _scene;
        PhysicsScene _physics;
        float _next;

        public void Init(Transform root, Camera eye, ArenaCfg cfg)
        {
            _root = root;
            _eye = eye;
            _scene = SceneManager.CreateScene("arena3d_occlude_" + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            int meshes = 0, boxes = 0;
            foreach (var group in new[] { "Castle", "Skyline" })
            {
                var g = root.Find(group);
                if (g == null) continue;
                foreach (Transform child in g)
                {
                    var rs = child.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var block = new Block { Renderers = rs };
                    bool any = false;
                    var b = new Bounds();
                    foreach (var r in rs)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var mesh = mf.sharedMesh;
                        var mb = mesh.bounds;
                        var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                        for (int k = 0; k < 8; k++)
                        {
                            var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                            var lp = m.MultiplyPoint3x4(corner);
                            if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                            else b.Encapsulate(lp);
                        }
                        var go = new GameObject(r.name);
                        SceneManager.MoveGameObjectToScene(go, _scene);
                        Place(go.transform, m);
                        Collider c;
                        if (mesh.isReadable)
                        {
                            var mc = go.AddComponent<MeshCollider>();
                            mc.sharedMesh = mesh;
                            c = mc;
                            meshes++;
                        }
                        else
                        {
                            var bc = go.AddComponent<BoxCollider>();
                            bc.center = mb.center;
                            bc.size = mb.size;
                            c = bc;
                            _boxes.Add((bc, block));
                            boxes++;
                        }
                        _owner[c] = block;
                    }
                    if (!any) continue;
                    block.Bounds = b;
                    _blocks.Add(block);
                }
            }
            Physics.SyncTransforms();
            Plugin.Log.LogInfo("[arena3d] occluders " + _blocks.Count + " mesh " + meshes + " box " + boxes);
            float x0 = cfg.cells[0], x1 = cfg.cells[1], z0 = cfg.cells[2], z1 = cfg.cells[3];
            float y = 1f;
            _points[0] = new Vector3((x0 + x1) / 2f, y, -(z0 + z1) / 2f);
            _points[1] = new Vector3(x0, y, -z0);
            _points[2] = new Vector3(x1, y, -z0);
            _points[3] = new Vector3(x0, y, -z1);
            _points[4] = new Vector3(x1, y, -z1);
        }

        static void Place(Transform t, Matrix4x4 m)
        {
            var s = new Vector3(((Vector3)m.GetColumn(0)).magnitude, ((Vector3)m.GetColumn(1)).magnitude, ((Vector3)m.GetColumn(2)).magnitude);
            if (m.determinant < 0f) s.x = -s.x;
            t.position = m.GetColumn(3);
            t.rotation = Quaternion.LookRotation((Vector3)m.GetColumn(2) / s.z, (Vector3)m.GetColumn(1) / s.y);
            t.localScale = s;
        }

        void LateUpdate()
        {
            if (_eye == null || _root == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.1f;
            var from = _root.InverseTransformPoint(_eye.transform.position);
            foreach (var b in _blocks) b.Hit = false;
            foreach (var (box, block) in _boxes)
            {
                var d = box.transform.InverseTransformPoint(from) - box.center;
                var e = box.size / 2f;
                if (Mathf.Abs(d.x) <= Mathf.Abs(e.x) && Mathf.Abs(d.y) <= Mathf.Abs(e.y) && Mathf.Abs(d.z) <= Mathf.Abs(e.z)) block.Hit = true;
            }
            foreach (var lp in _points)
            {
                var dir = lp - from;
                float dist = dir.magnitude;
                if (dist <= 1f) continue;
                int n = _physics.Raycast(from, dir / dist, _hits, dist - 1f);
                for (int i = 0; i < n; i++)
                    if (_owner.TryGetValue(_hits[i].collider, out var hb)) hb.Hit = true;
            }
            foreach (var b in _blocks)
            {
                if (b.Hit == b.Hidden) continue;
                b.Hidden = b.Hit;
                Plugin.Log.LogInfo("[arena3d] " + (b.Hit ? "hidden " : "shown ") + b.Renderers[0].name + " box " + b.Bounds.center.ToString("F1") + " size " + b.Bounds.size.ToString("F1") + " camera " + from.ToString("F1") + (b.Bounds.Contains(from) ? " inside" : ""));
                foreach (var r in b.Renderers) if (r != null) r.enabled = !b.Hit;
            }
        }

        void OnDestroy()
        {
            foreach (var b in _blocks)
                if (b.Hidden) foreach (var r in b.Renderers) if (r != null) r.enabled = true;
            if (_scene.IsValid() && _scene.isLoaded) SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
