using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    /// <summary>A glowing capsule that drifts toward the player's plane.</summary>
    public class PowerUp : MonoBehaviour
    {
        public PowerKind Kind;
        Transform core;
        MaterialPropertyBlock block;
        MeshRenderer ring;
        float age;

        public static readonly Color[] Colours =
        {
            new Color(1f, 0.55f, 0.15f),  // spread
            new Color(1f, 0.9f, 0.2f),    // rapid
            new Color(0.2f, 1f, 1f),      // pierce
            new Color(0.3f, 0.55f, 1f),   // shield
            new Color(1f, 0.2f, 0.25f),   // bomb
        };
        public static readonly string[] Letters = { "S", "R", "P", "H", "B" };
        public static readonly string[] Names = { "SPREAD SHOT", "RAPID FIRE", "PIERCING LASER", "SHIELD", "SMART BOMB" };

        public Color Colour => Colours[(int)Kind];

        public static PowerUp Create(PowerKind kind, Vector3 at)
        {
            var lib = OrbitalLibrary.I;
            var go = new GameObject("PowerUp " + kind);
            go.transform.position = at;
            var p = go.AddComponent<PowerUp>();
            p.Kind = kind;
            p.block = new MaterialPropertyBlock();

            p.core = new GameObject("Core").transform;
            p.core.SetParent(go.transform, false);
            p.core.localScale = Vector3.one * 0.45f;
            p.core.gameObject.AddComponent<MeshFilter>().sharedMesh = lib.cube;
            var cr = p.core.gameObject.AddComponent<MeshRenderer>();
            cr.sharedMaterial = lib.debris;
            var cb = new MaterialPropertyBlock();
            Color c = p.Colour; c.a = 1f;
            cb.SetColor("_BaseColor", c);
            cb.SetFloat("_EmissionStrength", 0f);
            cb.SetColor("_RimColor", c * 3f);
            cb.SetFloat("_Rim", 2f);
            cb.SetColor("_FlashColor", c * 1.5f);
            cb.SetFloat("_Flash", 1f);
            cr.SetPropertyBlock(cb);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(go.transform, false);
            halo.transform.localScale = Vector3.one * 1.3f;
            halo.AddComponent<MeshFilter>().sharedMesh = lib.sphere;
            p.ring = halo.AddComponent<MeshRenderer>();
            p.ring.sharedMaterial = lib.powerup;
            p.ring.shadowCastingMode = ShadowCastingMode.Off;

            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = p.Colour; l.range = 3.5f; l.intensity = 5f; l.shadows = LightShadows.None;
            return p;
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.position += Vector3.back * 9f * Time.deltaTime;
            core.localRotation = Quaternion.Euler(age * 90f, age * 140f, age * 60f);
            block.SetColor("_Color", Colour * (1.2f + 0.6f * Mathf.Sin(age * 10f)));
            ring.SetPropertyBlock(block);
            if (transform.position.z < -8f) Destroy(gameObject);
        }
    }
}
