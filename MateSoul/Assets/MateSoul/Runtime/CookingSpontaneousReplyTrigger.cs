using System;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CookableFood))]
    public sealed class CookingSpontaneousReplyTrigger : MonoBehaviour
    {
        [SerializeField] private string triggerId = "cooking.aroma";
        [SerializeField, TextArea] private string eventDescription;
        [SerializeField] private string[] contentRequirements = Array.Empty<string>();
        [SerializeField] private string[] rules = Array.Empty<string>();

        private CookableFood food;
        private GameItem item;
        private bool triggered;

        public void Configure(string id, string eventText,
            string[] requirements, string[] constraints)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(eventText) ||
                requirements == null || requirements.Length == 0 ||
                constraints == null || constraints.Length == 0)
                throw new ArgumentException(
                    "Cooking spontaneous reply trigger configuration is incomplete.");
            triggerId = id;
            eventDescription = eventText;
            contentRequirements = (string[])requirements.Clone();
            rules = (string[])constraints.Clone();
        }

        private void Awake()
        {
            food = GetComponent<CookableFood>();
            item = GetComponent<GameItem>();
        }

        private void OnEnable()
        {
            if (food == null) food = GetComponent<CookableFood>();
            food.DonenessChanged += OnDonenessChanged;
        }

        private void OnDisable()
        {
            if (food != null) food.DonenessChanged -= OnDonenessChanged;
        }

        private void OnDonenessChanged(FoodDoneness stage)
        {
            if (triggered || stage < FoodDoneness.Medium) return;
            triggered = true;
            if (item == null) item = GetComponent<GameItem>();
            var instanceId = item?.Instance?.Id ?? GetInstanceID().ToString();
            item?.Session?.SpontaneousReplies.Enqueue(
                $"{triggerId}:{instanceId}",
                eventDescription,
                contentRequirements,
                rules);
        }
    }
}
