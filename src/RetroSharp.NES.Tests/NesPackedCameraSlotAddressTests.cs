namespace RetroSharp.NES.Tests;

using Xunit;

/// <summary>
/// Issue #554: NesPackedCameraRuntime.SlotMetadata was migrated to the Start + slot*stride
/// pattern because Slot0/Slot1 in NesRuntimeMemoryLayout.PackedCamera are laid out contiguously
/// with a SlotMetadataBytes stride. Each expected address is the individually named layout
/// member, so a wrong stride or start constant makes the assertion fail.
/// </summary>
public sealed class NesPackedCameraSlotAddressTests
{
    [Fact]
    public void SlotMetadata_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(NesRuntimeMemoryLayout.PackedCamera.Slot0, NesPackedCameraRuntime.SlotMetadata(0));
        Assert.Equal(NesRuntimeMemoryLayout.PackedCamera.Slot1, NesPackedCameraRuntime.SlotMetadata(1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SlotMetadata_still_rejects_an_out_of_range_slot(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NesPackedCameraRuntime.SlotMetadata(slot));
    }
}
