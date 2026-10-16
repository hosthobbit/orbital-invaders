using UnityEngine;
using UnityEngine.Rendering;

namespace Orbital
{
    /// <summary>Keeps the backdrop alive: Earth spin, moon, sun glare billboards, streaming space dust.</summary>
    public class SpaceScene : MonoBehaviour
    {
        public Transform Earth;
        public Transform Clouds;
        public Transform Moon;
        public Transform[] Billboards;
        public Color AmbientSky = new Color(0.06f, 0.08f, 0.14f);
        public Color AmbientGround = new Color(0.05f, 0.09f, 0.16f);

        void Start()
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(AmbientSky * 0.6f);
            sh.AddDirectionalLight(Vector3.down, AmbientGround * 1.5f, 1f); // earthshine from below
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = false;
            BuildDust();
        }

        void BuildDust()
        {
            var lib = OrbitalLibrary.I;
            var go = new GameObject("SpaceDust");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0, 1, 70);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.75f, 1f, 0.7f), new Color(1f, 0.85f, 0.7f, 0.5f));
            main.maxParticles = 700;
            var em = ps.emission; em.rateOverTime = 110;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(44, 24, 4);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-16f, -11f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = lib.dust;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 2f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        void LateUpdate()
        {
            float t = Time.time;
            if (Earth) Earth.rotation = Quaternion.Euler(18f, -t * 0.6f, 8f);
            if (Clouds) Clouds.rotation = Quaternion.Euler(18f, -t * 0.75f, 8f);
            if (Moon) Moon.rotation = Quaternion.Euler(0, t * 0.5f, 0);
            var cam = CameraRig.I ? CameraRig.I.transform : null;
            if (cam && Billboards != null)
                foreach (var b in Billboards)
                    if (b) b.rotation = Quaternion.LookRotation(b.position - cam.position) * Quaternion.Euler(90, 0, 0);
        }
    }
}
