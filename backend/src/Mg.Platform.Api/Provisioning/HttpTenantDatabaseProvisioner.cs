using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Mg.Platform.Api.Provisioning;

/// <summary>
/// Calls a separately deployed mTLS-protected provisioning agent. Cluster-level
/// database credentials never enter the MG Platform API process.
/// </summary>
public sealed class HttpTenantDatabaseProvisioner(HttpClient httpClient) : ITenantDatabaseProvisioner
{
    private const string ProvisioningPath = "internal/v1/tenant-databases";

    public async Task<string> ProvisionAsync(
        TenantDatabaseProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedName = $"mg_tenant_{request.TenantId:N}";
        if (request.TenantId == Guid.Empty
            || request.IdempotencyKey == Guid.Empty
            || !string.Equals(request.DatabaseName, expectedName, StringComparison.Ordinal))
            throw new TenantProvisioningException("TENANT_PROVISIONING_REQUEST_INVALID", permanent: true);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(ProvisioningPath, request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new TenantProvisioningException("TENANT_PROVISIONING_AGENT_UNAVAILABLE");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TenantProvisioningException("TENANT_PROVISIONING_AGENT_UNAVAILABLE");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw MapAgentFailure(response.StatusCode);

            try
            {
                var result = await response.Content.ReadFromJsonAsync<ProvisioningAgentResponse>(
                    cancellationToken: cancellationToken);
                if (string.IsNullOrWhiteSpace(result?.ConnectionString))
                    throw new TenantProvisioningException("TENANT_PROVISIONING_AGENT_RESPONSE_INVALID");
                return result.ConnectionString;
            }
            catch (JsonException)
            {
                throw new TenantProvisioningException("TENANT_PROVISIONING_AGENT_RESPONSE_INVALID");
            }
        }
    }

    public async Task<bool> VerifyAsync(
        string connectionString,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            return false;

        try
        {
            var expectedDatabase = $"mg_tenant_{tenantId:N}";
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (!string.Equals(parsed.Database, expectedDatabase, StringComparison.Ordinal)
                || !string.Equals(parsed.Username, $"{expectedDatabase}_app", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(parsed.Host)
                || string.IsNullOrWhiteSpace(parsed.Password)
                || parsed.SslMode is not (SslMode.VerifyCA or SslMode.VerifyFull))
                return false;

            await using var connection = new NpgsqlConnection(parsed.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT identity.tenant_id,
                       COALESCE(MAX(migration.version), 0),
                       role.rolsuper,
                       role.rolcreatedb,
                       role.rolcreaterole,
                       role.rolreplication,
                       role.rolbypassrls,
                       has_schema_privilege(current_user, 'mg', 'USAGE'),
                       has_schema_privilege(current_user, 'mg', 'CREATE'),
                       has_database_privilege(current_user, current_database(), 'CREATE'),
                       database.datdba = role.oid,
                       role.rolcanlogin,
                       EXISTS (SELECT 1 FROM pg_auth_members membership WHERE membership.member = role.oid OR membership.roleid = role.oid),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'INSERT'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'DELETE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'INSERT'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'DELETE'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'INSERT'),
                       has_table_privilege(current_user, 'mg.audit_events', 'UPDATE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'DELETE'),
                       has_table_privilege(current_user, 'mg.audit_events', 'TRUNCATE'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.tenant_identity', 'TRIGGER'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.schema_migrations', 'TRIGGER'),
                       has_table_privilege(current_user, 'mg.audit_events', 'REFERENCES'),
                       has_table_privilege(current_user, 'mg.audit_events', 'TRIGGER'),
                       role.rolinherit
                FROM mg.tenant_identity AS identity
                LEFT JOIN mg.schema_migrations AS migration ON TRUE
                JOIN pg_roles AS role ON role.rolname = current_user
                JOIN pg_database AS database ON database.datname = current_database()
                WHERE identity.singleton = TRUE
                GROUP BY identity.tenant_id, role.oid, role.rolsuper, role.rolcreatedb,
                         role.rolcreaterole, role.rolreplication, role.rolbypassrls,
                         role.rolcanlogin, role.rolinherit, database.datdba
                """,
                connection);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;

            return reader.GetGuid(0) == tenantId
                && reader.GetInt32(1) >= 1
                && !reader.GetBoolean(2)
                && !reader.GetBoolean(3)
                && !reader.GetBoolean(4)
                && !reader.GetBoolean(5)
                && !reader.GetBoolean(6)
                && reader.GetBoolean(7)
                && !reader.GetBoolean(8)
                && !reader.GetBoolean(9)
                && !reader.GetBoolean(10)
                && reader.GetBoolean(11)
                && !reader.GetBoolean(12)
                && !reader.GetBoolean(13)
                && !reader.GetBoolean(14)
                && !reader.GetBoolean(15)
                && !reader.GetBoolean(16)
                && !reader.GetBoolean(17)
                && !reader.GetBoolean(18)
                && !reader.GetBoolean(19)
                && !reader.GetBoolean(20)
                && !reader.GetBoolean(21)
                && !reader.GetBoolean(22)
                && !reader.GetBoolean(23)
                && !reader.GetBoolean(24)
                && !reader.GetBoolean(25)
                && !reader.GetBoolean(26)
                && !reader.GetBoolean(27)
                && !reader.GetBoolean(28)
                && !reader.GetBoolean(29)
                && !reader.GetBoolean(30)
                && !reader.GetBoolean(31);
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static TenantProvisioningException MapAgentFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500)
            return new TenantProvisioningException("TENANT_PROVISIONING_AGENT_UNAVAILABLE");

        return new TenantProvisioningException(
            statusCode == HttpStatusCode.Conflict
                ? "TENANT_PROVISIONING_IDEMPOTENCY_CONFLICT"
                : "TENANT_PROVISIONING_AGENT_REJECTED",
            permanent: true);
    }

    private sealed record ProvisioningAgentResponse(string? ConnectionString);
}
