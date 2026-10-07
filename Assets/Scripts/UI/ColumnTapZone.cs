using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Full-height column hit zone that drops a tile on pointer DOWN, so a tap registers the moment the
    /// finger lands and a small slide before lifting can't cancel it. The sibling Button is kept only as
    /// an interactable gate (tutorial locking, CanvasGroups) and has no onClick listener.
    /// </summary>
    public class ColumnTapZone : MonoBehaviour, IPointerDownHandler
    {
        static readonly Color PressColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color RejectColor = new Color(1f, 0.25f, 0.25f, 0.35f);
        const float FlashFadeSeconds = 0.15f;

        public int Column;
        public Selectable Gate;
        public Action<int> OnTap;
        /// <summary>Raised instead of OnTap when the gate is locked (e.g. a non-highlighted column in the tutorial).</summary>
        public Action<int> OnRejected;
        public Image Flash;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Gate != null && !Gate.IsInteractable())
            {
                OnRejected?.Invoke(Column);
                return;
            }

            PlayFlash(PressColor);
            OnTap?.Invoke(Column);
        }

        /// <summary>Red flash used when a tap is refused (full column, wrong tutorial column).</summary>
        public void FlashReject() => PlayFlash(RejectColor);

        void PlayFlash(Color color)
        {
            if (Flash == null) return;
            Flash.DOKill();
            Flash.color = color;
            Flash.DOFade(0f, FlashFadeSeconds).SetUpdate(true).SetLink(Flash.gameObject);
        }
    }
}
