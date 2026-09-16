namespace FaultLedger.Infrastructure;

public sealed class MockProviderSlowResponseGate
{
    private readonly int expectedArrivals;
    private int arrivals;
    private readonly TaskCompletionSource<bool> reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MockProviderSlowResponseGate(int expectedArrivals = 1)
    {
        if (expectedArrivals < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedArrivals));
        }

        this.expectedArrivals = expectedArrivals;
    }

    public Task WaitUntilReachedAsync(CancellationToken cancellationToken) => reached.Task.WaitAsync(cancellationToken);

    public void Release() => released.TrySetResult(true);

    internal async Task WaitForReleaseAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref arrivals) == expectedArrivals)
        {
            reached.TrySetResult(true);
        }

        await released.Task.WaitAsync(cancellationToken);
    }
}
