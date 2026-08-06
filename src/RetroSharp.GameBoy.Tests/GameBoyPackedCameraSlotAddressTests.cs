namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

/// <summary>
/// Issue #554: the packed-camera slot address resolvers must keep returning the exact address
/// named in <see cref="GameBoyRuntimeMemoryLayout"/> for every valid slot, whether they compute it
/// with the Start + slot arithmetic pattern or with an explicit per-slot table. Each expected
/// address below is the individually named layout member (never a re-derivation of the same
/// Start + slot formula under test), so a wrong stride, a wrong start constant, or an off-by-one
/// slot offset makes the corresponding assertion fail.
/// </summary>
public sealed class GameBoyPackedCameraSlotAddressTests
{
    [Fact]
    public void VisualCacheValidAddress_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheValid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(0));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1Valid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(1));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2Valid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(2));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3Valid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(3));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4Valid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(4));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5Valid, GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(5));
    }

    [Fact]
    public void VisualCacheChunkLowAddress_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(0));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1ChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(1));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2ChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(2));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3ChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(3));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4ChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(4));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5ChunkLow, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkLowAddress(5));
    }

    [Fact]
    public void VisualCacheChunkHighAddress_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCacheChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(0));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1ChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(1));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2ChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(2));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3ChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(3));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4ChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(4));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5ChunkHigh, GameBoyWorldPackRuntimeEmitter.VisualCacheChunkHighAddress(5));
    }

    // This table was migrated to the Start + slot arithmetic pattern because
    // VisualCache0Age..VisualCache5Age are contiguous; it is the regression this issue is really
    // about, so each slot is checked against its own individually named constant.
    [Fact]
    public void VisualCacheAgeAddress_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache0Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(0));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache1Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(1));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache2Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(2));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache3Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(3));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache4Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(4));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.VisualCache5Age, GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(5));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    public void VisualCacheAgeAddress_still_rejects_an_out_of_range_slot(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBoyWorldPackRuntimeEmitter.VisualCacheAgeAddress(slot));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void VisualCacheValidAddress_still_rejects_an_out_of_range_slot(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBoyWorldPackRuntimeEmitter.VisualCacheValidAddress(slot));
    }

    // Camera prepare/commit metadata: this table was migrated to the Start + slot*stride pattern
    // because Slot0/Slot1 are laid out contiguously with a SlotMetadataBytes stride.
    [Fact]
    public void PackedCamera_SlotMetadata_matches_the_named_layout_member_for_every_valid_slot()
    {
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.Slot0, GameBoyPackedCameraRuntime.SlotMetadata(0));
        Assert.Equal(GameBoyRuntimeMemoryLayout.PackedCamera.Slot1, GameBoyPackedCameraRuntime.SlotMetadata(1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PackedCamera_SlotMetadata_still_rejects_an_out_of_range_slot(int slot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameBoyPackedCameraRuntime.SlotMetadata(slot));
    }
}
