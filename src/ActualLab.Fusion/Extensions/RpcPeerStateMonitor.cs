using System.Diagnostics.CodeAnalysis;
using ActualLab.Rpc;

namespace ActualLab.Fusion.Extensions;

public class RpcPeerStateMonitor : WorkerBase
{
    private MutableState<RpcPeerRawState> _rawState = null!;

    protected IServiceProvider Services => RpcHub.Services;
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected Moment Now => RpcHub.Clock.Now;

    public RpcHub RpcHub { get; }
    public RpcPeerRef? PeerRef { get; protected set; }
    public TimeSpan JustConnectedPeriod { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan JustDisconnectedPeriod { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MinReconnectsIn { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ExtraInvalidationDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public IState<RpcPeerRawState> RawState {
        get => _rawState;
        protected set => _rawState = (MutableState<RpcPeerRawState>)value;
    }
    public IState<Moment> LastReconnectDelayCancelledAt { get; protected set; } = null!;
    public IState<RpcPeerState> State { get; protected set; } = null!;

    public RpcPeerStateMonitor(
        IServiceProvider services,
        RpcPeerRef? peerRef,
        bool mustStart = true,
        bool mustCreateStates = true)
    {
        RpcHub = services.RpcHub();
        PeerRef = peerRef;
        if (!mustCreateStates)
            return;

        var stateFactory = services.StateFactory();
        var connectionState = peerRef == null ? null : RpcHub.GetPeer(peerRef).ConnectionState.Value;
        var isConnected = connectionState?.IsConnected() ?? true;

        var initialRawState = isConnected
            ? (RpcPeerRawState)new RpcPeerRawConnectedState(Now)
            : new RpcPeerRawDisconnectedState(Now, default, connectionState?.Error);
        var stateCategory = $"{GetType().Name}.{nameof(RawState)}";
        _rawState = stateFactory.NewMutable(initialRawState, stateCategory);

        stateCategory = $"{GetType().Name}.{nameof(LastReconnectDelayCancelledAt)}";
        LastReconnectDelayCancelledAt = peerRef == null
            ? stateFactory.NewMutable((Moment)default, stateCategory)
            : stateFactory.NewComputed<Moment>(
                FixedDelayer.NextTick,
                ComputeLastReconnectDelayCancelledAtState,
                stateCategory);

        stateCategory = $"{GetType().Name}.{nameof(State)}";
        var initialState = initialRawState.IsConnected
            ? new RpcPeerState(peerRef == null ? RpcPeerStateKind.Connected : RpcPeerStateKind.JustConnected)
            : new RpcPeerState(RpcPeerStateKind.JustDisconnected, connectionState?.Error);
        State = peerRef == null
            ? stateFactory.NewMutable(initialState, stateCategory)
            : stateFactory.NewComputed(initialState, FixedDelayer.NextTick, ComputeState, stateCategory);
        if (mustStart)
            Start();
    }

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        if (State is IDisposable d1)
            d1.Dispose();
        if (LastReconnectDelayCancelledAt is IDisposable d2)
            d2.Dispose();
    }

    public void Start()
    {
        if (PeerRef != null)
            _ = Run();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var peerRef = PeerRef;
        if (peerRef == null) // Always connected
            return;

        while (true) {
            Log.LogInformation("`{PeerRef}`: monitor (re)started", peerRef);
            var peer = RpcHub.GetClientPeer(peerRef);
            var peerCts = cancellationToken.LinkWith(peer.StopToken);
            var peerCancellationToken = peerCts.Token;
            var error = (Exception?)null;
            try {
                // This delay gives some time for peer to connect
                while (true) {
                    peerCancellationToken.ThrowIfCancellationRequested();
                    var connectionState = peer.ConnectionState;
                    var isConnected = connectionState.Value.IsConnected();
                    var nextConnectionStateTask = connectionState.WhenNext(peerCancellationToken);

                    if (isConnected) {
                        _rawState.Value = _rawState.Value.ToConnected(Now);
                        await nextConnectionStateTask.ConfigureAwait(false);
                    }
                    else {
                        var state = _rawState.Value.ToDisconnected(Now, peer.ReconnectsAt.Value, connectionState.Value);
                        _rawState.Value = state;
                        // Disconnected -> update ReconnectsAt value until the nextConnectionStateTask completes
                        var stateChangedToken = CancellationTokenExt.FromTask(nextConnectionStateTask, CancellationToken.None);
                        try {
                            var reconnectAtChanges = peer.ReconnectsAt.Changes(stateChangedToken);
                            await foreach (var reconnectsAt in reconnectAtChanges.ConfigureAwait(false)) {
                                if (state.ReconnectsAt != reconnectsAt)
                                    _rawState.Value = state = state with { ReconnectsAt = reconnectsAt };
                            }
                        }
                        catch (OperationCanceledException) when (stateChangedToken.IsCancellationRequested) {
                            // Intended
                        }
                    }
                }
            }
            catch (Exception e) {
                if (e.IsCancellationOf(cancellationToken)) {
                    Log.LogInformation("`{PeerRef}`: monitor stopped", peerRef);
                    return;
                }

                error = e;
                if (peer.StopToken.IsCancellationRequested)
                    Log.LogWarning("`{PeerRef}`: peer is terminated, will restart", peerRef);
                else
                    Log.LogError(e, "`{PeerRef}`: monitor failed, will restart", peerRef);
            }
            finally {
                _rawState.Value = _rawState.Value.ToDisconnected(Now, default, error);
                peerCts.CancelAndDisposeSilently();
            }
        }
    }

    protected virtual Task<Moment> ComputeLastReconnectDelayCancelledAtState(CancellationToken cancellationToken)
    {
        var reconnectDelayer = RpcHub.InternalServices.ClientPeerReconnectDelayer;
        var computed = Computed.GetCurrent();
        reconnectDelayer.CancelDelaysToken.Register(static c => {
            // It makes sense to wait a bit after the cancellation to let RpcPeer do some work
            _ = Task.Delay(50, CancellationToken.None).ContinueWith(
                _ => (c as Computed)?.Invalidate(),
                TaskScheduler.Default);
        }, computed);
        return Task.FromResult(Now);
    }

    protected virtual async Task<RpcPeerState> ComputeState(CancellationToken cancellationToken)
    {
        var s = await RawState.Use(cancellationToken).ConfigureAwait(false);
        var now = Now;
        if (s is not RpcPeerRawDisconnectedState d) {
            // Connected case
            var connectedFor = now - s.EnteredAt;
            if (connectedFor >= JustConnectedPeriod)
                return new RpcPeerState(RpcPeerStateKind.Connected);

            InvalidateIn(JustConnectedPeriod - connectedFor);
            return new RpcPeerState(RpcPeerStateKind.JustConnected);
        }

        // Disconnected case
        var disconnectedFor = now - d.DisconnectedAt;
        if (disconnectedFor < JustDisconnectedPeriod) {
            InvalidateIn(JustConnectedPeriod - disconnectedFor);
            return new RpcPeerState(RpcPeerStateKind.JustDisconnected, d.LastError);
        }
        var reconnectsIn = d.ReconnectsAt - now;
        if (reconnectsIn < MinReconnectsIn)
            return new RpcPeerState(RpcPeerStateKind.Disconnected, d.LastError);

        // Just to create a dependency that will trigger the recompute
        await LastReconnectDelayCancelledAt.Use(cancellationToken).ConfigureAwait(false);
        InvalidateIn(reconnectsIn - MinReconnectsIn);
        return new RpcPeerState(RpcPeerStateKind.Disconnected, d.LastError, reconnectsIn);
    }

    protected void InvalidateIn(TimeSpan delay)
        => Computed.GetCurrent().Invalidate(delay + ExtraInvalidationDelay);
}
