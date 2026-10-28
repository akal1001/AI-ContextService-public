
using System.Data;
using ContextService.Data.Database;
using ContextService.Models.Entities;
using Microsoft.Data.SqlClient;

namespace ContextService.Data.Repositories;

public class ContextRepository
{
    private readonly SqlConnectionFactory _connectionFactory;

    public ContextRepository(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(ContextEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await using var command = new SqlCommand("dbo.ContextEntry_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = entry.AgentId;
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = entry.SessionId;
        command.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = entry.Content;
        command.Parameters.Add("@ContextType", SqlDbType.NVarChar, 50).Value = entry.ContextType;

        var importance = command.Parameters.Add("@Importance", SqlDbType.Decimal);
        importance.Precision = 5;
        importance.Scale = 4;
        importance.Value = entry.Importance;

        command.Parameters.Add("@TokenCount", SqlDbType.Int).Value = entry.TokenCount;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = (object?)entry.Metadata ?? DBNull.Value;
        command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = (object?)entry.ExpiresAt ?? DBNull.Value;
        command.Parameters.Add("@MemoryScope", SqlDbType.NVarChar, 20).Value = entry.MemoryScope ?? "Session";

        await connection.OpenAsync(cancellationToken);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<ContextEntry>> GetBySessionAsync(string agentId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var entries = new List<ContextEntry>();

        await using var connection = _connectionFactory.CreateConnection();
        await using var command = new SqlCommand("dbo.ContextEntry_GetBySession", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = sessionId;

        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ContextEntry
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                AgentId = reader.GetString(reader.GetOrdinal("AgentId")),
                SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")),
                Content = reader.GetString(reader.GetOrdinal("Content")),
                ContextType = reader.GetString(reader.GetOrdinal("ContextType")),
                Importance = reader.GetDecimal(reader.GetOrdinal("Importance")),
                TokenCount = reader.GetInt32(reader.GetOrdinal("TokenCount")),
                Metadata = reader.IsDBNull(reader.GetOrdinal("Metadata")) ? null : reader.GetString(reader.GetOrdinal("Metadata")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                LastAccessedAt = reader.IsDBNull(reader.GetOrdinal("LastAccessedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastAccessedAt")),
                ExpiresAt = reader.IsDBNull(reader.GetOrdinal("ExpiresAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                MemoryScope = reader.GetString(reader.GetOrdinal("MemoryScope")),
                SupersededById = reader.IsDBNull(reader.GetOrdinal("SupersededById"))? null: reader.GetInt64(reader.GetOrdinal("SupersededById"))
            });
        }

        return entries;
    }

    public async Task<List<ContextEntry>> GetLongTermByAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var entries = new List<ContextEntry>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_GetLongTermByAgent", connection);

        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;

        await connection.OpenAsync(cancellationToken);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(MapContextEntry(reader));
        }

        return entries;
    }

    public async Task<ContextEntry?> GetByIdAsync(long id, string agentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_GetById", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;

        await connection.OpenAsync(cancellationToken);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? MapContextEntry(reader) : null;
    }

    public async Task<bool> UpdateAsync(ContextEntry entry, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_Update", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = entry.Id;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = entry.AgentId;
        command.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = entry.Content;
        command.Parameters.Add("@TokenCount", SqlDbType.Int).Value = entry.TokenCount;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = (object?)entry.Metadata ?? DBNull.Value;

        var importance = command.Parameters.Add("@Importance", SqlDbType.Decimal);
        importance.Precision = 5;
        importance.Scale = 4;
        importance.Value = entry.Importance;

        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task<bool> DeactivateAsync(long id, string agentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_Deactivate", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;

        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<ContextEntry>> GetByAgentAsync(string agentId, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var entries = new List<ContextEntry>();

        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_GetByAgent", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;
        command.Parameters.Add("@MaxResults", SqlDbType.Int).Value = maxResults;

        await connection.OpenAsync(cancellationToken);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            entries.Add(MapContextEntry(reader));

        return entries;
    }

    public async Task SupersedeAsync(long oldEntryId, long newEntryId, string agentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_Supersede", connection);

        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@OldEntryId", SqlDbType.BigInt).Value = oldEntryId;
        command.Parameters.Add("@NewEntryId", SqlDbType.BigInt).Value = newEntryId;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = agentId;

        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ContextEntry MapContextEntry(SqlDataReader reader)
    {
        return new ContextEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            AgentId = reader.GetString(reader.GetOrdinal("AgentId")),
            SessionId = reader.GetGuid(reader.GetOrdinal("SessionId")),
            Content = reader.GetString(reader.GetOrdinal("Content")),
            ContextType = reader.GetString(reader.GetOrdinal("ContextType")),
            Importance = reader.GetDecimal(reader.GetOrdinal("Importance")),
            TokenCount = reader.GetInt32(reader.GetOrdinal("TokenCount")),
            Metadata = reader.IsDBNull(reader.GetOrdinal("Metadata")) ? null : reader.GetString(reader.GetOrdinal("Metadata")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            LastAccessedAt = reader.IsDBNull(reader.GetOrdinal("LastAccessedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("LastAccessedAt")),
            ExpiresAt = reader.IsDBNull(reader.GetOrdinal("ExpiresAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ExpiresAt")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            MemoryScope = reader["MemoryScope"].ToString(),
            SupersededById = reader["SupersededById"] == DBNull.Value? null: Convert.ToInt64(reader["SupersededById"])};
    }

    public async Task<long> InsertAndSupersedeAsync(long oldEntryId, ContextEntry newEntry, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = new SqlCommand("dbo.ContextEntry_InsertAndSupersede", connection);

        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.Add("@OldEntryId", SqlDbType.BigInt).Value = oldEntryId;
        command.Parameters.Add("@AgentId", SqlDbType.NVarChar, 100).Value = newEntry.AgentId;
        command.Parameters.Add("@SessionId", SqlDbType.UniqueIdentifier).Value = newEntry.SessionId;
        command.Parameters.Add("@Content", SqlDbType.NVarChar, -1).Value = newEntry.Content;
        command.Parameters.Add("@ContextType", SqlDbType.NVarChar, 50).Value = newEntry.ContextType;
        command.Parameters.Add("@Importance", SqlDbType.Decimal).Value = newEntry.Importance;
        command.Parameters.Add("@TokenCount", SqlDbType.Int).Value = newEntry.TokenCount;
        command.Parameters.Add("@Metadata", SqlDbType.NVarChar, -1).Value = (object?)newEntry.Metadata ?? DBNull.Value;
        command.Parameters.Add("@ExpiresAt", SqlDbType.DateTime2).Value = (object?)newEntry.ExpiresAt ?? DBNull.Value;
        command.Parameters.Add("@MemoryScope", SqlDbType.NVarChar, 20).Value = newEntry.MemoryScope ?? "Session";

        await connection.OpenAsync(cancellationToken);

        object? result = await command.ExecuteScalarAsync(cancellationToken);

        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException("The stored procedure did not return a new memory ID.");
        }

        return Convert.ToInt64(result);
    }


}
