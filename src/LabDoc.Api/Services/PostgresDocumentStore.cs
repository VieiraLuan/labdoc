using Npgsql;
using LabDoc.Api.DTOs.Response;
using LabDoc.Api.Interfaces;
using LabDoc.Api.Models;

namespace LabDoc.Api.Services;

public sealed class PostgresDocumentStore : IDocumentStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS documents (
            id               uuid PRIMARY KEY,
            file_name        text        NOT NULL,
            content_hash     text        NOT NULL,
            description      text        NULL,
            source_system    text        NULL,
            character_count  integer     NOT NULL,
            chunk_count      integer     NOT NULL,
            embedding_model  text        NOT NULL,
            collection_name  text        NOT NULL,
            chunk_size       integer     NOT NULL DEFAULT 0,
            chunk_overlap    integer     NOT NULL DEFAULT 0,
            status           text        NOT NULL,
            created_at       timestamptz NOT NULL DEFAULT now(),
            updated_at       timestamptz NOT NULL DEFAULT now()
        );

        -- CREATE TABLE IF NOT EXISTS does not evolve an existing table: new columns
        -- must be added explicitly. In production this would be a migration.
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS chunk_size    integer NOT NULL DEFAULT 0;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS chunk_overlap integer NOT NULL DEFAULT 0;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS full_text     text    NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_content_hash
            ON documents (content_hash);
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresDocumentStore> _logger;

    public PostgresDocumentStore(NpgsqlDataSource dataSource, ILogger<PostgresDocumentStore> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(Schema);
        await command.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("documents schema ensured.");
    }

    public async Task<DocumentRecord?> FindByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        const string sql = $"{SelectColumns} WHERE content_hash = $1;";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(contentHash);

        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    private const string SelectColumns = """
        SELECT id, file_name, content_hash, description, source_system,
               character_count, chunk_count, embedding_model, collection_name,
               chunk_size, chunk_overlap, status, created_at, updated_at
        FROM documents
        """;

    private static DocumentRecord Map(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        FileName: reader.GetString(1),
        ContentHash: reader.GetString(2),
        Description: reader.IsDBNull(3) ? null : reader.GetString(3),
        SourceSystem: reader.IsDBNull(4) ? null : reader.GetString(4),
        CharacterCount: reader.GetInt32(5),
        ChunkCount: reader.GetInt32(6),
        EmbeddingModel: reader.GetString(7),
        CollectionName: reader.GetString(8),
        ChunkSize: reader.GetInt32(9),
        ChunkOverlap: reader.GetInt32(10),
        Status: reader.GetString(11),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(12),
        UpdatedAt: reader.GetFieldValue<DateTimeOffset>(13));

    public async Task SaveAsync(DocumentRecord document, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO documents (
                id, file_name, content_hash, description, source_system,
                character_count, chunk_count, embedding_model, collection_name,
                chunk_size, chunk_overlap, status, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
            ON CONFLICT (content_hash) DO UPDATE SET
                file_name       = EXCLUDED.file_name,
                description     = EXCLUDED.description,
                source_system   = EXCLUDED.source_system,
                character_count = EXCLUDED.character_count,
                chunk_count     = EXCLUDED.chunk_count,
                embedding_model = EXCLUDED.embedding_model,
                collection_name = EXCLUDED.collection_name,
                chunk_size      = EXCLUDED.chunk_size,
                chunk_overlap   = EXCLUDED.chunk_overlap,
                status          = EXCLUDED.status,
                updated_at      = EXCLUDED.updated_at;
            """;

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(document.Id);
        command.Parameters.AddWithValue(document.FileName);
        command.Parameters.AddWithValue(document.ContentHash);
        command.Parameters.AddWithValue((object?)document.Description ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)document.SourceSystem ?? DBNull.Value);
        command.Parameters.AddWithValue(document.CharacterCount);
        command.Parameters.AddWithValue(document.ChunkCount);
        command.Parameters.AddWithValue(document.EmbeddingModel);
        command.Parameters.AddWithValue(document.CollectionName);
        command.Parameters.AddWithValue(document.ChunkSize);
        command.Parameters.AddWithValue(document.ChunkOverlap);
        command.Parameters.AddWithValue(document.Status);
        command.Parameters.AddWithValue(document.CreatedAt);
        command.Parameters.AddWithValue(document.UpdatedAt);

        await command.ExecuteNonQueryAsync(ct);

        _logger.LogInformation(
            "Document {Id} saved: {File}, {Chunks} chunks, status={Status}.",
            document.Id, document.FileName, document.ChunkCount, document.Status);
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListSummariesAsync(CancellationToken ct = default)
    {
        // full_text can be megabytes: never pull the column into a listing,
        // only the fact that it is filled.
        const string sql = """
            SELECT id, file_name, description, source_system, character_count, chunk_count,
                   embedding_model, collection_name, chunk_size, chunk_overlap, status,
                   (full_text IS NOT NULL) AS has_full_text, created_at, updated_at
            FROM documents
            ORDER BY created_at DESC;
            """;

        var summaries = new List<DocumentSummary>();

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            summaries.Add(new DocumentSummary(
                Id: reader.GetGuid(0),
                FileName: reader.GetString(1),
                Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceSystem: reader.IsDBNull(3) ? null : reader.GetString(3),
                CharacterCount: reader.GetInt32(4),
                ChunkCount: reader.GetInt32(5),
                EmbeddingModel: reader.GetString(6),
                CollectionName: reader.GetString(7),
                ChunkSize: reader.GetInt32(8),
                ChunkOverlap: reader.GetInt32(9),
                Status: reader.GetString(10),
                HasFullText: reader.GetBoolean(11),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(12),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(13)));
        }

        return summaries;
    }

    public async Task<DocumentRecord?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = $"{SelectColumns} WHERE id = $1;";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task SaveFullTextAsync(Guid documentId, string text, CancellationToken ct = default)
    {
        const string sql = "UPDATE documents SET full_text = $2 WHERE id = $1;";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(documentId);
        command.Parameters.AddWithValue(text);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetFullTextAsync(Guid documentId, CancellationToken ct = default)
    {
        const string sql = "SELECT full_text FROM documents WHERE id = $1;";

        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(documentId);

        var result = await command.ExecuteScalarAsync(ct);

        return result is string text ? text : null;
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT count(*) FROM documents;");
        var result = await command.ExecuteScalarAsync(ct);

        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<DocumentRecord>> ListAsync(CancellationToken ct = default)
    {
        const string sql = $"{SelectColumns} ORDER BY created_at DESC;";

        var documents = new List<DocumentRecord>();

        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            documents.Add(Map(reader));
        }

        return documents;
    }
}
