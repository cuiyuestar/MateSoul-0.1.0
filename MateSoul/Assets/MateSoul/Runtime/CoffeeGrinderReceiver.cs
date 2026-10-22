using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(Collider))]
    public sealed class CoffeeGrinderReceiver : MonoBehaviour
    {
        [SerializeField] private CoffeeGrinderController grinder;

        public CoffeeGrinderController Grinder => grinder;

        public void Configure(CoffeeGrinderController value)
        {
            grinder = value != null
                ? value
                : throw new ArgumentNullException(nameof(value));
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var bean = other.GetComponentInParent<CoffeeBean>();
            if (bean != null) grinder.TryAcceptBean(bean);
        }

        private void OnTriggerStay(Collider other)
        {
            var pestle = other.GetComponentInParent<CoffeePestle>();
            if (pestle != null)
                grinder.ObservePestle(pestle, Time.fixedDeltaTime);
        }

        private void OnTriggerExit(Collider other)
        {
            var pestle = other.GetComponentInParent<CoffeePestle>();
            if (pestle != null) grinder.StopObserving(pestle);
        }
    }
}
