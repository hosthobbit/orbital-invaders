using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    /// <summary>The mystery saucer that skims across behind the formation.</summary>
    public class Ufo : MonoBehaviour
    {
        public static Ufo I { get; private set; }
        public const float VoxelSize = 0.2f;
        public bool Active { get; private set; }
        public Vector2 Half = new Vector2(1.3f, 0.55f);

        float dir;
        float speed = 6.5f;
        MeshRenderer rend;
        Light glow;
        static readonly int[] Mystery = { 50, 100, 150, 300 };

        void Awake()
        {
            I = this;
            gameObject.AddComponent<MeshFilter>().sharedMesh = OrbitalLibrary.I.ufoMesh;
            rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = OrbitalLibrary.I.voxel;
            rend.shadowCastingMode = ShadowCastingMode.On;
            glow = new GameObject("UfoLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0, -0.8f, 0);
            glow.type = LightType.Point; glow.color = new Color(1f, 0.25f, 0.4f); glow.range = 7f; glow.intensity = 8f; glow.shadows = LightShadows.None;
            var b = OrbitalLibrary.I.ufoMesh.bounds;
            Half = new Vector2(b.extents.x + 0.1f, b.extents.y + 0.1f);
            Hide();
        }

        public void Launch(float z)
        {
            dir = Random.value < 0.5f ? 1f : -1f;
            transform.position = new Vector3(-21f * dir, 6.8f, z);
            Active = true;
            rend.enabled = true;
            glow.enabled = true;
        }

        public void Hide()
        {
            Active = false;
            rend.enabled = false;
            glow.enabled = false;
            SoundBank.I?.SetUfo(false);
        }

        void Update()
        {
            if (!Active) { SoundBank.I.SetUfo(false); return; }
            transform.position += new Vector3(dir * speed * Time.deltaTime, 0, 0);
            transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 3f) * 6f, Time.time * 90f, 0);
            glow.intensity = 6f + 3f * Mathf.Sin(Time.time * 20f);
            SoundBank.I.SetUfo(true, Mathf.Clamp(transform.position.x / 14f, -1f, 1f));
            if (Mathf.Abs(transform.position.x) > 22f) Hide();
        }

        public int ShootDown()
        {
            int pts = Mystery[Random.Range(0, Mystery.Length)];
            Fx.I.VoxelBurst(OrbitalLibrary.I.ufoMesh, transform, VoxelSize, Vector3.zero, 6f, 2.5f);
            Fx.I.Explosion(transform.position, 1.6f, new Color(1f, 0.3f, 0.6f));
            SoundBank.I.Play(Sfx.UfoDie, 0.9f);
            Fx.I.Popup(pts.ToString(), transform.position + Vector3.up, new Color(1f, 0.4f, 0.8f), 1.6f);
            Hide();
            return pts;
        }
    }
}
