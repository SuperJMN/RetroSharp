namespace RetroSharp.NES;

using RetroSharp.Parser;

internal sealed partial class NesSdkOperationLowerer
{
    internal bool TryEmitCompatibilityValueCall(FunctionCall call)
    {
        switch (call.Name)
        {
            case "__rs_actor_camera_x_lo":
                NesVideoProgram.RequireArity(call, 0);
                builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.X);
                return true;
            case "__rs_actor_camera_x_hi":
                NesVideoProgram.RequireArity(call, 0);
                if (cameraConfig is { } xConfig
                    && Math.Max(0, (xConfig.MapWidth - NesTarget.Capabilities.ScreenTiles.Width) * 8) > byte.MaxValue)
                {
                    if (xConfig.MapWidth <= byte.MaxValue)
                    {
                        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileColumn);
                        for (var i = 0; i < 5; i++)
                        {
                            builder.ShiftRightA();
                        }
                    }
                    else
                    {
                        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.XHigh);
                    }
                }
                else
                {
                    builder.LoadAImmediate(0);
                }

                return true;
            case "__rs_actor_camera_y_lo":
                NesVideoProgram.RequireArity(call, 0);
                if (useFourScreenNametables)
                {
                    builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.Y);
                }
                else
                {
                    builder.LoadAImmediate(0);
                }

                return true;
            case "__rs_actor_camera_y_hi":
                NesVideoProgram.RequireArity(call, 0);
                if (useFourScreenNametables
                    && cameraConfig is { } yConfig
                    && Math.Max(0, (yConfig.MapHeight - NesTarget.Capabilities.ScreenTiles.Height) * 8) > byte.MaxValue)
                {
                    if (yConfig.MapHeight <= byte.MaxValue)
                    {
                        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Camera.TileRow);
                        for (var i = 0; i < 5; i++)
                        {
                            builder.ShiftRightA();
                        }
                    }
                    else
                    {
                        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Camera.YHigh);
                    }
                }
                else
                {
                    builder.LoadAImmediate(0);
                }

                return true;
            default:
                return false;
        }
    }
}
