namespace RetroSharp.Core.Sdk;

using RetroSharp.Core.Targeting;

public abstract record Sdk2DOperation
{
    public sealed record WaitFrame : Sdk2DOperation;

    public sealed record PollInput : Sdk2DOperation;

    public sealed record DrawLogicalSprite(
        string SpriteId,
        SdkByteExpression X,
        SdkByteExpression Y,
        SdkByteExpression Frame,
        SdkByteExpression? FlipX,
        int PaletteSlot,
        SpriteTransform StaticTransform) : Sdk2DOperation
    {
        public DrawLogicalSprite(
            string SpriteId,
            int X,
            int Y,
            int Frame,
            int PaletteSlot,
            SpriteTransform StaticTransform)
            : this(
                SpriteId,
                new SdkByteExpression.Constant(X),
                new SdkByteExpression.Constant(Y),
                new SdkByteExpression.Constant(Frame),
                null,
                PaletteSlot,
                StaticTransform)
        {
        }
    }

    public sealed record SetCameraPosition(
        SdkWordExpression X,
        SdkWordExpression Y,
        ScrollAxes Axes) : Sdk2DOperation
    {
        public SetCameraPosition(int X, int Y, ScrollAxes Axes)
            : this(new SdkWordExpression.Constant(X), new SdkWordExpression.Constant(Y), Axes)
        {
        }
    }

    public sealed record ApplyCamera(
        ScrollAxes Axes) : Sdk2DOperation;

    public sealed record StreamMapColumn(
        SdkByteExpression TargetColumn,
        SdkByteExpression SourceColumn,
        int Y,
        int Height) : Sdk2DOperation
    {
        public StreamMapColumn(
            int TargetColumn,
            int SourceColumn,
            int Y,
            int Height)
            : this(
                new SdkByteExpression.Constant(TargetColumn),
                new SdkByteExpression.Constant(SourceColumn),
                Y,
                Height)
        {
        }
    }

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

    public sealed record CameraAabbTiles(
        string WorldId,
        SdkByteExpression ScreenX,
        SdkWordExpression WorldY,
        int WorldYOffset,
        SdkAabbExtent Width,
        int Height,
        WorldTileFlags Flags) : Sdk2DOperation
    {
        public CameraAabbTiles(
            string WorldId,
            int ScreenX,
            SdkWordExpression WorldY,
            int Width,
            int Height,
            WorldTileFlags Flags)
            : this(WorldId, new SdkByteExpression.Constant(ScreenX), WorldY, 0, new SdkAabbExtent.Constant(Width), Height, Flags)
        {
        }

        public CameraAabbTiles(
            string WorldId,
            SdkByteExpression ScreenX,
            SdkWordExpression WorldY,
            int Width,
            int Height,
            WorldTileFlags Flags)
            : this(WorldId, ScreenX, WorldY, 0, new SdkAabbExtent.Constant(Width), Height, Flags)
        {
        }
    }

    public sealed record CameraAabbHitTop(
        string WorldId,
        SdkByteExpression ScreenX,
        SdkWordExpression WorldY,
        int WorldYOffset,
        SdkAabbExtent Width,
        int Height,
        WorldTileFlags Flags) : Sdk2DOperation
    {
        public CameraAabbHitTop(
            string WorldId,
            int ScreenX,
            SdkWordExpression WorldY,
            int Width,
            int Height,
            WorldTileFlags Flags)
            : this(WorldId, new SdkByteExpression.Constant(ScreenX), WorldY, 0, new SdkAabbExtent.Constant(Width), Height, Flags)
        {
        }

        public CameraAabbHitTop(
            string WorldId,
            SdkByteExpression ScreenX,
            SdkWordExpression WorldY,
            int Width,
            int Height,
            WorldTileFlags Flags)
            : this(WorldId, ScreenX, WorldY, 0, new SdkAabbExtent.Constant(Width), Height, Flags)
        {
        }
    }

    public sealed record CameraScreenAabbTiles(
        string WorldId,
        SdkByteExpression ScreenX,
        SdkByteExpression ScreenY,
        int ScreenYOffset,
        SdkAabbExtent Width,
        int Height,
        WorldTileFlags Flags) : Sdk2DOperation;

    public sealed record CameraScreenAabbHitTop(
        string WorldId,
        SdkByteExpression ScreenX,
        SdkByteExpression ScreenY,
        int ScreenYOffset,
        SdkAabbExtent Width,
        int Height,
        WorldTileFlags Flags) : Sdk2DOperation;

    public sealed record SetHudTile(
        HudMode Mode,
        int X,
        int Y,
        int Tile) : Sdk2DOperation;
}
