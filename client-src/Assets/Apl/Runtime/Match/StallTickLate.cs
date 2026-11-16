using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>The last beats in a frame's Update and LateUpdate for <see cref="StallWatch"/> (see <see cref="StallTick"/>).</summary>
    [DefaultExecutionOrder(32000)]
    public sealed class StallTickLate : MonoBehaviour
    {
        private void Update() => StallWatch.Beat("updates done", Time.frameCount);

        private void LateUpdate() => StallWatch.Beat("late done", Time.frameCount);
    }
}
