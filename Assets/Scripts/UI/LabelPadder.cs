using UnityEngine;
using UnityEngine.UI;

namespace StackSurge.UI
{
    /// <summary>
    /// Re-applies <see cref="UiSkin.PadLabels"/> whenever its button is resized, for buttons a layout group sizes
    /// after they are created (their rect is still zero when the skin pass runs).
    /// </summary>
    [DisallowMultipleComponent]
    public class LabelPadder : MonoBehaviour
    {
        Selectable _selectable;

        void OnEnable() => Pad();

        void OnRectTransformDimensionsChange() => Pad();

        void Pad()
        {
            if (_selectable == null) _selectable = GetComponent<Selectable>();
            UiSkin.PadLabels(_selectable, keepUpdated: false);
        }
    }
}
