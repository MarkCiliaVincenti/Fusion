using System.Diagnostics.CodeAnalysis;
using ActualLab.OS;
using ActualLab.Rpc.Internal;
using Errors = ActualLab.Internal.Errors;
using UnreferencedCode = ActualLab.Internal.UnreferencedCode;

namespace ActualLab.Rpc.Infrastructure;

public abstract class RpcCallTracker<TRpcCall> : IEnumerable<TRpcCall>
    where TRpcCall : RpcCall
{
    private RpcPeer _peer = null!;
    protected RpcLimits Limits { get; private set; } = null!;
    protected readonly ConcurrentDictionary<long, TRpcCall> Calls
        = new(HardwareInfo.GetProcessorCountFraction(2), 127);

    public RpcPeer Peer {
        get => _peer;
        protected set {
            if (_peer != null)
                throw Errors.AlreadyInitialized(nameof(Peer));

            _peer = value;
            Limits = _peer.Hub.Limits;
        }
    }

    public int Count => Calls.Count;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    // ReSharper disable once NotDisposedResourceIsReturned
    public IEnumerator<TRpcCall> GetEnumerator() => Calls.Values.GetEnumerator();

    public virtual void Initialize(RpcPeer peer)
        => Peer = peer;

    public TRpcCall? Get(long callId)
        => Calls.GetValueOrDefault(callId);
}

public sealed class RpcInboundCallTracker : RpcCallTracker<RpcInboundCall>
{
    public RpcInboundCall GetOrRegister(RpcInboundCall call)
    {
        if (call.NoWait || Calls.TryAdd(call.Id, call))
            return call;

        // We could use this call earlier, but it's more expensive,
        // and we should rarely land here, so we do this separately
        return Calls.GetOrAdd(call.Id, static (_, call1) => call1, call);
    }

    public bool Unregister(RpcInboundCall call)
        // NoWait should always return true here!
        => call.NoWait || Calls.TryRemove(call.Id, call);

    public void Clear()
        => Calls.Clear();
}

public sealed class RpcOutboundCallTracker : RpcCallTracker<RpcOutboundCall>
{
    private readonly ConcurrentDictionary<long, RpcOutboundCall> _inProgressCalls
        = new(HardwareInfo.GetProcessorCountFraction(2), 127);
    private long _lastId;

    public int InProgressCallCount => _inProgressCalls.Count;
    public IEnumerable<RpcOutboundCall> InProgressCalls => _inProgressCalls.Values;

    public void Register(RpcOutboundCall call)
    {
        if (call.NoWait)
            throw new ArgumentOutOfRangeException(nameof(call), "call.NoWait == true.");
        if (call.Id != 0)
            throw new ArgumentOutOfRangeException(nameof(call), "call.Id != 0.");

        while (true) {
            call.Id = Interlocked.Increment(ref _lastId);
            if (Calls.TryAdd(call.Id, call)) {
                // Also register an in-progress call
                call.StartedAt = CpuTimestamp.Now;
                _inProgressCalls.TryAdd(call.Id, call);
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Complete(RpcOutboundCall call)
        => _inProgressCalls.TryRemove(call.Id, call);

    public bool Unregister(RpcOutboundCall call)
        => Calls.TryRemove(call.Id, call);

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public void TryReroute()
    {
        foreach (var call in this)
            if (call.IsPeerChanged())
                call.SetRerouteError();
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public async Task Maintain(RpcHandshake handshake, bool isPeerChanged, CancellationToken cancellationToken)
    {
        await Reconnect(handshake, isPeerChanged, cancellationToken).ConfigureAwait(false);

        // This loop aborts timed out calls every CallTimeoutCheckPeriod
        while (!cancellationToken.IsCancellationRequested) {
            foreach (var call in this) {
                if (call.UntypedResultTask.IsCompleted)
                    continue;

                var timeouts = call.MethodDef.Timeouts;
                var startedAt = call.StartedAt;
                if (startedAt == default)
                    continue; // Something is off: call.StartedAt wasn't set
                if (startedAt.Elapsed <= timeouts.Timeout)
                    continue;

                var error = Internal.Errors.CallTimeout(Peer.Ref, timeouts.Timeout);
                // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                if ((timeouts.TimeoutAction & RpcCallTimeoutAction.Log) != 0)
                    Peer.Log.LogError(error, "{PeerRef}': {Message}", Peer.Ref, error.Message);
                // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                if ((timeouts.TimeoutAction & RpcCallTimeoutAction.Throw) != 0)
                    call.SetError(error, context: null, assumeCancelled: false);
            }
            await Task.Delay(Limits.CallTimeoutCheckPeriod.Next(), cancellationToken).ConfigureAwait(false);
        }
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public async Task Abort(Exception error)
    {
        var abortedCallIds = new HashSet<long>();
        for (int i = 0;; i++) {
            var abortedCallCountBefore = abortedCallIds.Count;
            foreach (var call in this) {
                if (abortedCallIds.Add(call.Id))
                    call.SetError(error, context: null, assumeCancelled: true);
            }
            if (i >= 2 && abortedCallCountBefore == abortedCallIds.Count)
                break;

            await Task.Delay(Limits.CallAbortCyclePeriod).ConfigureAwait(false);
        }
    }

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    private async Task Reconnect(RpcHandshake handshake, bool isPeerChanged, CancellationToken cancellationToken)
    {
        if (isPeerChanged || handshake.ProtocolVersion < 1) {
            // New remote peer or a peer running pre-v1 RPC protocol version
            await Resend(Calls.Values, cancellationToken).ConfigureAwait(false);
            return;
        }

        var completedStages = (
            from call in Calls.Values
            let reconnectStage = call.GetReconnectStage(isPeerChanged)
            where reconnectStage.HasValue
            group call.Id by reconnectStage.GetValueOrDefault() into g
            orderby g.Key
            select g
            ).ToDictionary(x => x.Key, IncreasingSeqCompressor.Serialize);
        if (completedStages.Count == 0) {
            // Nothing to resume via Reconnect method
            await Resend(Calls.Values, cancellationToken).ConfigureAwait(false);
            return;
        }

        Task<byte[]> reconnectTask;
        using (var _ = new RpcOutboundContext(Peer).Activate())
            reconnectTask = Peer.Hub.SystemCallSender.Client.Reconnect(handshake.Index, completedStages, cancellationToken);
        var callsToResendData = await reconnectTask.ConfigureAwait(false);

        var callsToResend = new List<RpcOutboundCall>();
        foreach (var callId in IncreasingSeqCompressor.Deserialize(callsToResendData))
            if (Calls.TryGetValue(callId, out var call))
                callsToResend.Add(call);
        await Resend(callsToResend, cancellationToken).ConfigureAwait(false);

        static async Task Resend(IEnumerable<RpcOutboundCall> calls, CancellationToken cancellationToken) {
            foreach (var call in calls) {
                cancellationToken.ThrowIfCancellationRequested();
                if (call.GetReconnectStage(true) != null)
                    await call.SendRegistered(false).ConfigureAwait(false);
            }
        }
    }
}
