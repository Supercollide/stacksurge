using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StackSurge.UI
{
    /// <summary>Raises <see cref="Released"/> when a press that started on this object ends (e.g. letting go of a slider).</summary>
    public class PointerUpRelay : MonoBehaviour, IPointerUpHandler
    {
        public event Action Released;

        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
    }
}
