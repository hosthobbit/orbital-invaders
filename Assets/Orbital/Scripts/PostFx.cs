using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Orbital
{
    /// <summary>Impact pulses, exposure kicks and slow motion on the global volume.</summary>
    public class PostFx : MonoBehaviour
    {
        public static PostFx I { get; private set; }
        public Volume Volume;

        ChromaticAberration chroma;
        LensDistortion lens;
        Bloom bloom;
        ColorAdjustments colour;
        float baseBloom, baseExposure, baseChroma;
        float pulse, kick;
        float slowTimer, slowScale = 1f;
        public bool Paused;

        void Awake()
        {
            I = this;
            var profile = Volume.profile;
            profile.TryGet(out chroma);
            profile.TryGet(out lens);
            profile.TryGet(out bloom);
            profile.TryGet(out colour);
            if (bloom) baseBloom = bloom.intensity.value;
            if (colour) baseExposure = colour.postExposure.value;
            if (chroma) baseChroma = chroma.intensity.value;
        }

        public void Impact(float s) => pulse = Mathf.Clamp01(Mathf.Max(pulse, s));
        public void Kick(float s) => kick = Mathf.Max(kick, s);
        public void SlowMo(float scale, float seconds) { slowScale = scale; slowTimer = seconds; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.5f);
            kick = Mathf.MoveTowards(kick, 0f, dt * 3f);
            if (Paused) Time.timeScale = 0f;
            else if (slowTimer > 0f)
            {
                slowTimer -= dt;
                Time.timeScale = Mathf.Lerp(Time.timeScale, slowScale, 1f - Mathf.Exp(-dt * 20f));
            }
            else Time.timeScale = Mathf.Lerp(Time.timeScale, 1f, 1f - Mathf.Exp(-dt * 5f));
            Time.fixedDeltaTime = 0.02f * Mathf.Max(Time.timeScale, 0.05f);

            if (chroma) chroma.intensity.value = baseChroma + pulse * 0.8f;
            if (lens) lens.intensity.value = -pulse * 0.3f;
            if (bloom) bloom.intensity.value = baseBloom + pulse * 1.0f + kick * 2f;
            if (colour) colour.postExposure.value = baseExposure + kick * 0.8f;
        }
    }
}
