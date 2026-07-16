namespace RetroSharp.FunctionalAcceptance;

public enum FunctionalDynamicSpriteKind
{
    Actor,
    Projectile,
}

public sealed record FunctionalDynamicSpriteLifecycleObservation(
    string Id,
    FunctionalDynamicSpriteKind Kind,
    bool Active,
    bool ObservedVisible);

public sealed record FunctionalProjectileMotionObservation(
    bool Active,
    sbyte VerticalVelocity);

public sealed record FunctionalEffectLifecycleObservation(
    bool Active,
    bool Visible);

public sealed record FunctionalActorContactObservation(
    bool Active,
    int State,
    int WorldY,
    sbyte VerticalVelocity);

public sealed record FunctionalActorProjectileLifecycleFrame(
    int Frame,
    IReadOnlyList<string> HeldInputs,
    IReadOnlyList<FunctionalDynamicSpriteLifecycleObservation> DynamicSprites,
    int? FireTick = null,
    int UsedActorSpawns = 0,
    int ActiveActorCount = 0,
    IReadOnlyList<FunctionalProjectileMotionObservation>? Projectiles = null,
    IReadOnlyList<FunctionalEffectLifecycleObservation>? Effects = null,
    IReadOnlyList<FunctionalActorContactObservation>? Actors = null);

public sealed class FunctionalActorProjectileLifecycleTracker(string sampleId)
{
    private readonly Dictionary<string, DynamicSpriteState> dynamicSprites = new(StringComparer.Ordinal);
    private readonly HashSet<long> completedVisibleSequences = [];
    private readonly List<sbyte> previousProjectileVelocities = [];
    private int previousAttemptCount;
    private int previousFireTick;
    private int previousActiveActorCount;
    private int previousActiveEffectCount;

    public long ActivatedSequence { get; private set; }

    public long VisibleSequence { get; private set; }

    public int MaximumActiveProjectiles { get; private set; }

    public int CurrentActiveProjectiles { get; private set; }

    public int MaximumUsedActorSpawns { get; private set; }

    public int ActorSlotRecycles { get; private set; }

    public int DroppedRequests { get; private set; }

    public int ReusedProjectileSlots { get; private set; }

    public int BounceContacts { get; private set; }

    public int ExpiredEffects { get; private set; }

    public int HiddenExpiredEffects { get; private set; }

    public int MaximumSpawnToVisibleFrames { get; private set; }

    public int CurrentActorTileContacts { get; private set; }

    public int MaximumActorTileContacts { get; private set; }

    public int CurrentGroundedActors { get; private set; }

    public int MaximumGroundedActors { get; private set; }

    public int? MinimumGroundedActorWorldY { get; private set; }

    public int? MaximumGroundedActorWorldY { get; private set; }

    public int? CurrentMinimumGroundedActorWorldY { get; private set; }

    public int? CurrentMaximumGroundedActorWorldY { get; private set; }

    public FunctionalSpawnLifecycleObservation Spawn => new(
        ActivatedSequence == 0 ? null : ActivatedSequence,
        VisibleSequence == 0 ? null : VisibleSequence);

    public void Update(FunctionalActorProjectileLifecycleFrame frame)
    {
        UpdateDynamicSprites(frame);
        UpdateProjectileMaximum(frame.Projectiles ?? []);
        UpdateActorContact(frame.Actors ?? []);

        if (sampleId == "actor-framework")
        {
            UpdateActorPool(frame);
            return;
        }

        UpdateProjectileAttempts(frame);
        UpdateProjectileBounces(frame.Projectiles ?? []);
        UpdateEffectExpiry(frame.Effects ?? []);
    }

    private void UpdateDynamicSprites(FunctionalActorProjectileLifecycleFrame frame)
    {
        foreach (var sprite in frame.DynamicSprites)
        {
            if (!dynamicSprites.TryGetValue(sprite.Id, out var state))
            {
                state = new();
                dynamicSprites.Add(sprite.Id, state);
            }

            if (sprite.Active && !state.PreviousActive)
            {
                ActivatedSequence++;
                state.PendingVisible = (ActivatedSequence, frame.Frame);
                state.ActivationCount++;
                if (sprite.Kind == FunctionalDynamicSpriteKind.Projectile && state.ActivationCount > 1)
                {
                    ReusedProjectileSlots++;
                }
                if (sprite.Kind == FunctionalDynamicSpriteKind.Actor && state.ActivationCount > 1)
                {
                    ActorSlotRecycles++;
                }
            }

            if (sprite.ObservedVisible && !state.PreviousVisible && state.PendingVisible is { } activated)
            {
                state.PendingVisible = null;
                completedVisibleSequences.Add(activated.Sequence);
                while (completedVisibleSequences.Remove(VisibleSequence + 1))
                {
                    VisibleSequence++;
                }
                MaximumSpawnToVisibleFrames = Math.Max(MaximumSpawnToVisibleFrames, frame.Frame - activated.Frame);
            }

            state.PreviousActive = sprite.Active;
            state.PreviousVisible = sprite.ObservedVisible;
        }
    }

