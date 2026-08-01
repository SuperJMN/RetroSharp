namespace RetroSharp.NES;

// Public view of the packed-camera staging shape. Budget reporting and format
// documentation consume these numbers instead of restating the runtime layout.
public static class NesPackedCameraBudget
{
    // A streamed column restores the whole buffered band, so its payload can be
    // taller than the visible screen and than a streamed row.
    public static int MaximumColumnPayloadTiles => NesPackedCameraRuntime.MaximumColumnPayloadLength;

    public static int RowPayloadTiles => NesPackedCameraRuntime.RowPayloadLength;

    public static int MaximumAttributeBytes => NesPackedCameraRuntime.MaximumAttributeBytes;

    public static int EdgeSlotBytes =>
        NesPackedCameraRuntime.AttributeStagingOffset + NesPackedCameraRuntime.MaximumAttributeBytes;

    public static int CurrentStagingBytes => NesRuntimeMemoryLayout.WorldPack.CurrentStagingBytes;

    public static int MaximumStagingBytes => NesRuntimeMemoryLayout.WorldPack.MaximumStagingBytes;
}
