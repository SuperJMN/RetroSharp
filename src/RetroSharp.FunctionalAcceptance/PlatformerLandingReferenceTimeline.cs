namespace RetroSharp.FunctionalAcceptance;

/// <summary>
/// Authored, target-neutral mechanics projection for the canonical platformer-landing scenario.
/// It is deliberately independent from emulator memory: the checked-in input timeline and the
/// reviewed physical frames that complete a gameplay tick are its only runtime inputs.
/// </summary>
public sealed class PlatformerLandingReferenceTimeline
{
    private const int WarmUpFrame = 160;
    private const int PlayerWidth = 18;
    private const int StartX = 72;
    private const int StartY = 273;
    private const int FloorStartX = 64;
    private const int FloorTopY = 304;
    private const int FootOffset = 31;
    private const int FallResetY = 336;
    private const int RightWallX = 384;
    private const int DeadZoneLeft = 64;
    private const int DeadZoneRight = 96;
    private const int TakeoffVelocity = -56;
    private const int HeldGravityThreshold = -32;
    private const int HeldGravity = 1;
    private const int ReleasedGravity = 5;
    private const int TerminalVelocity = 69;
    private const int Subpixel = 16;

    private readonly IReadOnlyDictionary<int, PlatformerLandingReferenceState> drawStateByFrame;

    public PlatformerLandingReferenceTimeline(
        FunctionalScenario scenario,
        int cameraY,
        int inputDelayFrames = 0,
        IReadOnlySet<int>? missedGameplayFrames = null,
        IReadOnlySet<int>? doubleGameplayFrames = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (scenario.SampleId != "platformer-landing" || scenario.WarmUpFrames != WarmUpFrame)
        {
            throw new ArgumentException("The reference timeline only supports the canonical platformer-landing scenario.", nameof(scenario));
        }

        missedGameplayFrames ??= new HashSet<int>();
        doubleGameplayFrames ??= new HashSet<int>();
        var state = new MutableState(cameraY);
        var result = new Dictionary<int, PlatformerLandingReferenceState>();
        var lastFrame = checked(scenario.WarmUpFrames + scenario.ObservationFrames);
        var previousButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var frame = scenario.WarmUpFrames + 1; frame <= lastFrame; frame++)
        {
            // The source draws immediately after WaitVBlank and simulates afterward, so retained
            // OAM for this physical frame projects the state completed by the preceding frame.
            result[frame] = state.Snapshot();
            // The reviewed input budget is part of the authored target projection. NES samples
            // the current physical frame; GB exposes the held state to this source loop one frame later.
            var inputFrame = frame - inputDelayFrames;
            var heldButtons = scenario.Inputs
                .Where(input => inputFrame >= input.StartFrame && inputFrame < input.StartFrame + input.DurationFrames)
                .SelectMany(input => input.Buttons)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var gameplayTicks = missedGameplayFrames.Contains(frame)
                ? 0
                : doubleGameplayFrames.Contains(frame) ? 2 : 1;
            for (var tick = 0; tick < gameplayTicks; tick++)
            {
                Advance(state, heldButtons, previousButtons);
                previousButtons = heldButtons;
            }
        }

        drawStateByFrame = result;
    }

    public PlatformerLandingReferenceState ExpectedDrawState(int frame) =>
        drawStateByFrame.TryGetValue(frame, out var state)
            ? state
            : throw new ArgumentOutOfRangeException(nameof(frame), frame, "Frame is outside the platformer-landing observation window.");

    private static void Advance(
        MutableState state,
        IReadOnlySet<string> heldButtons,
        IReadOnlySet<string> previousButtons)
    {
        var previousFootY = state.Y + FootOffset;
        ApplyGravity(state, heldButtons.Contains("A"));
        var footY = state.Y + FootOffset;

        if (state.Grounded)
        {
            if (state.X >= state.SupportProbeX + 8 || state.X + 8 <= state.SupportProbeX)
            {
                state.SupportProbeX = state.X;
                if (!HasFloorSupport(state.X))
                {
                    state.Grounded = false;
                }
            }
        }
        else if (state.VelocityY >= 0
                 && HasFloorSupport(state.X)
                 && previousFootY <= FloorTopY
                 && footY >= FloorTopY)
        {
            state.Y = FloorTopY - FootOffset;
            state.VelocityY = 0;
            state.Grounded = true;
            state.VerticalSubpixel = 0;
            state.JumpActive = false;
        }

        if (state.Y >= FallResetY)
        {
            state.X = state.CameraX + StartX;
            state.Y = StartY;
            state.VelocityY = 0;
            state.Grounded = true;
            state.JumpActive = false;
            state.FlipX = false;
            state.ResetInputLatch = true;
            state.VerticalSubpixel = 0;
            state.SupportProbeX = state.X - 8;
        }

        if (state.ResetInputLatch)
        {
            if (!heldButtons.Contains("LEFT") && !heldButtons.Contains("RIGHT") && !heldButtons.Contains("A"))
            {
                state.ResetInputLatch = false;
            }
        }
        else if (heldButtons.Contains("A") && !previousButtons.Contains("A") && state.Grounded)
        {
            state.VelocityY = TakeoffVelocity;
            state.Grounded = false;
            state.JumpActive = true;
            state.VerticalSubpixel = 0;
        }

        if (!state.ResetInputLatch && heldButtons.Contains("RIGHT"))
        {
            var screenX = state.X - state.CameraX;
            if (state.X < RightWallX - PlayerWidth)
            {
                state.X++;
                if (screenX >= DeadZoneRight)
                {
                    state.CameraX++;
                }
            }
            state.FlipX = false;
        }

        if (!state.ResetInputLatch && heldButtons.Contains("LEFT"))
        {
            var screenX = state.X - state.CameraX;
            state.X--;
            if (screenX <= DeadZoneLeft && state.CameraX > 0)
            {
                state.CameraX--;
            }
            state.FlipX = true;
        }
    }

    private static void ApplyGravity(MutableState state, bool jumpHeld)
    {
        if (state.Grounded)
        {
            return;
        }

        if (state.JumpActive && jumpHeld && state.VelocityY < HeldGravityThreshold)
        {
            state.VelocityY += HeldGravity;
        }
        else
        {
            state.VelocityY = Math.Min(TerminalVelocity, state.VelocityY + ReleasedGravity);
        }

        var motion = state.VerticalSubpixel + state.VelocityY;
        while (motion < 0)
        {
            state.Y--;
            motion += Subpixel;
        }
        while (motion >= Subpixel)
        {
            state.Y++;
            motion -= Subpixel;
        }
        state.VerticalSubpixel = motion;
    }

    private static bool HasFloorSupport(int playerX) =>
        playerX < RightWallX && playerX + PlayerWidth > FloorStartX;

    private sealed class MutableState(int cameraY)
    {
        public int X { get; set; } = StartX;
        public int Y { get; set; } = StartY;
        public int CameraX { get; set; }
        public int CameraY { get; } = cameraY;
        public int VelocityY { get; set; }
        public int VerticalSubpixel { get; set; }
        public int SupportProbeX { get; set; } = StartX;
        public bool Grounded { get; set; } = true;
        public bool JumpActive { get; set; }
        public bool FlipX { get; set; }
        public bool ResetInputLatch { get; set; }

        public PlatformerLandingReferenceState Snapshot() =>
            new(X, Y, CameraX, CameraY, Grounded, FlipX);
    }
}

public sealed record PlatformerLandingReferenceState(
    int PlayerX,
    int PlayerY,
    int CameraX,
    int CameraY,
    bool Grounded,
    bool FlipX);
