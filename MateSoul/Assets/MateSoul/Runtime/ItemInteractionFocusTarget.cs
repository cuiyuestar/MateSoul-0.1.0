using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MateSoul.Runtime
{
    /// <summary>Declares the renderers that represent one interactive control on an item.</summary>
    public sealed class ItemInteractionFocusTarget : MonoBehaviour
    {
        [SerializeField] private Selectable control;
        [SerializeField] private Material focusMaterial;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();

        private readonly Dictionary<Renderer, Material[]> baseMaterials =
            new Dictionary<Renderer, Material[]>();
        private bool requested, applied;

        public Selectable Control => control;
        public Material FocusMaterial => focusMaterial;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public bool IsFocusRequested => requested;
        public bool IsFocused => applied;

        public void Configure(Selectable owner, Material material, params Renderer[] targets)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (targets == null || targets.Length == 0 || targets.Any(target => target == null))
                throw new ArgumentException(
                    "An interaction focus target requires at least one renderer.", nameof(targets));
            control = owner;
            focusMaterial = material;
            renderers = targets.Distinct().ToArray();
            baseMaterials.Clear();
            applied = false;
        }

        public void SetFocused(bool value)
        {
            requested = value;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            var next = requested && isActiveAndEnabled &&
                       (control == null || control.IsInteractable());
            if (next == applied) return;
            CaptureBaseMaterials();
            foreach (var entry in baseMaterials)
            {
                if (entry.Key == null) continue;
                entry.Key.sharedMaterials = next
                    ? entry.Value.Concat(new[] { focusMaterial }).ToArray()
                    : entry.Value;
            }
            applied = next;
        }

        private void CaptureBaseMaterials()
        {
            if (baseMaterials.Count > 0) return;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                baseMaterials.Add(renderer, renderer.sharedMaterials
                    .Where(material => material != focusMaterial).ToArray());
            }
        }

        private void OnDisable()
        {
            requested = false;
            if (!applied) return;
            foreach (var entry in baseMaterials)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
            applied = false;
        }
    }
}
