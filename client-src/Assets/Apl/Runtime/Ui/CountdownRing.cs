using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// The autoselect clock: a ring that empties as the time runs out (gold, then
    /// red for the last three seconds, with a pulse) and the seconds in the middle.
    /// </summary>
    public sealed class CountdownRing : MonoBehaviour
    {
        public Image Fill = null!;
        public Text Label = null!;
        private int _shown = -1;

        /// <summary><paramref name="left"/> seconds of <paramref name="total"/>.</summary>
        public void Show(float left, float total)
        {
            var k = total <= 0 ? 0 : Mathf.Clamp01(left / total);
            Fill.fillAmount = k;
            var secs = Mathf.CeilToInt(Mathf.Max(0, left));
            var urgent = secs <= 3;
            Fill.color = urgent ? UiKit.Bad : UiKit.Gold;
            if (secs != _shown)
            {
                _shown = secs;
                Label.text = secs.ToString();
            }
            var pulse = urgent ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
            transform.localScale = new Vector3(pulse, pulse, 1);
        }

        /// <summary>No clock (a picker that waits for you): the ring full and quiet.</summary>
        public void Hide() => gameObject.SetActive(false);
    }
}
