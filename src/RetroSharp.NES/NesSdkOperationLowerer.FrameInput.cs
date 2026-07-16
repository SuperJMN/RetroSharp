namespace RetroSharp.NES;

using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    private const ushort ControllerPortAddress = 0x4016;

    private static readonly NesButton AButton = new("a", 0x01, NesRuntimeMemoryLayout.Input.HoldTicksStart);
    private static readonly NesButton BButton = new("b", 0x02, NesRuntimeMemoryLayout.Input.HoldTicksStart + 1);
    private static readonly NesButton SelectButton = new("select", 0x04, NesRuntimeMemoryLayout.Input.HoldTicksStart + 2);
    private static readonly NesButton StartButton = new("start", 0x08, NesRuntimeMemoryLayout.Input.HoldTicksStart + 3);
    private static readonly NesButton RightButton = new("right", 0x10, NesRuntimeMemoryLayout.Input.HoldTicksStart + 4);
    private static readonly NesButton LeftButton = new("left", 0x20, NesRuntimeMemoryLayout.Input.HoldTicksStart + 5);
    private static readonly NesButton UpButton = new("up", 0x40, NesRuntimeMemoryLayout.Input.HoldTicksStart + 6);
    private static readonly NesButton DownButton = new("down", 0x80, NesRuntimeMemoryLayout.Input.HoldTicksStart + 7);

    private static readonly NesButton[] Buttons =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        RightButton,
        LeftButton,
        UpButton,
        DownButton,
    ];

    private static readonly NesButton[] ControllerReadOrder =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        UpButton,
        DownButton,
        LeftButton,
        RightButton,
    ];

    internal void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Input.Previous);
        foreach (var button in Buttons)
        {
            builder.StoreAZeroPage(button.HoldTicksAddress);
        }
    }

    internal void EmitWaitFrame(bool applyPendingCameraScroll = false)
    {
        if (usePackedCamera)
        {
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
            var pending = builder.CreateLabel("nes_packed_frame_pending");
            builder.Label(pending);
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
            builder.BranchRelative(0xF0, pending);
            builder.DecrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
            var hardwareVBlank = builder.CreateLabel("nes_packed_hardware_vblank");
            builder.Label(hardwareVBlank);
            builder.Emit(0x2C, 0x02, 0x20);
            builder.BranchRelative(0x10, hardwareVBlank);
            if (applyPendingCameraScroll)
            {
                EmitApplyPendingCameraScrollAtVBlank();
            }

            return;
        }

        var clearLabel = builder.CreateLabel("vblank_clear");
        var setLabel = builder.CreateLabel("vblank");
        builder.Label(clearLabel);
        builder.Emit(0x2C, 0x02, 0x20);
        builder.BranchRelative(0x30, clearLabel);
        builder.Label(setLabel);
        builder.Emit(0x2C, 0x02, 0x20);
        builder.BranchRelative(0x10, setLabel);
        if (applyPendingCameraScroll)
        {
            EmitApplyPendingCameraScrollAtVBlank();
        }
    }

    private void EmitPollInput()
    {
        if (usePackedCamera)
        {
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.GameplayTickCount);
        }

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Input.Previous);

        if (usePackedCamera)
        {
            var retry = builder.CreateLabel("nes_input_stable_retry");
            var stable = builder.CreateLabel("nes_input_stable");
            builder.LoadXImmediate(3);
            builder.Label(retry);
            EmitControllerSnapshot(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            EmitControllerSnapshot(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.CompareZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            builder.BranchRelative(0xF0, stable);
            builder.DecrementX();
            builder.JumpIf(0xD0, retry);
            builder.Label(stable);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
            builder.StoreAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        }
        else
        {
            EmitControllerSnapshot(NesRuntimeMemoryLayout.Input.Current);
        }

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    private void EmitControllerSnapshot(byte destination)
    {
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.StoreAZeroPage(destination);

        foreach (var button in ControllerReadOrder)
        {
            EmitReadControllerButton(button, destination);
        }
    }

    private void EmitReadControllerButton(NesButton button, byte destination)
    {
        var skipLabel = builder.CreateLabel("input_button_skip");
        builder.LoadAAbsolute(ControllerPortAddress);
        builder.AndImmediate(0x01);
        builder.BranchRelative(0xF0, skipLabel);
        builder.LoadAZeroPage(destination);
        builder.OrImmediate(button.SnapshotMask);
        builder.StoreAZeroPage(destination);
        builder.Label(skipLabel);
    }

    private void EmitUpdateButtonHoldTicks(NesButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.BranchRelative(0xF0, resetLabel);
        builder.LoadAZeroPage(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.BranchRelative(0xF0, endLabel);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);
        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    internal void EmitButtonDown(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(NesRuntimeMemoryLayout.Input.Current, ButtonArg(call, "button_down argument 1"));
    }

    internal void EmitButtonJustPressed(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Previous);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    internal void EmitButtonJustReleased(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Current);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Input.Previous);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    internal void EmitButtonHoldTicks(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        builder.LoadAZeroPage(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(byte address, NesButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");
        builder.LoadAZeroPage(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, pressedLabel);
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private NesButton ButtonArg(FunctionCall call, string contextName)
    {
        var argument = call.Parameters.ElementAt(0);
        if (argument is ConstantSyntax)
        {
            var ordinal = NesVideoProgram.ConstValue(argument, contextName);
            if (ordinal < 0 || ordinal >= Buttons.Length)
            {
                throw new InvalidOperationException($"Unsupported NES button ordinal '{ordinal}'.");
            }

            return Buttons[ordinal];
        }

        throw new InvalidOperationException($"{contextName} must be a Button enum member.");
    }

    internal readonly record struct NesButton(string Name, byte SnapshotMask, byte HoldTicksAddress);
}
