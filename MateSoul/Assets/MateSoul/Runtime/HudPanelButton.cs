using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    // The hit rectangle stays fixed; only the label responds to hover/press.
    public sealed class HudPanelButton : Button
    {
        private Image surface;
        private TMP_Text label;
        private Color normalSurface, normalText, selectedSurface, selectedText;
        private Color targetSurface, targetText;
        private float targetScale = 1;
        private bool selected, animating;

        public void Configure(Image background, TMP_Text text, Color backgroundColor,
            Color textColor, Color selectionColor, Color selectionTextColor)
        {
            surface = background;
            label = text;
            normalSurface = backgroundColor;
            normalText = textColor;
            selectedSurface = selectionColor;
            selectedText = selectionTextColor;
            transition = Transition.None;
            targetGraphic = surface;
            DoStateTransition(currentSelectionState, true);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            DoStateTransition(currentSelectionState, !gameObject.activeInHierarchy);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (surface == null || label == null) return;
            targetSurface = selected ? selectedSurface : normalSurface;
            targetText = selected ? selectedText : normalText;
            targetScale = 1;
            if (state == SelectionState.Disabled)
            {
                targetSurface.a *= .48f;
                targetText.a *= .48f;
            }
            else if (state == SelectionState.Pressed)
            {
                targetSurface = Color.Lerp(targetSurface, selectedSurface, .55f);
                targetScale = .96f;
            }
            else if (state == SelectionState.Highlighted || state == SelectionState.Selected)
            {
                targetSurface = Color.Lerp(targetSurface, selectedSurface, .24f);
                targetScale = 1.025f;
            }
            animating = !instant;
            if (instant) ApplyTarget();
        }

        private void Update()
        {
            if (!animating) return;
            var blend = 1 - Mathf.Exp(-24f * Time.unscaledDeltaTime);
            surface.color = Color.Lerp(surface.color, targetSurface, blend);
            label.color = Color.Lerp(label.color, targetText, blend);
            label.rectTransform.localScale = Vector3.Lerp(
                label.rectTransform.localScale, Vector3.one * targetScale, blend);
            if (Mathf.Abs(label.rectTransform.localScale.x - targetScale) < .001f &&
                Vector4.Distance(surface.color, targetSurface) < .002f &&
                Vector4.Distance(label.color, targetText) < .002f)
            {
                ApplyTarget();
                animating = false;
            }
        }

        private void ApplyTarget()
        {
            surface.color = targetSurface;
            label.color = targetText;
            label.rectTransform.localScale = Vector3.one * targetScale;
        }
    }
}
