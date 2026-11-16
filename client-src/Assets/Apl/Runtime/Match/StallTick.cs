using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// The main thread's beats for <see cref="StallWatch"/>, first in the frame: before every other
    /// script's Update ("frame start"), FixedUpdate ("fixed step") and LateUpdate ("late start"), and once
    /// the frame is drawn ("rendered"). <see cref="StallTickLate"/> beats last in Update and LateUpdate,
    /// so the phase a stall is stuck after says which part of the frame holds it. Test runs only.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class StallTick : MonoBehaviour
    {
        private static StallTick? _me;

        public static void Create()
        {
            if (_me != null) return;
            var go = new GameObject("StallTick");
            DontDestroyOnLoad(go);
            _me = go.AddComponent<StallTick>();
            go.AddComponent<StallTickLate>();
        }

        private void Start() => StartCoroutine(Rendered());

        private void FixedUpdate() => StallWatch.Beat("fixed step", Time.frameCount);

        private void Update() => StallWatch.Beat("frame start", Time.frameCount);

        private void LateUpdate() => StallWatch.Beat("late start", Time.frameCount);

        private static IEnumerator Rendered()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                StallWatch.Beat("rendered", Time.frameCount);
            }
        }
    }
}
