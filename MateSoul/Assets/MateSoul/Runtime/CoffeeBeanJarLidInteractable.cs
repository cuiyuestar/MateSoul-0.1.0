using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(PhysicsBody))]
    public sealed class CoffeeBeanJarLidInteractable : PhysicsGrabInteractable
    {
        [SerializeField] private CoffeeBeanJarLid lid;

        public CoffeeBeanJarLid Lid => lid;

        public void Configure(CoffeeBeanJarLid value)
        {
            lid = value != null
                ? value
                : throw new ArgumentNullException(nameof(value));
            selectMode = InteractableSelectMode.Single;
            ConfigureSnap(false);
        }

        protected override void Grab()
        {
            lid?.DetachByHand();
            base.Grab();
            if (interactorsSelecting[0] is XRBaseControllerInteractor controller)
                controller.SendHapticImpulse(.24f, .045f);
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
            => lid != null && base.IsSelectableBy(interactor);
    }
}
