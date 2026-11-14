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
        private readonly Func<ItemInstance, bool> canUseItem;
        private readonly Random random = new Random();
        private float wait;
        public bool Enabled { get; set; } = true;

        public AutonomousBehaviour(AvatarActionCoordinator avatar, INavigationService navigation,
            IAvatarMotor motor, ItemRegistry items, Func<ItemInstance, bool> canUseItem = null)
        {
            this.avatar = avatar; this.navigation = navigation; this.motor = motor; this.items = items;
            this.canUseItem = canUseItem;
        }

        public void Tick(float seconds)
        {
            if (!Enabled || !avatar.IsIdle) return;
            wait -= seconds;
            if (wait > 0) return;
            wait = 2f;
            var choice = random.NextDouble();
            // Walking and the three idle variations stay the base behaviour; 20% goes to item use.
            if (choice < .4 && navigation.TryRandom(motor.Position, random, out var target))
            {
                avatar.Submit(new AvatarIntent("move", IntentSource.Autonomous, payload: target));
                return;
            }
            if (choice >= .8)
            {
                var available = items.Items.Where(i => i.IsPresent && i.Supports("use") &&
                    !i.IsOccupied && canUseItem != null && canUseItem(i)).ToArray();
                if (available.Length > 0)
                {
                    avatar.Submit(new AvatarIntent(UseHeldItemAction.Id, IntentSource.Autonomous,
                        available[random.Next(available.Length)].Id));
                    return;
                }
            }
            avatar.Submit(new AvatarIntent("stay", IntentSource.Autonomous));
        }
    }
}
