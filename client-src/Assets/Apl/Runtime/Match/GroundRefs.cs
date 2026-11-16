using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// What the ground builder put in the scene, for the match director: the
    /// people on the field, both sets of stumps, the camera.
    /// </summary>
    public sealed class GroundRefs : MonoBehaviour
    {
        public Animator? Striker, NonStriker, Bowler, Keeper, Umpire;
        public Animator[] Fielders = System.Array.Empty<Animator>();

        /// <summary>The batting end's stumps (left, middle, right from the bowler) and bails.</summary>
        public Transform[] Stumps = System.Array.Empty<Transform>();
        public Transform[] Bails = System.Array.Empty<Transform>();

        public Camera Camera = null!;

        /// <summary>
        /// Materials the stadium builder copies at runtime (plain, glowing,
        /// cut-out), so their shader variants ship, and the APL cover for the
        /// boards and screens.
        /// </summary>
        public Material LitTemplate = null!, GlowTemplate = null!, CutoutTemplate = null!;
        public Texture2D? Cover;

        /// <summary>
        /// The cricketers' bodies (Rocketbox), each with its kit mask and skin
        /// tone (1–6), the kit painter and the cricket animator controller.
        /// </summary>
        public GameObject[] CastModels = System.Array.Empty<GameObject>();
        public Texture2D[] CastKits = System.Array.Empty<Texture2D>();
        public int[] CastTones = System.Array.Empty<int>();
        public Material? KitBlit;

        /// <summary>Cheerleaders' bodies and kit masks (KitBuilder.Cheer) — never players.</summary>
        public GameObject[] CheerModels = System.Array.Empty<GameObject>();
        public Texture2D[] CheerKits = System.Array.Empty<Texture2D>();
        public RuntimeAnimatorController? Cricket;
    }

    /// <summary>
    /// Engine coordinates (metres from the batter's stumps: x across, + towards
    /// his off side; z up the pitch) to the ground: the pitch runs along world z,
    /// batting end at -10.06, the broadcast camera behind the bowler at +z.
    /// From behind the bowler a right-hander's off side is screen-left (+x).
    /// </summary>
    public static class GroundSpace
    {
        public const float HalfPitch = 10.06f;

        public static Vector3 Ball(double x, double y, double z, bool leftHanded) =>
            new Vector3((float)(leftHanded ? -x : x), (float)y, -HalfPitch + (float)z);

        /// <summary>A ground position given as (x across, f forward) from the batter.</summary>
        public static Vector3 Field(double x, double f, bool leftHanded, float y = 0) =>
            new Vector3((float)(leftHanded ? -x : x), y, -HalfPitch + (float)f);

        /// <summary>Direction off the bat: angle from straight (+ off side), as a ground vector.</summary>
        public static Vector3 Direction(double angleDeg, bool leftHanded)
        {
            var a = angleDeg * Mathf.Deg2Rad;
            return new Vector3((float)(System.Math.Sin(a) * (leftHanded ? -1 : 1)), 0, (float)System.Math.Cos(a));
        }
    }
}