    private void UpdateProjectileMaximum(IReadOnlyList<FunctionalProjectileMotionObservation> projectiles)
    {
        CurrentActiveProjectiles = projectiles.Count(projectile => projectile.Active);
        MaximumActiveProjectiles = Math.Max(MaximumActiveProjectiles, CurrentActiveProjectiles);
    }

    private void UpdateActorPool(FunctionalActorProjectileLifecycleFrame frame)
    {
        MaximumUsedActorSpawns = Math.Max(MaximumUsedActorSpawns, frame.UsedActorSpawns);
        if (frame.ActiveActorCount < previousActiveActorCount)
        {
            ActorSlotRecycles += previousActiveActorCount - frame.ActiveActorCount;
        }
        previousActiveActorCount = frame.ActiveActorCount;
    }

    private void UpdateProjectileAttempts(FunctionalActorProjectileLifecycleFrame frame)
    {
        var attempts = previousAttemptCount;
        if (sampleId is "shots-simple" or "shots-bouncy")
        {
            var fireTick = frame.FireTick ?? throw new InvalidOperationException(
                $"Lifecycle frame {frame.Frame} for '{sampleId}' requires the production fire tick.");
            if (frame.Frame > 0 && previousFireTick != 0 && fireTick == 0)
            {
                attempts++;
            }
            previousFireTick = fireTick;
        }
        else
        {
            attempts += frame.HeldInputs.Count(button =>
                button.Equals("a", StringComparison.OrdinalIgnoreCase)
                || button.Equals("b", StringComparison.OrdinalIgnoreCase));
        }

        previousAttemptCount = attempts;
        DroppedRequests = Math.Max(DroppedRequests, attempts - (int)ActivatedSequence);
    }

    private void UpdateProjectileBounces(IReadOnlyList<FunctionalProjectileMotionObservation> projectiles)
    {
        while (previousProjectileVelocities.Count < projectiles.Count)
        {
            previousProjectileVelocities.Add(0);
        }

        for (var index = 0; index < projectiles.Count; index++)
        {
            var projectile = projectiles[index];
            if (sampleId == "shots-bouncy"
                && projectile.Active
                && previousProjectileVelocities[index] >= 0
                && projectile.VerticalVelocity < 0)
            {
                BounceContacts++;
            }
            previousProjectileVelocities[index] = projectile.VerticalVelocity;
        }
    }

    private void UpdateEffectExpiry(IReadOnlyList<FunctionalEffectLifecycleObservation> effects)
    {
        if (sampleId != "runner-projectile")
        {
            return;
        }

        var activeEffects = effects.Count(effect => effect.Active);
        if (activeEffects < previousActiveEffectCount)
        {
            ExpiredEffects += previousActiveEffectCount - activeEffects;
            HiddenExpiredEffects += effects.Count(effect => !effect.Active && !effect.Visible);
        }
        previousActiveEffectCount = activeEffects;
    }

    private void UpdateActorContact(IReadOnlyList<FunctionalActorContactObservation> actors)
    {
        CurrentActorTileContacts = actors.Count(actor => actor.Active && actor.State != 0);
        MaximumActorTileContacts = Math.Max(MaximumActorTileContacts, CurrentActorTileContacts);

        var grounded = actors
            .Where(actor => actor.Active && actor.State != 0 && actor.VerticalVelocity == 0)
            .ToArray();
        CurrentGroundedActors = grounded.Length;
        MaximumGroundedActors = Math.Max(MaximumGroundedActors, CurrentGroundedActors);
        CurrentMinimumGroundedActorWorldY = grounded.Length == 0 ? null : grounded.Min(actor => actor.WorldY);
        CurrentMaximumGroundedActorWorldY = grounded.Length == 0 ? null : grounded.Max(actor => actor.WorldY);
        foreach (var actor in grounded)
        {
            MinimumGroundedActorWorldY = MinimumGroundedActorWorldY is { } minimum
                ? Math.Min(minimum, actor.WorldY)
                : actor.WorldY;
            MaximumGroundedActorWorldY = MaximumGroundedActorWorldY is { } maximum
                ? Math.Max(maximum, actor.WorldY)
                : actor.WorldY;
        }
    }

    private sealed class DynamicSpriteState
    {
        public bool PreviousActive { get; set; }

        public bool PreviousVisible { get; set; }

        public int ActivationCount { get; set; }

        public (long Sequence, int Frame)? PendingVisible { get; set; }
    }
}
