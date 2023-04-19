namespace Stl.Async;

public abstract class AsyncEvent<T>
{
    protected readonly bool RunContinuationsAsynchronously;
    protected readonly TaskCompletionSource<AsyncEvent<T>> WhenNextSource;

    public T Value { get; }

    protected AsyncEvent(T value, bool runContinuationsAsynchronously)
    {
        RunContinuationsAsynchronously = runContinuationsAsynchronously;
        WhenNextSource = TaskCompletionSourceExt.New<AsyncEvent<T>>(runContinuationsAsynchronously);
        Value = value;
    }

    public override string ToString()
        => $"{GetType().GetName()}({Value})";

    public Task<AsyncEvent<T>> WhenNext()
        => WhenNextSource.Task;
}
