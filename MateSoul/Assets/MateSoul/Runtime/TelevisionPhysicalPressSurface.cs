using System.Collections.Generic;
using UnityEngine;

namespace MateSoul.Runtime
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TelevisionPhysicalPressSurface : MonoBehaviour
    {
        private sealed class Contact
        {
            public PhysicalPressInteractor Interactor;
            public int ColliderCount;
        }

        [SerializeField] private TelevisionPowerButton button;
        [SerializeField, Min(.001f)] private float pressDepth = .012f;
        private readonly Dictionary<int, Contact> contacts = new Dictionary<int, Contact>();
        private readonly List<int> staleContacts = new List<int>();

        public void Configure(TelevisionPowerButton target, float depth)
        {
            button = target;
            pressDepth = Mathf.Max(.001f, depth);
            var trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var interactor = other.GetComponentInParent<PhysicalPressInteractor>();
            if (interactor == null) return;
            var id = interactor.GetInstanceID();
            if (!contacts.TryGetValue(id, out var contact))
            {
                contact = new Contact { Interactor = interactor };
                contacts.Add(id, contact);
            }
            contact.ColliderCount++;
            UpdatePress(interactor);
        }

        private void OnTriggerStay(Collider other)
        {
            var interactor = other.GetComponentInParent<PhysicalPressInteractor>();
            if (interactor != null) UpdatePress(interactor);
        }

        private void OnTriggerExit(Collider other)
        {
            var interactor = other.GetComponentInParent<PhysicalPressInteractor>();
            if (interactor == null) return;
            var id = interactor.GetInstanceID();
            if (!contacts.TryGetValue(id, out var contact)) return;
            if (contact.ColliderCount > 1)
            {
                contact.ColliderCount--;
                return;
            }
            contacts.Remove(id);
            button.EndPhysicalPress(id);
        }

        private void UpdatePress(PhysicalPressInteractor interactor)
        {
            var depth = Vector3.Dot(interactor.PressPosition - transform.position, transform.forward);
            button.SetPhysicalPress(interactor.GetInstanceID(), Mathf.Clamp01(depth / pressDepth));
        }

        private void LateUpdate()
        {
            staleContacts.Clear();
            foreach (var entry in contacts)
                if (entry.Value.Interactor == null || !entry.Value.Interactor.isActiveAndEnabled)
                    staleContacts.Add(entry.Key);
            foreach (var id in staleContacts)
            {
                contacts.Remove(id);
                button.EndPhysicalPress(id);
            }
        }

        private void OnDisable()
        {
            if (button != null)
                foreach (var id in contacts.Keys) button.EndPhysicalPress(id);
            contacts.Clear();
        }
    }
}
