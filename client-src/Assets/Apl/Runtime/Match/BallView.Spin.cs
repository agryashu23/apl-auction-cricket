using Apl.Core.Play;

namespace Apl.Client.Match
{
    /// <summary>The ball's spin, seam and movement cues (<see cref="BallSpin"/>), set as each delivery leaves the hand.</summary>
    public sealed partial class BallView
    {
        /// <summary>Out of the hand with this delivery: the seam spins its way, swing and bounce cues on.</summary>
        public void Release(Delivery d, bool leftHanded)
        {
            Release();
            BallSpin.On(gameObject).Bowl(d, leftHanded);
        }
    }
}
