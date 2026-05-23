using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;

using System.Reflection;

namespace LivingCompanionsValley.Services
{
    public class WorkerFakeFarmer : Farmer
    {
        private static readonly MethodInfo? _updateMovementAnimationMethod = typeof(Farmer).GetMethod("updateMovementAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? _performBeginUsingToolMethod = typeof(Farmer).GetMethod("performBeginUsingTool", BindingFlags.NonPublic | BindingFlags.Instance);

        public WorkerFakeFarmer() : base()
        {
        }

        public void TickVisuals(GameTime time, bool isMoving, int facingDirection, Vector2 position, GameLocation location)
        {
            this.Position = position;
            this.currentLocation = location;
            this.FacingDirection = facingDirection;

            // Advance the tool animations
            this.FarmerSprite.checkForSingleAnimation(time);

            if (this.UsingTool)
            {
                // If using a tool, keep them still and ensure the animation plays out
                this.Halt();

                // Advance tool use time or end tool use if animation is finished
                // The native system relies on sprite index reaching the end for most tools
                if (this.FarmerSprite.currentAnimationIndex >= this.FarmerSprite.CurrentAnimation?.Count - 1 || this.FarmerSprite.CurrentAnimation == null)
                {
                     // Normally handled by endBehaviors, but we can forcefully end it if it gets stuck
                     if (this.FarmerSprite.CurrentAnimation == null && !this.FarmerSprite.pauseForSingleAnimation)
                     {
                         this.EndUsingTool();
                     }
                }
            }
            else
            {
                if (isMoving)
                {
                    this.setMovingInFacingDirection();
                    this.setRunning(true);

                    // We need to bypass updateCommon and natively advance the walking animation
                    // This is usually done by updateMovementAnimation
                    try
                    {
                        _updateMovementAnimationMethod?.Invoke(this, new object[] { time });
                    }
                    catch (Exception ex)
                    {
                        ModEntry.Logger?.LogOnce($"[WorkerFakeFarmer] Error updating movement animation: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
                    }
                }
                else
                {
                    this.Halt();
                    this.FarmerSprite.StopAnimation();
                }
            }
        }

        public void SwingTool(Tool tool, int direction)
        {
            this.FacingDirection = direction;
            this.CurrentTool = tool;
            this.UsingTool = true;

            // Native tools rely on lastClick to know where they are swinging
            this.lastClick = this.GetToolLocation();

            // Bypass IsLocalPlayer restrictions to start the animation
            try
            {
                _performBeginUsingToolMethod?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                ModEntry.Logger?.LogOnce($"[WorkerFakeFarmer] Error initiating tool swing: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
            }
        }

        public void EndUsingTool()
        {
            this.UsingTool = false;
            this.CurrentTool = null;
            this.FarmerSprite.pauseForSingleAnimation = false;
            this.Halt();
        }
    }
}
