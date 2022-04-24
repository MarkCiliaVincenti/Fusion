using Stl.OS;

namespace Stl.Async;

public abstract class BatchProcessorBase<TIn, TOut> : WorkerBase
{
    public const int DefaultCapacity = 4096;
    public int ConcurrencyLevel { get; set; } = HardwareInfo.GetProcessorCountPo2Factor();
    public int MaxBatchSize { get; set; } = 256;
    public Func<CancellationToken, Task>? BatchingDelayTaskFactory { get; set; }
    protected Channel<BatchItem<TIn, TOut>> Queue { get; }

    protected BatchProcessorBase(int capacity = DefaultCapacity)
        : this(new BoundedChannelOptions(capacity)) { }
    protected BatchProcessorBase(BoundedChannelOptions options)
        : this(Channel.CreateBounded<BatchItem<TIn, TOut>>(options)) { }
    protected BatchProcessorBase(Channel<BatchItem<TIn, TOut>> queue)
        => Queue = queue;

    public async Task<TOut> Process(TIn input, CancellationToken cancellationToken = default)
    {
        Start();
        var outputTask = TaskSource.New<TOut>(false).Task;
        var batchItem = new BatchItem<TIn, TOut>(input, cancellationToken, outputTask);
        await Queue.Writer.WriteAsync(batchItem, cancellationToken).ConfigureAwait(false);
        return await outputTask.ConfigureAwait(false);
    }

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var readLock = Queue;
        var concurrencyLevel = ConcurrencyLevel;
        var maxBatchSize = MaxBatchSize;

        async Task Worker()
        {
            var reader = Queue.Reader;
            var batch = new List<BatchItem<TIn, TOut>>(maxBatchSize);
            while (!cancellationToken.IsCancellationRequested) {
                lock (readLock) {
                    while (batch.Count < maxBatchSize && reader.TryRead(out var item))
                        batch.Add(item);
                }
                if (batch.Count == 0) {
                    await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                    if (BatchingDelayTaskFactory != null)
                        await BatchingDelayTaskFactory(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                try {
                    await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);
                }
                finally {
                    batch.Clear();
                }
            }
        }

        var workerTasks = new Task[concurrencyLevel];
        for (var i = 0; i < concurrencyLevel; i++)
            workerTasks[i] = Task.Run(Worker, cancellationToken);
        return Task.WhenAll(workerTasks);
    }

    protected abstract Task ProcessBatch(List<BatchItem<TIn, TOut>> batch, CancellationToken cancellationToken);
}

public class BatchProcessor<TIn, TOut> : BatchProcessorBase<TIn, TOut>
{
    public Func<List<BatchItem<TIn, TOut>>, CancellationToken, Task> Implementation { get; set; } =
        (_, _) => throw new NotSupportedException("Set the delegate property to make it work.");

    public BatchProcessor(int capacity = DefaultCapacity) : base(capacity) { }
    public BatchProcessor(BoundedChannelOptions options) : base(options) { }
    public BatchProcessor(Channel<BatchItem<TIn, TOut>> queue) : base(queue) { }

    protected override async Task ProcessBatch(List<BatchItem<TIn, TOut>> batch, CancellationToken cancellationToken)
    {
        try {
            await Implementation.Invoke(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            if (!cancellationToken.IsCancellationRequested)
                cancellationToken = new CancellationToken(canceled: true);
            foreach (var item in batch)
                item.TryCancel(cancellationToken);
            throw;
        }
        catch (Exception e) {
            var result = Result.Error<TOut>(e);
            foreach (var item in batch)
                item.SetResult(result, cancellationToken);
            throw;
        }
    }
}