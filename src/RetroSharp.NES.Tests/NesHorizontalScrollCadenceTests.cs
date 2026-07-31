namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesHorizontalScrollCadenceTests
{
    [Theory]
    [InlineData("hscroll-offset.rs", 220, 190, 96)]
    [InlineData("hscroll-full.rs", 2_580, 2_525, 2_240)]
    public void Horizontal_camera_reversal_retains_one_pixel_physical_cadence(
        string sourceFile,
        int totalFrames,
        int traceStart,
        int maximumCameraX)
    {
        var sourcePath = RepositoryFile($"samples/tiled-hscroll/{sourceFile}");
        var directory = Path.GetDirectoryName(sourcePath)
                        ?? throw new InvalidOperationException("Could not locate the horizontal-scroll sample.");
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            File.ReadAllText(sourcePath),
            directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cameraX = build.Report.UserVariables.Single(variable => variable.Name == "cameraX").Address;
        var cpu = new NesTestCpu(build.Rom);
        var frames = new List<ReversalFrame>();

        for (var frame = 0; frame < totalFrames; frame++)
        {
            cpu.RunFrames(cpu.PhysicalFrames + 1);
            if (frame >= traceStart)
            {
                frames.Add(new ReversalFrame(
                    frame,
                    cpu.ScrollX + ((cpu.PpuControl & 0x01) != 0 ? 256 : 0),
                    Word(cpu, cameraX),
                    CameraWord(
                        cpu,
                        NesRuntimeMemoryLayout.Camera.X,
                        NesRuntimeMemoryLayout.Camera.XHigh),
                    Word(cpu, NesRuntimeMemoryLayout.PackedCamera.VisibleCameraXLow),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PrefetchedColumnDirection),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.PendingAxes),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.Slot0 + NesPackedCameraRuntime.StateOffset),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.Slot1 + NesPackedCameraRuntime.StateOffset),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.RequestCount),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.ResidentCount),
                    cpu.Ram(NesRuntimeMemoryLayout.PackedCamera.CommitCount)));
            }
        }

        Assert.All(
            frames,
            frame => Assert.InRange(frame.LogicalX, 0, maximumCameraX));
        var cadence = frames.Zip(frames.Skip(1), (previous, current) => (previous, current)).ToArray();
        Assert.All(
            cadence,
            pair => Assert.True(
                Math.Min(
                    (pair.current.HardwareX - pair.previous.HardwareX + 512) % 512,
                    (pair.previous.HardwareX - pair.current.HardwareX + 512) % 512) <= 1,
                $"Physical X changed by more than one pixel: {Trace(frames)}"));
    }

    private static int Word(NesTestCpu cpu, ushort lowAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(checked((ushort)(lowAddress + 1))) << 8;

    private static int CameraWord(NesTestCpu cpu, byte lowAddress, ushort highAddress) =>
        cpu.Ram(lowAddress) | cpu.Ram(highAddress) << 8;

    private static string RepositoryFile(string relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, relativePath);
    }

    private static string Trace(IEnumerable<ReversalFrame> frames) =>
        string.Join(
            "; ",
            frames.Select(frame =>
                $"{frame.Frame}:hw{frame.HardwareX}/req{frame.RequestedX}/log{frame.LogicalX}"
                + $"/vis{frame.VisibleX}/latch{frame.ColumnLatch:X2}/pending{frame.PendingAxes}"
                + $"/slots{frame.Slot0State},{frame.Slot1State}"
                + $"/life{frame.Requests},{frame.Residents},{frame.Commits}"));

    private sealed record ReversalFrame(
        int Frame,
        int HardwareX,
        int RequestedX,
        int LogicalX,
        int VisibleX,
        int ColumnLatch,
        int PendingAxes,
        int Slot0State,
        int Slot1State,
        int Requests,
        int Residents,
        int Commits);
}
