using System;
using System.Linq;
using MateSoul.Core;
using MateSoul.Gameplay;

namespace MateSoul.Runtime
{
    public sealed class AutonomousBehaviour
    {
        private readonly AvatarActionCoordinator avatar;
        private readonly INavigationService navigation;
        private readonly IAvatarMotor motor;
        private readonly ItemRegistry items;
        private readonly Random random = new Random();
        private float wait;
        public bool Enabled { get; set; } = true;

        public AutonomousBehaviour(AvatarActionCoordinator avatar, INavigationService navigation,
            IAvatarMotor motor, ItemRegistry items)
        {
            this.avatar = avatar; this.navigation = navigation; this.motor = motor; this.items = items;
        }

        public void Tick(float seconds)
        {
            if (!Enabled || !avatar.IsIdle) return;
            wait -= seconds;
            if (wait > 0) return;
            wait = 2f;
            var choice = random.NextDouble();
            if (choice < .6 && navigation.TryRandom(motor.Position, random, out var target))
                avatar.Submit(new AvatarIntent("move", IntentSource.Autonomous, payload: target));
            else if (choice > .85)
            {
                var available = items.Items.Where(i => i.Supports("visit") && !i.IsOccupied).ToArray();
                if (available.Length > 0)
                    avatar.Submit(new AvatarIntent("visit", IntentSource.Autonomous,
                        available[random.Next(available.Length)].Id));
            }
            else avatar.Submit(new AvatarIntent("stay", IntentSource.Autonomous));
        }
    }
}
