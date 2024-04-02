using System.Diagnostics.CodeAnalysis;
using ActualLab.CommandR.Operations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Multitenancy;
using ActualLab.OS;

namespace ActualLab.Fusion.EntityFramework.Npgsql.Operations;

#pragma warning disable EF1002

public class NpgsqlDbOperationLogChangeTracker<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext>
    (NpgsqlDbOperationLogChangeTrackingOptions<TDbContext> options, IServiceProvider services)
    : DbOperationCompletionTrackerBase<TDbContext, NpgsqlDbOperationLogChangeTrackingOptions<TDbContext>>(options, services)
    where TDbContext : DbContext
{
    protected override DbOperationCompletionTrackerBase.TenantWatcher CreateTenantWatcher(Symbol tenantId)
        => new TenantWatcher(this, tenantId);

    protected new class TenantWatcher : DbOperationCompletionTrackerBase.TenantWatcher
    {
        public TenantWatcher(NpgsqlDbOperationLogChangeTracker<TDbContext> owner, Symbol tenantId)
            : base(owner.TenantRegistry.Get(tenantId))
        {
            var dbHub = owner.Services.DbHub<TDbContext>();
            var hostId = owner.Services.GetRequiredService<HostId>();

            var watchChain = new AsyncChain($"Watch({tenantId})", async cancellationToken => {
                var dbContext = dbHub.CreateDbContext(Tenant);
                await using var _ = dbContext.ConfigureAwait(false);

                var database = dbContext.Database;
                await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                var dbConnection = (NpgsqlConnection) database.GetDbConnection()!;
                dbConnection.Notification += (_, eventArgs) => {
                    if (eventArgs.Payload != hostId.Id)
                        CompleteWaitForChanges();
                };
                await dbContext.Database
                    .ExecuteSqlRawAsync($"LISTEN {owner.Options.ChannelName}", cancellationToken)
                    .ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested)
                    await dbConnection.WaitAsync(cancellationToken).ConfigureAwait(false);
            }).RetryForever(owner.Options.TrackerRetryDelays, owner.Log);

            _ = watchChain.RunIsolated(StopToken);
        }
    }
}
