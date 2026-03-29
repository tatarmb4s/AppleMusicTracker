using AppleMusicHistory.Core.Models;
using AppleMusicHistory.Infrastructure.Scraping;

namespace AppleMusicHistory.Tests;

public sealed class AppleMusicUiAutomationSnapshotSourceTests
{
    [Fact]
    public async Task NoProcess_ReturnsAppNotRunning()
    {
        var source = new AppleMusicUiAutomationSnapshotSource(new FakeProbe { Process = null }, TimeSpan.FromMilliseconds(50));

        var result = await source.GetCurrentAsync(CancellationToken.None);

        Assert.Equal(AppleMusicSnapshotReadState.AppNotRunning, result.State);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task MissingProcessDuringRead_ReturnsAppNotRunning()
    {
        var probe = new FakeProbe();
        probe.Enqueue(new ArgumentException("Process with an Id of 17712 is not running."));
        probe.IsRunning = false;
        var source = new AppleMusicUiAutomationSnapshotSource(probe, TimeSpan.FromMilliseconds(50));

        var result = await source.GetCurrentAsync(CancellationToken.None);

        Assert.Equal(AppleMusicSnapshotReadState.AppNotRunning, result.State);
        Assert.Contains("17712", result.DiagnosticMessage);
    }

    [Fact]
    public async Task TimeoutDuringRead_ReturnsRecovering()
    {
        var probe = new FakeProbe();
        probe.Enqueue(_ =>
        {
            Thread.Sleep(200);
            return new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.Recovering, DiagnosticMessage: "late");
        });
        var source = new AppleMusicUiAutomationSnapshotSource(probe, TimeSpan.FromMilliseconds(25));

        var result = await source.GetCurrentAsync(CancellationToken.None);

        Assert.Equal(AppleMusicSnapshotReadState.Recovering, result.State);
        Assert.Equal("Timed out while reading Apple Music UI.", result.DiagnosticMessage);
    }

    [Fact]
    public async Task ProbeRecovering_ResetsPauseTracking()
    {
        var probe = new FakeProbe();
        probe.Enqueue(new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.Available, CreateProbeSnapshot(0.5)));
        probe.Enqueue(new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.Recovering, DiagnosticMessage: "resume"));
        probe.Enqueue(new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.Available, CreateProbeSnapshot(0.5)));
        var source = new AppleMusicUiAutomationSnapshotSource(probe, TimeSpan.FromMilliseconds(50));

        var first = await source.GetCurrentAsync(CancellationToken.None);
        var recovering = await source.GetCurrentAsync(CancellationToken.None);
        var second = await source.GetCurrentAsync(CancellationToken.None);

        Assert.Equal(AppleMusicSnapshotReadState.Available, first.State);
        Assert.False(first.Snapshot!.IsPaused);
        Assert.Equal(AppleMusicSnapshotReadState.Recovering, recovering.State);
        Assert.Equal(AppleMusicSnapshotReadState.Available, second.State);
        Assert.False(second.Snapshot!.IsPaused);
    }

    [Fact]
    public async Task NoTrackOutcome_IsPreserved()
    {
        var probe = new FakeProbe();
        probe.Enqueue(new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.NoTrackDetected, DiagnosticMessage: "No active Apple Music track detected."));
        var source = new AppleMusicUiAutomationSnapshotSource(probe, TimeSpan.FromMilliseconds(50));

        var result = await source.GetCurrentAsync(CancellationToken.None);

        Assert.Equal(AppleMusicSnapshotReadState.NoTrackDetected, result.State);
        Assert.Null(result.Snapshot);
    }

    private static AppleMusicProbeSnapshotData CreateProbeSnapshot(double progress)
    {
        return new AppleMusicProbeSnapshotData(
            "Song",
            "Artist",
            "Album",
            "Artist - Album",
            DateTimeOffset.Parse("2026-03-09T12:00:00Z"),
            null,
            progress,
            60,
            240,
            "Lossless",
            "Main Window");
    }

    private sealed class FakeProbe : IAppleMusicUiProbe
    {
        private readonly Queue<object> _reads = new();

        public AppleMusicProcessInfo? Process { get; set; } = new(1234);

        public bool IsRunning { get; set; } = true;

        public AppleMusicProcessInfo? FindProcess() => Process;

        public AppleMusicProbeReadOutcome ReadPlayback(int processId, CancellationToken cancellationToken)
        {
            var next = _reads.Count > 0
                ? _reads.Dequeue()
                : new AppleMusicProbeReadOutcome(AppleMusicProbeReadState.NoTrackDetected);

            return next switch
            {
                AppleMusicProbeReadOutcome outcome => outcome,
                Exception exception => throw exception,
                Func<CancellationToken, AppleMusicProbeReadOutcome> factory => factory(cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported fake probe payload type: {next.GetType().FullName}")
            };
        }

        public bool IsProcessRunning(int processId) => IsRunning;

        public void Enqueue(AppleMusicProbeReadOutcome outcome) => _reads.Enqueue(outcome);

        public void Enqueue(Exception exception) => _reads.Enqueue(exception);

        public void Enqueue(Func<CancellationToken, AppleMusicProbeReadOutcome> read) => _reads.Enqueue(read);
    }
}
