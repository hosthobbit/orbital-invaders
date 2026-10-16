using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    public enum InvaderKind { Squid = 0, Crab = 1, Octopus = 2 }
    public enum DiveState { None, Swoop, Return }

    /// <summary>One voxel invader in the formation.</summary>
    public class Invader : MonoBehaviour
    {
        public const float VoxelSize = 0.2f;
        public InvaderKind Kind;
        public Vector3 Slot;          // position relative to the formation root
        public bool Alive = true;
        public int Points;
        public float HalfWidth = 0.75f, HalfHeight = 0.55f;

        // Galaga-style dive: a Bezier swoop past the player, then a warp back into the slot
        public DiveState Dive;
        public bool Diving => Dive != DiveState.None;
        public Vector3 DiveVelocity { get; private set; }
        Vector3 p0, p1, p2, p3;
        float diveT, diveDur, diveSide;
        bool diveShot;
        TrailRenderer trail;

        MeshFilter filter;
        MeshRenderer rend;
        MaterialPropertyBlock block;
        Mesh frameA, frameB;
        public float Flash;
        float shownFlash = -1f;
        float bobSeed;
        public Mesh CurrentMesh => filter.sharedMesh;

        public static Invader Create(InvaderKind kind, Vector3 slot, Transform parent)
        {
            var lib = OrbitalLibrary.I;
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            var inv = go.AddComponent<Invader>();
            inv.Kind = kind;
            inv.Slot = slot;
            inv.Points = kind == InvaderKind.Squid ? 30 : kind == InvaderKind.Crab ? 20 : 10;
            inv.frameA = lib.invaderMeshes[(int)kind * 2];
            inv.frameB = lib.invaderMeshes[(int)kind * 2 + 1];
            inv.filter = go.AddComponent<MeshFilter>();
            inv.filter.sharedMesh = inv.frameA;
            inv.rend = go.AddComponent<MeshRenderer>();
            inv.rend.sharedMaterial = lib.voxel;
            inv.rend.shadowCastingMode = ShadowCastingMode.On;
            inv.block = new MaterialPropertyBlock();
            inv.bobSeed = Random.value * 10f;
            var b = inv.frameA.bounds;
            inv.HalfWidth = b.extents.x + 0.05f;
            inv.HalfHeight = b.extents.y + 0.05f;
            go.transform.localPosition = slot;
            return inv;
        }

        public void SetFrame(int f) => filter.sharedMesh = f == 0 ? frameA : frameB;

        public Color Colour => Kind == InvaderKind.Squid ? new Color(1f, 0.3f, 0.9f) : Kind == InvaderKind.Crab ? new Color(0.3f, 0.9f, 1f) : new Color(0.4f, 1f, 0.5f);

        /// <summary>Peel off and swoop through the player's plane at <paramref name="target"/>.</summary>
        public void StartDive(Vector3 target, float duration)
        {
            Dive = DiveState.Swoop;
            diveT = 0f;
            diveDur = duration;
            diveShot = false;
            p0 = transform.position;
            diveSide = p0.x >= target.x ? 1f : -1f;          // loop out away from the player first
            p1 = p0 + new Vector3(diveSide * 5f, 4f, -7f);
            p2 = new Vector3(target.x + diveSide * 3f, target.y + 2.5f, target.z + 12f);
            p3 = new Vector3(target.x - diveSide * 2f, target.y - 0.8f, target.z - 9f);
            SetTrail(true);
        }

        /// <summary>Abort a swoop (e.g. the player died) and head home.</summary>
        public void Recall()
        {
            if (Dive == DiveState.Swoop) BeginReturn();
        }

        void BeginReturn()
        {
            Dive = DiveState.Return;
            diveT = 0f;
            DiveVelocity = Vector3.zero;
            transform.localPosition = Slot + new Vector3(0, 0, 70f);
            transform.localRotation = Quaternion.identity;
            if (trail) trail.Clear();
        }

        void SetTrail(bool on)
        {
            if (!trail)
            {
                trail = gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = OrbitalLibrary.I.streak;
                trail.time = 0.35f;
                trail.widthMultiplier = 0.6f;
                trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
                Color c = Colour;
                trail.startColor = c * 2f;
                trail.endColor = new Color(c.r, c.g, c.b, 0f);
                trail.shadowCastingMode = ShadowCastingMode.Off;
            }
            trail.Clear();
            trail.emitting = on;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        void UpdateDive(float dt)
        {
            if (Dive == DiveState.Swoop)
            {
                diveT += dt / diveDur;
                Vector3 before = transform.position;
                Vector3 pos = Bezier(p0, p1, p2, p3, Mathf.Min(diveT, 1f));
                transform.position = pos;
                if (dt > 0f) DiveVelocity = (pos - before) / dt;
                if (DiveVelocity.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(-DiveVelocity.normalized) * Quaternion.Euler(0, 0, diveSide * diveT * 360f);
                if (!diveShot && diveT > 0.42f)
                {
                    diveShot = true;
                    Formation.I.DiverFire(this);
                }
                if (diveT >= 1f) BeginReturn();
            }
            else
            {
                diveT += dt / 1.1f;
                float e = 1f - Mathf.Pow(1f - Mathf.Min(diveT, 1f), 3f);
                transform.localPosition = Slot + new Vector3(0, 0, 70f * (1f - e));
                if (diveT >= 1f)
                {
                    Dive = DiveState.None;
                    transform.localPosition = Slot;
                    Flash = 1f;
                    if (trail) trail.emitting = false;
                }
            }
        }

        void Update()
        {
            Flash = Mathf.MoveTowards(Flash, 0f, Time.deltaTime * 6f);
            if (Mathf.Abs(Flash - shownFlash) > 0.01f)
            {
                block.SetFloat("_Flash", Flash);
                rend.SetPropertyBlock(block);
                shownFlash = Flash;
            }
            if (Diving) { UpdateDive(Time.deltaTime); return; }
            // a tiny idle wobble so the 3D voxels catch the light
            transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f + bobSeed) * 6f, Mathf.Sin(Time.time * 0.9f + bobSeed * 2f) * 10f, 0);
        }
    }
}
