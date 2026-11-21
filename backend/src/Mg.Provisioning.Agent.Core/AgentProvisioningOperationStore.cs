using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const int ControlPlaneMaintenanceLockClassId = 19783;
    private const int ControlPlaneMaintenanceLockObjectId = 20562;

    private readonly Func<CancellationToken, Task>? afterPreparedCredentialInserted;
    private readonly Func<CancellationToken, Task>? afterCredentialRewrapped;
    private readonly Func<CancellationToken, Task>? afterPhaseUpdated;

    internal AgentProvisioningOperationStore(
        NpgsqlDataSource dataSourceValue,
        AgentCredentialProtector credentialProtectorValue,
        Func<CancellationToken, Task>? afterPreparedCredentialInserted,
        Func<CancellationToken, Task>? afterCredentialRewrapped,
        Func<CancellationToken, Task>? afterPhaseUpdated)
        : this(dataSourceValue, credentialProtectorValue)
    {
        this.afterPreparedCredentialInserted = afterPreparedCredentialInserted;
        this.afterCredentialRewrapped = afterCredentialRewrapped;
        this.afterPhaseUpdated = afterPhaseUpdated;
    }

    private static readonly string[] Phases =
        ["Prepared", "DatabaseCreated", "SchemaApplied", "Verified"];

    public async Task<AgentProvisioningCredentials> GetOrCreatePreparedCredentialsAsync(
        Guid tenantId,
        Guid idempotencyKey,
        string databaseName,
        Func<CancellationToken, ValueTask<AgentProvisioningCredentials>> credentialFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentialFactory);
        ValidateTenant(tenantId, idempotencyKey, databaseName);

        var requestHash = ComputeRequestHash(tenantId, idempotencyKey, databaseName);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);

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
                        throw new AgentProvisioningConflictException(
                            "A conflicting provisioning operation already exists for this tenant.");

                    var stablePayload = credentialProtector.Unprotect(
                        encrypted, tenantId, idempotencyKey, databaseName);
                    var stableCredentials = JsonSerializer.Deserialize<AgentProvisioningCredentials>(stablePayload)
                        ?? throw new InvalidOperationException("Stored tenant credential bundle is invalid.");
                    ValidateCredentials(stableCredentials, tenantId, databaseName);
                    await reader.CloseAsync();

                    if (!string.Equals(encrypted.KeyId, credentialProtector.CurrentKeyId, StringComparison.Ordinal))
                    {
                        await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);
                        var rewrapped = credentialProtector.Protect(
                            stablePayload, tenantId, idempotencyKey, databaseName);
                        try
                        {
                            await using var update = new NpgsqlCommand(
                                """
                                UPDATE mg_agent.provisioning_operations
                                SET credential_key_id = @new_key_id,
                                    credential_nonce = @new_nonce,
                                    credential_ciphertext = @new_ciphertext,
                                    credential_tag = @new_tag,
                                    updated_at = now()
                                WHERE tenant_id = @tenant_id
                                  AND idempotency_key = @idempotency_key
                                  AND credential_key_id = @old_key_id;
                                """, connection, transaction);
                            update.Parameters.AddWithValue("new_key_id", NpgsqlDbType.Text, rewrapped.KeyId);
                            update.Parameters.AddWithValue("new_nonce", NpgsqlDbType.Bytea, rewrapped.Nonce);
                            update.Parameters.AddWithValue("new_ciphertext", NpgsqlDbType.Bytea, rewrapped.Ciphertext);
                            update.Parameters.AddWithValue("new_tag", NpgsqlDbType.Bytea, rewrapped.Tag);
                            update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
                            update.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
                            update.Parameters.AddWithValue("old_key_id", NpgsqlDbType.Text, encrypted.KeyId);
                            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                                throw new InvalidOperationException("Credential key rotation could not update the locked operation.");
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(rewrapped.Nonce);
                            CryptographicOperations.ZeroMemory(rewrapped.Ciphertext);
                            CryptographicOperations.ZeroMemory(rewrapped.Tag);
                        }
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return stableCredentials;
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
                    throw new AgentProvisioningConflictException(
                        "The idempotency key is already assigned to another tenant.");
            }

            // The factory must only generate a credential; it must not perform external side effects.
            var credentials = await credentialFactory(cancellationToken);
            ValidateCredentials(credentials, tenantId, databaseName);
            var payload = JsonSerializer.Serialize(credentials);
            var envelope = credentialProtector.Protect(
                payload, tenantId, idempotencyKey, databaseName);

            await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);
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

            if (afterPreparedCredentialInserted is not null)
                await afterPreparedCredentialInserted(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return credentials;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(requestHash);
        }
    }

    /// <summary>
    /// Re-encrypts a bounded batch of stored credential bundles with the current key.
    /// Keep all historical keys in the configured key ring until this returns zero.
    /// </summary>
    public Task<int> RewrapStoredCredentialsAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        RewrapStoredCredentialsCoreAsync(null, batchSize, cancellationToken);

    internal Task<int> RewrapStoredCredentialsForTenantAsync(
        Guid tenantId,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant identifier must be non-empty.", nameof(tenantId));
        return RewrapStoredCredentialsCoreAsync(tenantId, batchSize, cancellationToken);
    }

    private async Task<int> RewrapStoredCredentialsCoreAsync(
        Guid? tenantId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Credential rewrap batch size must be between 1 and 1000.");

        var rows = new List<(
            Guid TenantId,
            Guid IdempotencyKey,
            string DatabaseName,
            string KeyId,
            byte[] Nonce,
            byte[] Ciphertext,
            byte[] Tag)>();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireControlPlaneMaintenanceTransactionLockAsync(connection, transaction, cancellationToken);
        await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);
        await using (var select = new NpgsqlCommand(
            """
            SELECT tenant_id, idempotency_key, database_name,
                   credential_key_id, credential_nonce,
                   credential_ciphertext, credential_tag
            FROM mg_agent.provisioning_operations
            WHERE credential_key_id <> @current_key_id
              AND (@tenant_id IS NULL OR tenant_id = @tenant_id)
            ORDER BY updated_at
            LIMIT @batch_size
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("current_key_id", NpgsqlDbType.Text, credentialProtector.CurrentKeyId);
            select.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, (object?)tenantId ?? DBNull.Value);
            select.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, batchSize);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<byte[]>(4),
                    reader.GetFieldValue<byte[]>(5),
                    reader.GetFieldValue<byte[]>(6)));
            }
            await reader.CloseAsync();
        }

        try
        {
            foreach (var row in rows)
            {
                var oldEnvelope = new EncryptedTenantCredential(
                    row.KeyId, row.Nonce, row.Ciphertext, row.Tag);
                var payload = credentialProtector.Unprotect(
                    oldEnvelope, row.TenantId, row.IdempotencyKey, row.DatabaseName);
                var credentials = JsonSerializer.Deserialize<AgentProvisioningCredentials>(payload)
                    ?? throw new InvalidOperationException("Stored tenant credential bundle is invalid.");
                ValidateCredentials(credentials, row.TenantId, row.DatabaseName);

                await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);
                var newEnvelope = credentialProtector.Protect(
                    payload, row.TenantId, row.IdempotencyKey, row.DatabaseName);
                try
                {
                    await using var update = new NpgsqlCommand(
                        """
                        UPDATE mg_agent.provisioning_operations
                        SET credential_key_id = @new_key_id,
                            credential_nonce = @new_nonce,
                            credential_ciphertext = @new_ciphertext,
                            credential_tag = @new_tag,
                            updated_at = now()
                        WHERE tenant_id = @tenant_id
                          AND idempotency_key = @idempotency_key
                          AND credential_key_id = @old_key_id;
                        """, connection, transaction);
                    update.Parameters.AddWithValue("new_key_id", NpgsqlDbType.Text, newEnvelope.KeyId);
                    update.Parameters.AddWithValue("new_nonce", NpgsqlDbType.Bytea, newEnvelope.Nonce);
                    update.Parameters.AddWithValue("new_ciphertext", NpgsqlDbType.Bytea, newEnvelope.Ciphertext);
                    update.Parameters.AddWithValue("new_tag", NpgsqlDbType.Bytea, newEnvelope.Tag);
                    update.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, row.TenantId);
                    update.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, row.IdempotencyKey);
                    update.Parameters.AddWithValue("old_key_id", NpgsqlDbType.Text, row.KeyId);
                    if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException("Credential rewrap lost its locked provisioning operation.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(newEnvelope.Nonce);
                    CryptographicOperations.ZeroMemory(newEnvelope.Ciphertext);
                    CryptographicOperations.ZeroMemory(newEnvelope.Tag);
                }

                if (afterCredentialRewrapped is not null)
                    await afterCredentialRewrapped(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return rows.Count;
        }
        finally
        {
            foreach (var row in rows)
            {
                CryptographicOperations.ZeroMemory(row.Nonce);
                CryptographicOperations.ZeroMemory(row.Ciphertext);
                CryptographicOperations.ZeroMemory(row.Tag);
            }
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
        await VerifyLedgerBoundaryAsync(connection, null, cancellationToken);
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
        await AcquireControlPlaneMaintenanceSessionLockAsync(connection, cancellationToken);
        try
        {
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
        finally
        {
            await ReleaseControlPlaneMaintenanceSessionLockAsync(connection);
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await VerifyLedgerBoundaryAsync(connection, transaction, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE mg_agent.provisioning_operations
            SET phase = @next_phase, updated_at = now()
            WHERE tenant_id = @tenant_id
              AND idempotency_key = @idempotency_key
              AND phase = @expected_phase;
            """, connection, transaction);
        command.Parameters.AddWithValue("next_phase", NpgsqlDbType.Text, nextPhase);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Uuid, idempotencyKey);
        command.Parameters.AddWithValue("expected_phase", NpgsqlDbType.Text, expectedPhase);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "Provisioning operation is missing or its phase changed; reconcile actual tenant resources before retrying.");
        if (afterPhaseUpdated is not null)
            await afterPhaseUpdated(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task AcquireControlPlaneMaintenanceSessionLockAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_lock_shared(@class_id, @object_id);", connection);
        command.Parameters.AddWithValue("class_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockClassId);
        command.Parameters.AddWithValue("object_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockObjectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseControlPlaneMaintenanceSessionLockAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock_shared(@class_id, @object_id);", connection);
            command.Parameters.AddWithValue("class_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockClassId);
            command.Parameters.AddWithValue("object_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockObjectId);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (NpgsqlException)
        {
            // Connection disposal below releases the session-level shared lock.
        }
    }

    private static async Task AcquireControlPlaneMaintenanceTransactionLockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock_shared(@class_id, @object_id);", connection, transaction);
        command.Parameters.AddWithValue("class_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockClassId);
        command.Parameters.AddWithValue("object_id", NpgsqlDbType.Integer, ControlPlaneMaintenanceLockObjectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task VerifyLedgerBoundaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                has_schema_privilege(current_user, 'mg_agent', 'USAGE')
                AND has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'SELECT')
                AND has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'INSERT')
                AND has_table_privilege(current_user, 'mg_agent.provisioning_operations', 'UPDATE')
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_database database
                    WHERE database.datname = current_database()
                      AND EXISTS (
                          SELECT 1
                          FROM aclexplode(COALESCE(database.datacl, acldefault('d', database.datdba))) acl
                          WHERE acl.grantee = 0
                            AND acl.privilege_type IN ('CONNECT', 'CREATE', 'TEMPORARY')
                      )
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_namespace schema
                    WHERE schema.nspname = 'mg_agent'
                      AND EXISTS (
                          SELECT 1
                          FROM aclexplode(COALESCE(schema.nspacl, acldefault('n', schema.nspowner))) acl
                          WHERE acl.grantee = 0
                            AND acl.privilege_type IN ('USAGE', 'CREATE')
                      )
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM pg_class ledger
                    JOIN pg_namespace schema ON schema.oid = ledger.relnamespace
                    WHERE schema.nspname = 'mg_agent'
                      AND ledger.relname = 'provisioning_operations'
                      AND EXISTS (
                          SELECT 1
                          FROM aclexplode(COALESCE(ledger.relacl, acldefault('r', ledger.relowner))) acl
                          WHERE acl.grantee = 0
                            AND acl.privilege_type IN
                                ('SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER')
                      )
                );
            """, connection, transaction);
        var allowed = await command.ExecuteScalarAsync(cancellationToken);
        if (allowed is not true)
            throw new InvalidOperationException(
                "Agent control ledger ACL boundary changed; refusing ledger access or mutation.");
    }

    private static void ValidateCredentials(
        AgentProvisioningCredentials credentials, Guid tenantId, string expectedDatabase)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var app = ParseCredential(credentials.ApplicationConnectionString, "application");
        var owner = ParseCredential(credentials.OwnerConnectionString, "owner");

        if (!string.Equals(app.Database, expectedDatabase, StringComparison.Ordinal)
            || !string.Equals(app.Username, $"{expectedDatabase}_app", StringComparison.Ordinal)
            || !string.Equals(owner.Database, expectedDatabase, StringComparison.Ordinal)
            || !string.Equals(owner.Username, $"{expectedDatabase}_owner", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(app.Password)
            || string.IsNullOrWhiteSpace(owner.Password)
            || string.Equals(app.Password, owner.Password, StringComparison.Ordinal)
            || !string.Equals(app.Host, owner.Host, StringComparison.OrdinalIgnoreCase)
            || app.Port != owner.Port
            || app.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull)
            || owner.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException(
                "Provisioning agent credential bundle does not meet immutable tenant role, database and TLS requirements.");
    }

    private static NpgsqlConnectionStringBuilder ParseCredential(string connectionString, string roleKind)
    {
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(parsed.Host)
                || string.IsNullOrWhiteSpace(parsed.Username)
                || string.IsNullOrWhiteSpace(parsed.Password))
                throw new InvalidOperationException("Provisioning agent generated an incomplete " + roleKind + " credential.");
            return parsed;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Provisioning agent generated a malformed " + roleKind + " credential.");
        }
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
