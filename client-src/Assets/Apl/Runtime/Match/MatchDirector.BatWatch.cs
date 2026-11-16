using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Final rating 2026-11-14 (Running #1, owner "yes this needs fixing"): an orphan bat at the crease with no batter in
    /// frame (P118–P122 lying flat by the bowler's crease after a dive-in; M882–M887 standing on its toe at the keeper's
    /// popping crease). BatterRig's own safety net (a bat over 1.6 m from his hips goes back in his hand) never fired in
    /// either run, so the bat WAS with his hips — it was his body that wasn't drawn. Mid-run the running clip carries his
    /// body in the pose while his transform stays at the crease he left (BatterRig.Reaction's note: "~17 m"), and a
    /// skinned mesh's bounds are kept about its root bone, not the bones as they are — so with the crease he left out of
    /// shot his whole body was culled while the bat (its own object, placed at his hand) was drawn alone.
    ///
    /// The fix: every batter's skinned meshes keep their bounds on the bones as they are (updateWhenOffscreen) — the body
    /// is drawn wherever his hands and bat are. And a watch, for the PM: <c>[APL bat] orphan …</c> if a bat is ever on
    /// screen with its batter's body not drawn, or away from his hips (expected: none).
    /// </summary>
    public sealed partial class MatchDirector
    {
        private readonly Dictionary<BatterRig, SkinnedMeshRenderer[]> _batterSkins = new Dictionary<BatterRig, SkinnedMeshRenderer[]>();
        private readonly Dictionary<BatterRig, float> _orphanLogged = new Dictionary<BatterRig, float>();
        private int _batWatchBeat = -100;

        /// <summary>Called every ball (ResetFielders): the watch running (a StopAllCoroutines may have ended it).</summary>
        private void EnsureBatWatch()
        {
            if (Time.frameCount - _batWatchBeat > 5) StartCoroutine(BatWatch());
        }

        private IEnumerator BatWatch()
        {
            _batWatchBeat = Time.frameCount;
            while (true)
            {
                yield return null;
                _batWatchBeat = Time.frameCount;
                foreach (var rig in _batters.Values)
                {
                    if (rig == null || !rig.isActiveAndEnabled) continue;
                    var skins = Skins(rig);
                    var bat = rig.Bat;
                    if (bat == null || !bat.gameObject.activeInHierarchy) continue;
                    var batR = bat.GetComponentInChildren<Renderer>();
                    if (batR == null || !batR.enabled || !batR.isVisible) continue;
                    var drawn = false;
                    var on = 0;
                    foreach (var s in skins)
                    {
                        if (s == null || !s.enabled) continue;
                        on++;
                        if (s.isVisible) drawn = true;
                    }
                    var a = rig.GetComponent<Animator>();
                    var hips = a != null ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
                    var gap = hips != null ? Vector3.Distance(bat.position, hips.position) : 0f;
                    if (drawn && gap < 1.6f) continue;
                    if (_orphanLogged.TryGetValue(rig, out var last) && Time.realtimeSinceStartup - last < 3f) continue;
                    _orphanLogged[rig] = Time.realtimeSinceStartup;
                    var root = hips != null ? Vector3.Distance(Flat(rig.transform.position), Flat(hips.position)) : 0f;
                    var state = a != null ? a.GetCurrentAnimatorStateInfo(0).shortNameHash : 0;
                    Debug.LogWarning($"[APL bat] orphan: {rig.name}'s bat on screen {(drawn ? "with his body drawn" : "with his body NOT drawn")}, {gap:0.00} m from his hips (root {root:0.0} m from hips, body renderers on {on}/{skins.Length}, state {state}, replay {(_replays != null && _replays.Running ? "Y" : "N")})");
                }
            }
        }

        /// <summary>His skinned meshes, set (once) to keep their bounds on the bones as they are.</summary>
        private SkinnedMeshRenderer[] Skins(BatterRig rig)
        {
            if (_batterSkins.TryGetValue(rig, out var skins) && skins.Length > 0 && skins[0] != null) return skins;
            skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in skins) s.updateWhenOffscreen = true;
            _batterSkins[rig] = skins;
            return skins;
        }
    }
}
