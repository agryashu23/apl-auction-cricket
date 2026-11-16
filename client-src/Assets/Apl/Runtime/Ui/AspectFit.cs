using UnityEngine;
using UnityEngine.UI;

namespace Apl.Client.Ui
{
    /// <summary>
    /// Keeps every UiKit canvas at least 1920 × 1080 reference pixels in BOTH
    /// directions whatever the phone: wider than 16:9 (20:9 phones) it matches the
    /// height, so the full 1080 is there and the extra width is spare at the
    /// sides; narrower (tablets) it matches the width. Screens laid out in
    /// 1920 × 1080 then never lose their top and bottom (the pause menu did).
    /// </summary>
    public sealed class AspectFit : MonoBehaviour
    {
        private CanvasScaler _scaler = null!;
        private float _aspect = -1;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            Fit();
        }

        private void Update()
        {
            if (!Mathf.Approximately(_aspect, Aspect)) Fit();
        }

        // -aplaspect (Dev/AspectFlag): the forced shape from the first frame, before the window resize lands.
        private static float Aspect => Apl.Client.Dev.AspectFlag.Aspect;

        private void Fit()
        {
            _aspect = Aspect;
            if (_scaler != null) _scaler.matchWidthOrHeight = _aspect >= 16f / 9f ? 1f : 0f;
        }
    }
}
