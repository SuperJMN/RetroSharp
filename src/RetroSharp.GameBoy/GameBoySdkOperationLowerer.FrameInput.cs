namespace RetroSharp.GameBoy;

internal sealed partial class GameBoySdkOperationLowerer
{
    private const byte JoypadDeselect = 0x30;
    private const int JoypadSettleReadCount = 4;

    private static readonly GameBoyButton[] Buttons =
    [
        new(0x01, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart),
        new(0x02, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 1),
        new(0x04, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 2),
        new(0x08, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 3),
        new(0x10, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 4),
        new(0x20, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 5),
        new(0x40, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 6),
        new(0x80, GameBoyRuntimeMemoryLayout.Input.HoldTicksStart + 7),
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

    private readonly record struct GameBoyButton(byte SnapshotMask, ushort HoldTicksAddress);
}
