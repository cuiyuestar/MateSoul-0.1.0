using System;
using UnityEngine;

namespace MateSoul.Runtime
{
    [CreateAssetMenu(menuName = "MateSoul/Conversation Gateway", fileName = "ConversationGateway")]
    public sealed class ConversationGatewaySettings : ScriptableObject
    {
        [SerializeField] private string endpoint;
        [SerializeField, Min(1)] private int timeoutSeconds = 60;
        [SerializeField] private bool allowLocalHttpInDevelopment;

        public string Endpoint => endpoint;
        public int TimeoutSeconds => timeoutSeconds;
        public bool AllowLocalHttpInDevelopment => allowLocalHttpInDevelopment;

        public void Configure(string serviceEndpoint, int requestTimeoutSeconds,
            bool allowDevelopmentHttp)
        {
            if (requestTimeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestTimeoutSeconds));
            endpoint = serviceEndpoint;
            timeoutSeconds = requestTimeoutSeconds;
            allowLocalHttpInDevelopment = allowDevelopmentHttp;
        }
    }
}
