namespace RetroSharp.Core.Sdk;

// Builds one target-owned staging RAM range for a WorldPack slot. Each target supplies
// its own concrete range record (for example a work-RAM range or a general-purpose RAM
// range) and decides whether, and how, the zero-based slot index feeds a descriptive
// name; this delegate is the only seam between the shared cursor bookkeeping below and
// that target-owned range type.
public delegate TRange WorldPackStagingSlotFactory<out TRange>(int slotIndex, ushort start, int length);

// Shared incremental-cursor allocator for WorldPack staging RAM layouts. Game Boy and NES
// each reserve one contiguous staging region and hand it out as same-shaped groups of
// slots (a visual cache, a two-entry collision pair, and a two-entry edge pair) using one
// algorithm: advance a cursor by each slot's length in turn, in declaration order. What
// varies is the concrete range type each target's runtime memory layout owns, how many
// bytes a slot needs, and how many slots a group has; a target-owned physical slot size
// (10 bytes on one target's paging scheme, 16 on another's) and any additional fixed
// range a target allocates outside this incremental cursor — such as an optional
// collision memo table — are that target layout's own concern, not this allocator's.
public static class WorldPackStagingLayoutAllocator
{
    public static IReadOnlyList<TRange> CreateSlots<TRange>(
        WorldPackStagingSlotFactory<TRange> createSlot,
        int length,
        int count,
        ref int cursor)
    {
        ArgumentNullException.ThrowIfNull(createSlot);
        var slots = new TRange[count];
        for (var index = 0; index < count; index++)
        {
            slots[index] = createSlot(index, checked((ushort)cursor), length);
            cursor = checked(cursor + length);
        }

        return slots;
    }

    public static IReadOnlyList<TRange> CreatePair<TRange>(
        WorldPackStagingSlotFactory<TRange> createSlot,
        int length,
        ref int cursor)
        => CreateSlots(createSlot, length, 2, ref cursor);

    public static void ValidateIdBytes(int value, string parameterName)
    {
        if (value is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "WorldPack v1 ID width must be one or two bytes.");
        }
    }
}
