using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Full-height column hit zone that drops a tile on pointer DOWN, so a tap registers the moment the
    /// finger lands and a small slide before lifting can't cancel it. The sibling Button is kept only as
    /// an interactable gate (tutorial locking, CanvasGroups) and has no onClick listener.
    /// Feedback comes from the tile itself (drop animation) rather than a column flash.
    /// </summary>
    public class ColumnTapZone : MonoBehaviour, IPointerDownHandler
    {
        public int Column;
        public Selectable Gate;
        public Action<int> OnTap;
        /// <summary>Raised instead of OnTap when the gate is locked (e.g. a non-highlighted column in the tutorial).</summary>
        public Action<int> OnRejected;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Gate != null && !Gate.IsInteractable())
            {
                OnRejected?.Invoke(Column);
                return;
            }

            OnTap?.Invoke(Column);
        }
    }
}
