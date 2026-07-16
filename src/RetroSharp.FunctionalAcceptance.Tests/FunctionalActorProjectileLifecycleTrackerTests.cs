namespace RetroSharp.FunctionalAcceptance.Tests;

using Xunit;

public sealed class FunctionalActorProjectileLifecycleTrackerTests
{
    [Fact]
    public void Actor_contact_and_landing_state_retains_the_production_world_position()
    {
        var tracker = new FunctionalActorProjectileLifecycleTracker("actor-framework");

        tracker.Update(new(
            Frame: 1,
            HeldInputs: [],
            DynamicSprites: [new("actor-0", FunctionalDynamicSpriteKind.Actor, Active: true, ObservedVisible: true)],
            UsedActorSpawns: 1,
            ActiveActorCount: 1,
            Actors: [new(Active: true, State: 1, WorldY: 8, VerticalVelocity: 0)]));

        Assert.Equal(1, tracker.CurrentActorTileContacts);
        Assert.Equal(1, tracker.CurrentGroundedActors);
        Assert.Equal(8, tracker.MinimumGroundedActorWorldY);
        Assert.Equal(8, tracker.MaximumGroundedActorWorldY);
    }

    [Fact]
    public void Lifecycle_tracker_sequences_visibility_and_preserves_pool_evidence()
    {
        var tracker = new FunctionalActorProjectileLifecycleTracker("shots-bouncy");

        tracker.Update(ProjectileFrame(
            frame: 1,
            fireTick: 1,
            active: true,
            visible: false,
            verticalVelocity: 1));
        tracker.Update(ProjectileFrame(
            frame: 2,
            fireTick: 0,
            active: true,
            visible: true,
            verticalVelocity: -1));
        tracker.Update(ProjectileFrame(
            frame: 3,
            fireTick: 1,
            active: false,
            visible: false,
            verticalVelocity: 0));
        tracker.Update(ProjectileFrame(
            frame: 4,
            fireTick: 0,
            active: false,
            visible: false,
            verticalVelocity: 0));
        tracker.Update(ProjectileFrame(
            frame: 5,
            fireTick: 1,
            active: true,
            visible: true,
            verticalVelocity: 0));

        Assert.Equal(2, tracker.ActivatedSequence);
        Assert.Equal(2, tracker.VisibleSequence);
        Assert.Equal(1, tracker.MaximumSpawnToVisibleFrames);
        Assert.Equal(1, tracker.ReusedProjectileSlots);
        Assert.Equal(1, tracker.DroppedRequests);
        Assert.Equal(1, tracker.BounceContacts);
        Assert.Equal(1, tracker.MaximumActiveProjectiles);
    }

    [Fact]
    public void Lifecycle_tracker_counts_actor_recycle_and_effect_expiry()
    {
        var actors = new FunctionalActorProjectileLifecycleTracker("actor-framework");
        actors.Update(new(
            Frame: 1,
            HeldInputs: [],
            DynamicSprites: [new("actor-0", FunctionalDynamicSpriteKind.Actor, Active: true, ObservedVisible: true)],
            UsedActorSpawns: 2,
            ActiveActorCount: 2));
        actors.Update(new(
            Frame: 2,
            HeldInputs: [],
            DynamicSprites: [new("actor-0", FunctionalDynamicSpriteKind.Actor, Active: false, ObservedVisible: false)],
            UsedActorSpawns: 2,
            ActiveActorCount: 1));

        var effects = new FunctionalActorProjectileLifecycleTracker("runner-projectile");
        effects.Update(new(
            Frame: 1,
            HeldInputs: ["b"],
            DynamicSprites: [],
            Effects: [new(Active: true, Visible: true), new(Active: false, Visible: false)]));
        effects.Update(new(
            Frame: 2,
            HeldInputs: [],
            DynamicSprites: [],
            Effects: [new(Active: false, Visible: false), new(Active: false, Visible: false)]));

        Assert.Equal(2, actors.MaximumUsedActorSpawns);
        Assert.Equal(1, actors.ActorSlotRecycles);
        Assert.Equal(1, effects.ExpiredEffects);
        Assert.Equal(2, effects.HiddenExpiredEffects);
    }

    private static FunctionalActorProjectileLifecycleFrame ProjectileFrame(
        int frame,
        int fireTick,
        bool active,
        bool visible,
        sbyte verticalVelocity) =>
        new(
            Frame: frame,
            HeldInputs: [],
            DynamicSprites: [new("projectile-0", FunctionalDynamicSpriteKind.Projectile, active, visible)],
            FireTick: fireTick,
            Projectiles: [new(active, verticalVelocity)]);
}
