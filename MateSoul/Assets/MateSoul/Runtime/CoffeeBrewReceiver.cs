using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    [DisallowMultipleComponent, RequireComponent(typeof(WaterReceiver))]
    public sealed class CoffeeBrewReceiver : MonoBehaviour, IRuntimeWorldFactSource
    {
        public const int DefaultGroundUnitsPerServing = 12;
        public const float DefaultMinimumWaterLiters = .08f;
        public const float DefaultMinimumTemperatureCelsius = 80f;
        private static readonly Color CoffeeColor =
            new Color(.115f, .042f, .014f, .86f);

        [SerializeField] private WaterReceiver water;
        [SerializeField, Min(1)] private int maximumGroundUnits = 48;
        [SerializeField, Min(1)] private int groundUnitsPerServing =
            DefaultGroundUnitsPerServing;
        [SerializeField, Min(.01f)] private float minimumWaterLiters =
            DefaultMinimumWaterLiters;
        [SerializeField, Min(21f)] private float minimumTemperatureCelsius =
            DefaultMinimumTemperatureCelsius;
        [SerializeField] private int groundUnits;
        [SerializeField] private bool coffee;

        private readonly HashSet<CoffeeBean> wholeBeans =
            new HashSet<CoffeeBean>();
        private Renderer liquidRenderer;
        private MaterialPropertyBlock properties;
        private SystemNotifications notifications;
        private bool subscribed;
        private bool coldWaterWarningShown;
        private bool wholeBeanWarningShown;

        public WaterReceiver Water => water;
        public int GroundUnits => groundUnits;
        public int MaximumGroundUnits => maximumGroundUnits;
        public int GroundUnitsPerServing => groundUnitsPerServing;
        public float MinimumWaterLiters => minimumWaterLiters;
        public float MinimumTemperatureCelsius =>
            minimumTemperatureCelsius;
        public bool IsCoffee => coffee;
        public int WholeBeanCount
        {
            get
            {
                RemoveDestroyedWholeBeans();
                return wholeBeans.Count;
            }
        }
        public event Action<CoffeeBrewReceiver> BrewCompleted;

        public bool TryCaptureRuntimeWorldFact(
            out RuntimeWorldFactSnapshot fact)
        {
            var wholeBeans = WholeBeanCount;
            if (!coffee && groundUnits <= 0 && wholeBeans <= 0 &&
                (water == null || water.CurrentLiters <= 0))
            {
                fact = null;
                return false;
            }
            fact = RuntimeWorldFact.Create(
                this,
                "coffee",
                coffee ? "brewed" : "ingredients",
                $"water_ml={Mathf.RoundToInt((water?.CurrentLiters ?? 0) * 1000f)};" +
                $"temperature_c={Mathf.RoundToInt(water?.CurrentTemperatureCelsius ?? 0)};" +
                $"ground_units={groundUnits};whole_beans={wholeBeans}");
            return true;
        }

        public void Configure(WaterReceiver receiver,
            int servingGroundUnits = DefaultGroundUnitsPerServing,
            float minimumLiters = DefaultMinimumWaterLiters,
            float minimumTemperature = DefaultMinimumTemperatureCelsius,
            int groundCapacity = 48)
        {
            if (receiver == null || receiver.gameObject != gameObject ||
                servingGroundUnits <= 0 ||
                !PhysicsBody.IsFinite(minimumLiters) || minimumLiters <= 0 ||
                !PhysicsBody.IsFinite(minimumTemperature) ||
                minimumTemperature < WaterReceiver.AmbientTemperatureCelsius ||
                groundCapacity < servingGroundUnits)
                throw new ArgumentException(
                    "Coffee brew receiver bindings and limits are incomplete.");
            water = receiver;
            groundUnitsPerServing = servingGroundUnits;
            minimumWaterLiters = minimumLiters;
            minimumTemperatureCelsius = minimumTemperature;
            maximumGroundUnits = groundCapacity;
            groundUnits = 0;
            coffee = false;
            coldWaterWarningShown = false;
            wholeBeanWarningShown = false;
            wholeBeans.Clear();
            ResolveVisual();
            ApplyVisual();
        }

        public void BindNotifications(SystemNotifications value)
            => notifications = value ??
               throw new ArgumentNullException(nameof(value));

        private void Awake()
        {
            if (water == null || water.gameObject != gameObject)
                throw new InvalidOperationException(
                    "Coffee brew receiver bindings are incomplete.");
            ResolveVisual();
            ApplyVisual();
        }

        private void OnEnable()
        {
            Subscribe();
            ApplyVisual();
            EvaluateContents();
        }

        private void OnDisable() => Unsubscribe();

        public bool CanReceiveGroundCoffeeAt(Vector3 worldPoint)
            => !coffee && groundUnits < maximumGroundUnits &&
               water.CanReceiveAt(worldPoint);

        public int ReceiveGroundCoffee(int requestedUnits,
            Vector3 worldPoint)
        {
            if (requestedUnits <= 0 ||
                !CanReceiveGroundCoffeeAt(worldPoint))
                return 0;
            var accepted = Mathf.Min(
                requestedUnits, maximumGroundUnits - groundUnits);
            groundUnits += accepted;
            EvaluateContents();
            return accepted;
        }

        public bool RegisterWholeBean(CoffeeBean bean)
        {
            if (bean == null || bean.IsCaptured || !wholeBeans.Add(bean))
                return false;
            EvaluateContents();
            return true;
        }

        public void UnregisterWholeBean(CoffeeBean bean)
        {
            if (bean == null || !wholeBeans.Remove(bean)) return;
            if (wholeBeans.Count == 0) wholeBeanWarningShown = false;
            EvaluateContents();
        }

        private void Subscribe()
        {
            if (subscribed || water == null) return;
            water.ContentsChanged += OnWaterChanged;
            water.Emptied += OnWaterEmptied;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || water == null) return;
            water.ContentsChanged -= OnWaterChanged;
            water.Emptied -= OnWaterEmptied;
            subscribed = false;
        }

        private void OnWaterChanged(WaterReceiver _) => EvaluateContents();

        private void OnWaterEmptied(WaterReceiver _)
        {
            groundUnits = 0;
            coffee = false;
            coldWaterWarningShown = false;
            wholeBeanWarningShown = false;
            ApplyVisual();
        }

        private void EvaluateContents()
        {
            RemoveDestroyedWholeBeans();
            if (water == null || water.CurrentLiters <= 0)
            {
                coldWaterWarningShown = false;
                wholeBeanWarningShown = false;
                return;
            }

            if (wholeBeans.Count > 0)
            {
                coldWaterWarningShown = false;
                if (!wholeBeanWarningShown)
                {
                    wholeBeanWarningShown = true;
                    ShowHint("请先将咖啡豆研磨成粉再冲泡");
                }
                return;
            }
            wholeBeanWarningShown = false;

            if (!coffee &&
                groundUnits >= groundUnitsPerServing &&
                water.CurrentLiters >= minimumWaterLiters &&
                water.CurrentTemperatureCelsius >=
                minimumTemperatureCelsius)
            {
                Brew();
                return;
            }

            if (!coffee && groundUnits > 0 &&
                water.CurrentTemperatureCelsius <
                minimumTemperatureCelsius)
            {
                if (!coldWaterWarningShown)
                {
                    coldWaterWarningShown = true;
                    ShowHint("请用加热后的水冲泡咖啡");
                }
                return;
            }
            coldWaterWarningShown = false;
        }

        private void Brew()
        {
            groundUnits -= groundUnitsPerServing;
            coffee = true;
            coldWaterWarningShown = false;
            ApplyVisual();
            ShowHint("咖啡冲泡完成");
            BrewCompleted?.Invoke(this);
        }

        private void RemoveDestroyedWholeBeans()
        {
            wholeBeans.RemoveWhere(bean => bean == null);
            if (wholeBeans.Count == 0) wholeBeanWarningShown = false;
        }

        private void ResolveVisual()
        {
            liquidRenderer = water?.WaterVisual == null
                ? null
                : water.WaterVisual.GetComponent<Renderer>();
            if (liquidRenderer != null && properties == null)
                properties = new MaterialPropertyBlock();
        }

        private void ApplyVisual()
        {
            if (liquidRenderer == null) return;
            if (!coffee)
            {
                water.LiquidColor = new Color(.35f, .78f, .95f, .55f);
                liquidRenderer.SetPropertyBlock(null);
                return;
            }
            liquidRenderer.GetPropertyBlock(properties);
            water.LiquidColor = CoffeeColor;
            properties.SetColor("_BaseColor", CoffeeColor);
            properties.SetColor("_Color", CoffeeColor);
            liquidRenderer.SetPropertyBlock(properties);
        }

        private void ShowHint(string message)
        {
            if (notifications != null)
            {
                notifications.ShowHint(message);
                return;
            }
            var item = GetComponent<GameItem>();
            if (item?.Session != null)
                item.Session.Notifications.ShowHint(message);
        }
    }
}
