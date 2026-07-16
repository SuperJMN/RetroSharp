namespace RetroSharp.GameBoy;

using RetroSharp.Parser;

internal sealed partial class GameBoySdkOperationLowerer
{
    private const byte JoypadDeselect = 0x30;
    private const int JoypadSettleReadCount = 4;

    private static readonly GameBoyButton[] Buttons =
    [
        new("a", 0x10, 0x01, 0x01, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart),
        new("b", 0x10, 0x02, 0x02, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 1),
        new("select", 0x10, 0x04, 0x04, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 2),
        new("start", 0x10, 0x08, 0x08, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 3),
        new("right", 0x20, 0x01, 0x10, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 4),
        new("left", 0x20, 0x02, 0x20, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 5),
        new("up", 0x20, 0x04, 0x40, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 6),
        new("down", 0x20, 0x08, 0x80, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 7),
    ];

    private void EmitWaitFrame()
    {
        if (usesPackedCameraRuntime)
        {
            var wait = builder.CreateLabel("wait_vblank_fresh");
            var done = builder.CreateLabel("wait_vblank_done");
            builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitEnteredVBlank);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xCA, wait);
            builder.XorA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitEnteredVBlank);
            builder.JumpAbsolute(done);
            builder.Label(wait);
            GameBoyRomBuilder.EmitWaitVBlank(builder, builder.CreateLabel("wait_vblank"));
            builder.Label(done);
            if (usesPackedCollisionRuntime)
            {
                builder.LoadA(GameBoyRuntimeMemoryLayout.Collision.GameplayTickCount);
                builder.Emit(0x3C);
                builder.StoreA(GameBoyRuntimeMemoryLayout.Collision.GameplayTickCount);
            }

            builder.Emit(0x40);
            return;
        }

        GameBoyRomBuilder.EmitWaitVBlank(builder, builder.CreateLabel("wait_vblank"));
    }

    internal void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Previous);
        foreach (var button in Buttons)
        {
            builder.StoreA(button.HoldTicksAddress);
        }
    }

    private void EmitPollInput()
    {
        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Previous);

        EmitReadJoypadNibble(0x10);
        builder.LoadBFromA();

        EmitReadJoypadNibble(0x20);
        builder.SwapA();
        builder.OrAFromB();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Input.Current);
        EmitDeselectJoypad();

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    internal void EmitButtonPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_pressed argument 1");
        var pressedLabel = builder.CreateLabel("button_pressed");
        var endLabel = builder.CreateLabel("button_end");

        EmitReadJoypadNibble(button.Selector);
        builder.AndImmediate(button.Mask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        EmitDeselectJoypad();
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        EmitDeselectJoypad();
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitButtonDown(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(GameBoyRuntimeMemoryLayout.Input.Current, ButtonArg(call, "button_down argument 1"));
    }

    internal void EmitButtonJustPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Previous);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    internal void EmitButtonJustReleased(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Previous);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    internal void EmitButtonHoldTicks(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        builder.LoadA(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(ushort address, GameBoyButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");

        builder.LoadA(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private static GameBoyButton ButtonArg(FunctionCall call, string context)
    {
        var argument = call.Parameters.ElementAt(0);

        // A `Button` enum member (e.g. Button.A) is constant-folded to its ordinal,
        // which matches the canonical Buttons order, so resolve it by index.
        if (argument is ConstantSyntax)
        {
            var ordinal = GameBoyVideoProgram.ConstValue(argument, context);
            if (ordinal < 0 || ordinal >= Buttons.Length)
            {
                throw new InvalidOperationException($"Unsupported Game Boy button ordinal '{ordinal}'.");
            }

            return Buttons[ordinal];
        }

        throw new InvalidOperationException($"{context} must be a Button enum member.");
    }

    internal void EmitReadJoypadNibble(byte selector)
    {
        builder.LoadAImmediate(selector);
        builder.StoreHighRamA(0x00);
        for (var i = 0; i < JoypadSettleReadCount; i++)
        {
            builder.LoadHighRamA(0x00);
        }

        builder.ComplementA();
        builder.AndImmediate(0x0F);
    }

    internal void EmitDeselectJoypad()
    {
        builder.LoadAImmediate(JoypadDeselect);
        builder.StoreHighRamA(0x00);
    }

    private void EmitUpdateButtonHoldTicks(GameBoyButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, resetLabel);

        builder.LoadA(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.JumpAbsolute(0xCA, endLabel);
        builder.AddAImmediate(1);
        builder.StoreA(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    private readonly record struct GameBoyButton(string Name, byte Selector, byte Mask, byte SnapshotMask, ushort HoldTicksAddress);
}
