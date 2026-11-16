using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Apl.Client.Ui
{
    /// <summary>
    /// Fires the moment a finger touches (a Button only fires when it lifts,
    /// ~0.1 s later) — for the shot buttons, where that delay is a late shot.
    /// </summary>
    public sealed class OnPress : MonoBehaviour, IPointerDownHandler
    {
        public Action? Pressed;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();
    }
}
