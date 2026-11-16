using Apl.Client.Match;
using UnityEngine;

namespace Apl.Client.Modes
{
    /// <summary>
    /// On a batter's body (Locker.Dress): puts the user's bat design on his bat, or
    /// the plain bat back on anyone else's. The rig makes its bat in its own Awake,
    /// which can come after the kit, so this also looks once more at Start.
    /// </summary>
    public sealed class LockerBat : MonoBehaviour
    {
        public bool Mine;
        private Material? _plain;

        private void Start() => Apply();

        public void Apply()
        {
            var rig = GetComponent<BatterRig>();
            if (rig == null || rig.Bat == null) return;
            var blade = rig.Bat.Find("Blade")?.GetComponent<MeshRenderer>();
            if (blade == null) return;
            if (_plain == null) _plain = blade.sharedMaterial;
            var mat = Mine && _plain != null ? Locker.BatMaterial(_plain) : null;
            blade.sharedMaterial = mat ?? _plain;
            if (Mine && mat != null) Debug.Log($"[APL locker] {name} bat in {Progression.Equipped(Slot.Bat).Name}");
        }
    }
}
