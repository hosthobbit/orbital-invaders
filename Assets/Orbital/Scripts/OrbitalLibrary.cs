using UnityEngine;

namespace Orbital
{
    /// <summary>Everything the editor builder generates, loaded at runtime from Resources.</summary>
    public class OrbitalLibrary : ScriptableObject
    {
        [Header("Invaders: [kind * 2 + frame]")]
        public Mesh[] invaderMeshes = new Mesh[6];
        public Mesh ufoMesh;
        public Mesh bossMesh;
        public Mesh bossTurretMesh;
        public Mesh playerMesh;
        public Mesh cube;
        public Mesh sphere;
        public Mesh quad;
        public Mesh capsule;

        [Header("Materials")]
        public Material voxel;
        public Material debris;
        public Material bunker;
        public Material playerHull;
        public Material bossHull;
        public Material playerShot;
        public Material shotTrail;
        public Material enemyShot;
        public Material fire;
        public Material ember;
        public Material spark;
        public Material ring;
        public Material smoke;
        public Material streak;
        public Material engine;
        public Material shield;
        public Material powerup;
        public Material reticle;
        public Material dust;

        [Header("Fonts")]
        public Font hudFont;
        public Font titleFont;

        static OrbitalLibrary instance;
        public static OrbitalLibrary I
        {
            get
            {
                if (instance == null) instance = Resources.Load<OrbitalLibrary>("OrbitalLibrary");
                return instance;
            }
        }
    }
}
