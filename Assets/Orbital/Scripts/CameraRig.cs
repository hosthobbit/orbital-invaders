using UnityEngine;

namespace Orbital
{
    public enum CamMode { Title, Follow, Orbit }

    /// <summary>Chase camera behind the player's ship, with title drift, death orbit and shake.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam;
        public CamMode Mode = CamMode.Title;
        public Transform Target;
        public Vector3 OrbitCentre;

        float trauma;
        float orbitAngle;
        Vector3 pos, look;
        float roll;

        void Awake()
        {
            I = this;
            if (!Cam) Cam = GetComponent<Camera>();
            pos = transform.position;
            look = pos + transform.forward * 10f;
        }

        public void AddTrauma(float t) => trauma = Mathf.Clamp01(trauma + t);

        public void StartOrbit(Vector3 centre)
        {
            OrbitCentre = centre;
            orbitAngle = 0f;
            Mode = CamMode.Orbit;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 wantPos, wantLook;
            float wantRoll = 0f;
            float sharp = 4f;
            switch (Mode)
            {
                case CamMode.Title:
                {
                    float t = Time.unscaledTime;
                    wantPos = new Vector3(Mathf.Sin(t * 0.13f) * 3f, 2.5f + Mathf.Sin(t * 0.21f) * 0.8f, -12f);
                    wantLook = new Vector3(Mathf.Sin(t * 0.09f) * 2f, 2.2f, 30f);
                    sharp = 1.5f;
                    break;
                }
                case CamMode.Orbit:
                {
                    orbitAngle += dt * 0.5f;
                    Vector3 off = Quaternion.Euler(0, Mathf.Rad2Deg * orbitAngle - 150f, 0) * new Vector3(0, 0, -11f);
                    wantPos = OrbitCentre + off + Vector3.up * 4f;
                    wantLook = OrbitCentre;
                    sharp = 2f;
                    break;
                }
                default:
                {
                    Vector3 p = Target ? Target.position : Vector3.zero;
                    // high and behind: the ship sits low on screen and its missiles climb visibly up to the wall
                    wantPos = new Vector3(p.x * 0.6f, p.y * 0.6f + 7.5f, -13f);
                    wantLook = new Vector3(p.x * 0.35f, p.y * 0.5f + 0.5f, 22f);
                    if (Target) wantRoll = -Target.GetComponent<Player>().Bank * 0.25f;
                    sharp = 5f;
                    break;
                }
            }

            float k = 1f - Mathf.Exp(-dt * sharp);
            pos = Vector3.Lerp(pos, wantPos, k);
            look = Vector3.Lerp(look, wantLook, k);
            roll = Mathf.Lerp(roll, wantRoll, k);

            Quaternion rot = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0, 0, roll);
            Vector3 p2 = pos;
            trauma = Mathf.Max(0f, trauma - dt * 1.3f);
            float shake = trauma * trauma;
            if (shake > 0f)
            {
                float t = Time.unscaledTime * 25f;
                p2 += rot * new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0) * shake * 0.9f;
                rot *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 5.5f) - 0.5f) * shake * 8f);
            }
            transform.SetPositionAndRotation(p2, rot);
        }
    }
}
