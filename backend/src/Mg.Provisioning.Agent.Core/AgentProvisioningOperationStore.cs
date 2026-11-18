using System.Security.Cryptography;
using System.Text;
using Mg.Provisioning.Agent.Core;
using Npgsql;
using NpgsqlTypes;

namespace Mg.Provisioning.Agent.Core;

/// <summary>
/// Durable idempotency ledger for the isolated provisioning agent.
/// Call GetOrCreate before performing any tenant database or role side effect.
/// </summary>
public sealed class AgentProvisioningOperationStore(
    NpgsqlDataSource dataSource,
    AgentCredentialProtector credentialProtector)
{
    private static readonly string[] Phases =
        ["Prepared", "DatabaseCreated", "SchemaApplied", "Verified"];

    public async Task<string> GetOrCreatePreparedCredentialAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName,
        Func<CancellationToken, ValueTask<string>> credentialFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ValidateTenant(tenantId, idempotencyKey, databaseName);

        var requestHash = ComputeRequestHash(tenantId, idempotencyKey, databaseName);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var advisoryLock = new NpgsqlCommand(
                """
                SELECT pg_advisory_xact_lock(hashtextextended('tenant:' || @tenant_id::text, 0));
                SELECT pg_advisory_xact_lock(hashtextextended('idempotency:' || @idempotency_key::text, 0));
                """, connection, transaction))
            {
                advisoryLock.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                advisoryLock.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
                await advisoryLock.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var existing = new NpgsqlCommand(
                """
                SELECT idempotency_key, database_name, request_hash,
                       credential_key_id, credential_nonce,
                       credential_ciphertext, credential_tag
                FROM mg_agent.provisioning_operations
                WHERE tenant_id = @tenant_id
                FOR UPDATE;
                """, connection, transaction))
            {
                existing.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    var storedIdempotencyKey = reader.GetGuid(0);
                    var storedDatabaseName = reader.GetString(1);
                    var storedRequestHash = reader.GetFieldValue<byte[]>(2);
                    var encrypted = new EncryptedTenantCredential(
                        reader.GetString(3),
                        reader.GetFieldValue<byte[]>(4),
                        reader.GetFieldValue<byte[]>(5),
                        reader.GetFieldValue<byte[]>(6));

                    if (storedIdempotencyKey != idempotencyKey
                        || !string.Equals(storedDatabaseName, databaseName, StringComparison.Ordinal)
                        || !CryptographicOperations.FixedTimeEquals(storedRequestHash, requestHash))
                        throw new InvalidOperationException(
                            "A conflicting provisioning operation already exists for this tenant.");

                    var stableCredential = credentialProtector.Unprotect(
                        encrypted, tenantId, idempotencyKey, databaseName);
                    await reader.CloseAsync();
                    await transaction.CommitAsync(cancellationToken);
                    return stableCredential;
                }
            }

            // Also reject reuse of an idempotency key for a different tenant before insert.
            await using (var reusedKey = new NpgsqlCommand(
                """
                SELECT tenant_id
                FROM mg_agent.provisioning_operations
                WHERE idempotency_key = @idempotency_key;
                """, connection, transaction))
            {
                reusedKey.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
                var conflictingTenant = await reusedKey.ExecuteScalarAsync(cancellationToken);
                if (conflictingTenant is Guid otherTenant && otherTenant != tenantId)
                    throw new InvalidOperationException(
                        "The idempotency key is already assigned to another tenant.");
            }

            // The factory must only generate a credential; it must not perform external side effects.
            var connectionString = await credentialFactory(cancellationToken);
            ValidateApplicationConnectionString(connectionString, tenantId, databaseName);
            var envelope = credentialProtector.Protect(
                connectionString, tenantId, idempotencyKey, databaseName);

            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO mg_agent.provisioning_operations
                (
                    tenant_id, idempotency_key, database_name, request_hash, phase,
                    credential_key_id, credential_nonce, credential_ciphertext, credential_tag
                )
                VALUES
                (
                    @tenant_id, @idempotency_key, @database_name, @request_hash, 'Prepared',
                    @key_id, @nonce, @ciphertext, @tag
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                insert.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
                insert.Parameters.AddWithValue("database_name", NpgsqlDbType.Text, databaseName);
                insert.Parameters.AddWithValue("request_hash", NpgsqlDbType.Bytea, requestHash);
                insert.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, envelope.KeyId);
                insert.Parameters.AddWithValue("nonce", NpgsqlDbType.Bytea, envelope.Nonce);
                insert.Parameters.AddWithValue("ciphertext", NpgsqlDbType.Bytea, envelope.Ciphertext);
                insert.Parameters.AddWithValue("tag", NpgsqlDbType.Bytea, envelope.Tag);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return connectionString;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(requestHash);
        }
    }

    public async Task<string> GetPhaseAsync(
        Guid tenantId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT phase
            FROM mg_agent.provisioning_operations
            WHERE tenant_id = @tenant_id AND idempotency_key = @idempotency_key;
            """, connection);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
        var phase = await command.ExecuteScalarAsync(cancellationToken);
        return phase as string
            ?? throw new InvalidOperationException("Provisioning operation does not exist for the requested tenant and idempotency key.");
    }

    public async Task<AgentExclusiveExecutionResult<T>> TryExecuteTenantExclusiveAsync<T>(
        Guid tenantId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant identifier must be non-empty.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(action);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string lockSql = "SELECT pg_try_advisory_lock(hashtextextended(@tenant_id::text, 0));";
        await using (var acquire = new NpgsqlCommand(lockSql, connection))
        {
            acquire.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
            var acquired = await acquire.ExecuteScalarAsync(cancellationToken);
            if (acquired is not true)
                return new AgentExclusiveExecutionResult<T>(false, default);
        }

        try
        {
            var value = await action(cancellationToken);
            return new AgentExclusiveExecutionResult<T>(true, value);
        }
        finally
        {
            // If cancellation or a connection failure prevents unlock, disposing the session
            // releases PostgreSQL's session-level advisory lock.
            try
            {
                await using var release = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(hashtextextended(@tenant_id::text, 0));", connection);
                release.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                await release.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (NpgsqlException)
            {
                // Connection disposal below is the final lock-release guarantee.
            }
        }
    }

    public async Task AdvancePhaseAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string expectedPhase,
        string nextPhase,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");

        var expectedIndex = Array.IndexOf(Phases, expectedPhase);
        var nextIndex = Array.IndexOf(Phases, nextPhase);
        if (expectedIndex < 0 || nextIndex != expectedIndex + 1)
            throw new ArgumentException("Provisioning phase transitions must advance exactly one step.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE mg_agent.provisioning_operations
            SET phase = @next_phase, updated_at = now()
            WHERE tenant_id = @tenant_id
              AND idempotency_key = @idempotency_key
              AND phase = @expected_phase;
            """, connection);
        command.Parameters.AddWithValue("next_phase", NpgsqlDbType.Text, nextPhase);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
        command.Parameters.AddWithValue("expected_phase", NpgsqlDbType.Text, expectedPhase);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "Provisioning operation is missing or its phase changed; reconcile actual tenant resources before retrying.");
    }

    private static void ValidateApplicationConnectionString(string connectionString, Guid tenantId, string expectedDatabase)
    {
        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Provisioning agent generated an invalid application connection string.");
        }

        if (!string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal)
            || !string.Equals(parsed.Username, $"mg_tenant_{tenantId:N}_app", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Password)
            || parsed.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException(
                "Provisioning agent application credential does not meet tenant database identity and TLS requirements.");
    }

    private static byte[] ComputeRequestHash(Guid tenantId, Guid idempotencyKey, string databaseName) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"mg-agent-request:v1\n{tenantId:N}\n{idempotencyKey:N}\n{databaseName}"));

    private static void ValidateTenant(Guid tenantId, Guid idempotencyKey, string databaseName)
    {
        if (tenantId == Guid.Empty || idempotencyKey == Guid.Empty)
            throw new ArgumentException("Tenant and idempotency identifiers must be non-empty.");
        if (!string.Equals(databaseName, $"mg_tenant_{tenantId:N}", StringComparison.Ordinal))
            throw new ArgumentException("Database name must be derived from the immutable tenant UUID.");
    }
}

public sealed record AgentExclusiveExecutionResult<T>(bool Acquired, T? Value);
