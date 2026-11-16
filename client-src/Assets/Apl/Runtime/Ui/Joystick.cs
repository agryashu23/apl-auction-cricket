using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Apl.Client.Ui
{
    /// <summary>
    /// A thumb stick: the knob follows the finger anywhere inside the base and
    /// reports the direction and throw (0–1) while held, null when let go (the
    /// knob springs back to the middle).
    /// </summary>
    public sealed class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Knob = null!;
        public float Radius = 70f;

        public event Action<Vector2?>? Changed;

        private RectTransform _rt = null!;

        private void Awake() => _rt = (RectTransform)transform;

        public void OnPointerDown(PointerEventData e) => Move(e);

        public void OnDrag(PointerEventData e) => Move(e);

        public void OnPointerUp(PointerEventData e)
        {
            Knob.anchoredPosition = Vector2.zero;
            Changed?.Invoke(null);
        }

        private void Move(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local);
            var v = Vector2.ClampMagnitude(local, Radius);
            Knob.anchoredPosition = v;
            Changed?.Invoke(v / Radius);
        }
    }
}
