namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public abstract record Sdk2DOperation
{
    public sealed record WaitFrame : Sdk2DOperation;

    public sealed record PollInput : Sdk2DOperation;

    public sealed record DrawLogicalSprite(
        string SpriteId,
        Size2D LogicalSize,
        int X,
        int Y,
        int Frame,
        int PaletteSlot,
        SpriteTransform Transform) : Sdk2DOperation;

    public sealed record SetCameraPosition(
        SdkByteExpression X,
        SdkByteExpression Y,
        ScrollAxes Axes) : Sdk2DOperation
    {
        public SetCameraPosition(int X, int Y, ScrollAxes Axes)
            : this(new SdkByteExpression.Constant(X), new SdkByteExpression.Constant(Y), Axes)
        {
        }
    }

    public sealed record ApplyCamera(
        ScrollAxes Axes) : Sdk2DOperation;

    public sealed record StreamMapColumn(
        int TargetColumn,
        int SourceColumn,
        int Y,
        int Height) : Sdk2DOperation;

    public sealed record StreamMapRow(
        int TargetRow,
        int SourceRow,
        int X,
        int Width) : Sdk2DOperation;

    public sealed record ReadWorldTile(
        string WorldId,
        SdkByteExpression WorldX,
        SdkByteExpression WorldY) : Sdk2DOperation
    {
        public ReadWorldTile(string WorldId, int WorldX, int WorldY)
            : this(WorldId, new SdkByteExpression.Constant(WorldX), new SdkByteExpression.Constant(WorldY))
        {
        }
    }

    public sealed record ReadWorldTileFlags(
        string WorldId,
        SdkByteExpression WorldX,
        SdkByteExpression WorldY) : Sdk2DOperation
    {
        public ReadWorldTileFlags(string WorldId, int WorldX, int WorldY)
            : this(WorldId, new SdkByteExpression.Constant(WorldX), new SdkByteExpression.Constant(WorldY))
        {
        }
    }

    public sealed record SetHudTile(
        HudMode Mode,
        int X,
        int Y,
        int Tile) : Sdk2DOperation;
}
