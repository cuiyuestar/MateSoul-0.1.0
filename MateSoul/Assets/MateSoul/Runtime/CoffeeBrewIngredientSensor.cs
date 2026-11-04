using System;
using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(Collider))]
    public sealed class CoffeeBrewIngredientSensor : MonoBehaviour
    {
        [SerializeField] private CoffeeBrewReceiver receiver;

        private readonly HashSet<CoffeeBean> beans =
            new HashSet<CoffeeBean>();

        public CoffeeBrewReceiver Receiver => receiver;

        public void Configure(CoffeeBrewReceiver value)
        {
            receiver = value != null
                ? value
                : throw new ArgumentNullException(nameof(value));
            GetComponent<Collider>().isTrigger = true;
            AlignOpening();
        }

        private void Awake()
        {
            if (receiver == null)
                throw new InvalidOperationException(
                    "Coffee ingredient sensor binding is incomplete.");
            GetComponent<Collider>().isTrigger = true;
            AlignOpening();
        }

        private void AlignOpening()
        {
            var water = receiver.Water;
            transform.position = water.transform.TransformPoint(
                water.OpeningCenter + Vector3.down * .012f);
            if (GetComponent<Collider>() is SphereCollider sphere)
                sphere.radius = water.OpeningRadius * .95f;
        }

        private void Start() => AlignOpening();

        private void OnTriggerEnter(Collider other) => Observe(other);

        private void OnTriggerStay(Collider other) => Observe(other);

        private void Observe(Collider other)
        {
            var bean = other.GetComponentInParent<CoffeeBean>();
            if (bean == null || bean.IsCaptured || !beans.Add(bean)) return;
            receiver.RegisterWholeBean(bean);
        }

        private void OnTriggerExit(Collider other)
        {
            var bean = other.GetComponentInParent<CoffeeBean>();
            if (bean == null || !beans.Remove(bean)) return;
            receiver.UnregisterWholeBean(bean);
        }

        private void OnDisable()
        {
            foreach (var bean in beans)
                if (bean != null) receiver?.UnregisterWholeBean(bean);
            beans.Clear();
        }
    }
}
