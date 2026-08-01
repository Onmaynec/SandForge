using System.Text.Json;
using Microsoft.Data.Sqlite;
using SandForge.Domain;

namespace SandForge.Core;

public sealed class SessionStore
{
    private readonly string _dataDirectory;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SessionStore(string dataDirectory)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _databasePath = Path.Combine(_dataDirectory, "sandforge.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath => _databasePath;
    public string DataDirectory => _dataDirectory;

    public async Task SaveAsync(SandboxSession session, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Sessions
                (Id, TemplateId, CreatedAt, StartedAt, EndedAt, Status, WorkspacePath, ConfigurationPath,
                 TargetFileHash, Risk, SandboxProcessId, CleanupState, Error, UpdatedAt)
                VALUES ($id, $template, $created, $started, $ended, $status, $workspace, $config,
                        $hash, $risk, $pid, $cleanup, $error, $updated)
                ON CONFLICT(Id) DO UPDATE SET
                    TemplateId=excluded.TemplateId, StartedAt=excluded.StartedAt, EndedAt=excluded.EndedAt,
                    Status=excluded.Status, WorkspacePath=excluded.WorkspacePath,
                    ConfigurationPath=excluded.ConfigurationPath, TargetFileHash=excluded.TargetFileHash,
                    Risk=excluded.Risk, SandboxProcessId=excluded.SandboxProcessId,
                    CleanupState=excluded.CleanupState, Error=excluded.Error, UpdatedAt=excluded.UpdatedAt;
                """;
            Add(command, "$id", session.Id);
            Add(command, "$template", session.TemplateId);
            Add(command, "$created", session.CreatedAt.ToString("O"));
            Add(command, "$started", session.StartedAt?.ToString("O"));
            Add(command, "$ended", session.EndedAt?.ToString("O"));
            Add(command, "$status", (int)session.Status);
            Add(command, "$workspace", session.WorkspacePath);
            Add(command, "$config", session.ConfigurationPath);
            Add(command, "$hash", session.TargetFileHash);
            Add(command, "$risk", (int)session.Risk);
            Add(command, "$pid", session.SandboxProcessId);
            Add(command, "$cleanup", (int)session.Cleanup);
            Add(command, "$error", session.Error);
            Add(command, "$updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(connection, transaction, "DELETE FROM SessionArtifacts WHERE SessionId=$id", session.Id, cancellationToken);
        foreach (SessionArtifact artifact in session.Artifacts)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SessionArtifacts(Id, SessionId, Type, RelativePath, Size, Sha256, CreatedAt)
                VALUES($artifactId, $sessionId, $type, $path, $size, $hash, $created);
                """;
            Add(command, "$artifactId", artifact.Id);
            Add(command, "$sessionId", session.Id);
            Add(command, "$type", artifact.Type);
            Add(command, "$path", artifact.RelativePath);
            Add(command, "$size", artifact.Size);
            Add(command, "$hash", artifact.Sha256);
            Add(command, "$created", artifact.CreatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(connection, transaction, "DELETE FROM CollectorResults WHERE SessionId=$id", session.Id, cancellationToken);
        foreach (CollectorResult collector in session.Collectors)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO CollectorResults(SessionId, CollectorId, RelativePath, ItemCount, Error)
                VALUES($sessionId, $collectorId, $path, $count, $error);
                """;
            Add(command, "$sessionId", session.Id);
            Add(command, "$collectorId", collector.Id);
            Add(command, "$path", collector.RelativePath);
            Add(command, "$count", collector.ItemCount);
            Add(command, "$error", collector.Error);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<SandboxSession>> LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var sessions = new List<SandboxSession>();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM Sessions ORDER BY CreatedAt DESC";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) sessions.Add(ReadSessionRow(reader));
        }
        for (int i = 0; i < sessions.Count; i++)
        {
            SandboxSession session = sessions[i];
            sessions[i] = session with
            {
                Artifacts = await ReadArtifactsAsync(connection, session.Id, cancellationToken),
                Collectors = await ReadCollectorsAsync(connection, session.Id, cancellationToken)
            };
        }
        return sessions;
    }

    public async Task<SandboxSession?> FindAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        SandboxSession? session;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM Sessions WHERE Id=$id";
            Add(command, "$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            session = await reader.ReadAsync(cancellationToken) ? ReadSessionRow(reader) : null;
        }
        if (session is null) return null;
        return session with
        {
            Artifacts = await ReadArtifactsAsync(connection, session.Id, cancellationToken),
            Collectors = await ReadCollectorsAsync(connection, session.Id, cancellationToken)
        };
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, "DELETE FROM SessionArtifacts WHERE SessionId=$id", id, cancellationToken);
        await ExecuteAsync(connection, transaction, "DELETE FROM CollectorResults WHERE SessionId=$id", id, cancellationToken);
        await ExecuteAsync(connection, transaction, "DELETE FROM Sessions WHERE Id=$id", id, cancellationToken);
        transaction.Commit();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_dataDirectory);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys=ON;
                CREATE TABLE IF NOT EXISTS MigrationHistory(
                    Version INTEGER PRIMARY KEY,
                    AppliedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Sessions(
                    Id TEXT PRIMARY KEY,
                    TemplateId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    StartedAt TEXT NULL,
                    EndedAt TEXT NULL,
                    Status INTEGER NOT NULL,
                    WorkspacePath TEXT NOT NULL,
                    ConfigurationPath TEXT NOT NULL,
                    TargetFileHash TEXT NOT NULL,
                    Risk INTEGER NOT NULL,
                    SandboxProcessId INTEGER NULL,
                    CleanupState INTEGER NOT NULL DEFAULT 0,
                    Error TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS SessionArtifacts(
                    Id TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                    Type TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    Size INTEGER NOT NULL,
                    Sha256 TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS CollectorResults(
                    SessionId TEXT NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                    CollectorId TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    ItemCount INTEGER NOT NULL,
                    Error TEXT NULL,
                    PRIMARY KEY(SessionId, CollectorId)
                );
                INSERT OR IGNORE INTO MigrationHistory(Version, AppliedAt) VALUES(1, CURRENT_TIMESTAMP);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
            try { await ImportLegacyJsonAsync(cancellationToken); }
            catch { _initialized = false; throw; }
        }
        finally { _initializationLock.Release(); }
    }

    private async Task ImportLegacyJsonAsync(CancellationToken cancellationToken)
    {
        string legacyPath = Path.Combine(_dataDirectory, "sessions", "index.json");
        if (!File.Exists(legacyPath)) return;
        List<SandboxSession>? sessions;
        await using (FileStream stream = File.OpenRead(legacyPath))
            sessions = await JsonSerializer.DeserializeAsync<List<SandboxSession>>(stream, JsonOptions, cancellationToken);
        if (sessions is not null)
            foreach (SandboxSession session in sessions) await SaveAsync(session, cancellationToken);
        File.Move(legacyPath, legacyPath + ".migrated", true);
    }

    private static SandboxSession ReadSessionRow(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("Id")),
        TemplateId = reader.GetString(reader.GetOrdinal("TemplateId")),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        StartedAt = ReadDate(reader, "StartedAt"),
        EndedAt = ReadDate(reader, "EndedAt"),
        Status = (SessionStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        WorkspacePath = reader.GetString(reader.GetOrdinal("WorkspacePath")),
        ConfigurationPath = reader.GetString(reader.GetOrdinal("ConfigurationPath")),
        TargetFileHash = reader.GetString(reader.GetOrdinal("TargetFileHash")),
        Risk = (RiskLevel)reader.GetInt32(reader.GetOrdinal("Risk")),
        SandboxProcessId = reader.IsDBNull(reader.GetOrdinal("SandboxProcessId")) ? null : reader.GetInt32(reader.GetOrdinal("SandboxProcessId")),
        Cleanup = (CleanupState)reader.GetInt32(reader.GetOrdinal("CleanupState")),
        Error = reader.IsDBNull(reader.GetOrdinal("Error")) ? null : reader.GetString(reader.GetOrdinal("Error"))
    };

    private static async Task<IReadOnlyList<SessionArtifact>> ReadArtifactsAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        var result = new List<SessionArtifact>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SessionArtifacts WHERE SessionId=$id ORDER BY RelativePath";
        Add(command, "$id", sessionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new SessionArtifact
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Type = reader.GetString(reader.GetOrdinal("Type")),
            RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
            Size = reader.GetInt64(reader.GetOrdinal("Size")),
            Sha256 = reader.GetString(reader.GetOrdinal("Sha256")),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt")))
        });
        return result;
    }

    private static async Task<IReadOnlyList<CollectorResult>> ReadCollectorsAsync(SqliteConnection connection, string sessionId, CancellationToken cancellationToken)
    {
        var result = new List<CollectorResult>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM CollectorResults WHERE SessionId=$id ORDER BY CollectorId";
        Add(command, "$id", sessionId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new CollectorResult
        {
            Id = reader.GetString(reader.GetOrdinal("CollectorId")),
            RelativePath = reader.GetString(reader.GetOrdinal("RelativePath")),
            ItemCount = reader.GetInt32(reader.GetOrdinal("ItemCount")),
            Error = reader.IsDBNull(reader.GetOrdinal("Error")) ? null : reader.GetString(reader.GetOrdinal("Error"))
        });
        return result;
    }

    private static DateTimeOffset? ReadDate(SqliteDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name)) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(name)));

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, string id, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        Add(command, "$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
