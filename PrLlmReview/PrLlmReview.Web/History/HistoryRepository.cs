using Microsoft.Data.Sqlite;
using PrLlmReview.Models;

namespace PrLlmReview.History;

/// <summary>
/// Persists and queries review records in a SQLite database.
/// </summary>
public sealed class HistoryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<HistoryRepository> _logger;

    public HistoryRepository(IConfiguration config, ILogger<HistoryRepository> logger)
    {
        var dbPath = config["History:DbPath"] ?? "history.db";

        // Ensure directory exists
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public void EnsureCreated()
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ReviewRecord (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ReviewedAt      TEXT NOT NULL,
                ProjectName     TEXT NOT NULL,
                RepositoryName  TEXT NOT NULL,
                PrId            INTEGER NOT NULL,
                PrTitle         TEXT NOT NULL,
                AuthorName      TEXT,
                TargetBranch    TEXT,
                FilesReviewed   INTEGER,
                OverallSeverity TEXT,
                SummaryText     TEXT,
                FullResultJson  TEXT
            );
            """;
        cmd.ExecuteNonQuery();
        _logger.LogDebug("History database schema ensured.");
    }

    public async Task SaveAsync(ReviewRecord record, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd  = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO ReviewRecord
                (ReviewedAt, ProjectName, RepositoryName, PrId, PrTitle, AuthorName,
                 TargetBranch, FilesReviewed, OverallSeverity, SummaryText, FullResultJson)
            VALUES
                ($reviewedAt, $projectName, $repositoryName, $prId, $prTitle, $authorName,
                 $targetBranch, $filesReviewed, $overallSeverity, $summaryText, $fullResultJson);
            """;

        cmd.Parameters.AddWithValue("$reviewedAt",      record.ReviewedAt);
        cmd.Parameters.AddWithValue("$projectName",     record.ProjectName);
        cmd.Parameters.AddWithValue("$repositoryName",  record.RepositoryName);
        cmd.Parameters.AddWithValue("$prId",            record.PrId);
        cmd.Parameters.AddWithValue("$prTitle",         record.PrTitle);
        cmd.Parameters.AddWithValue("$authorName",      record.AuthorName);
        cmd.Parameters.AddWithValue("$targetBranch",    record.TargetBranch);
        cmd.Parameters.AddWithValue("$filesReviewed",   record.FilesReviewed);
        cmd.Parameters.AddWithValue("$overallSeverity", record.OverallSeverity);
        cmd.Parameters.AddWithValue("$summaryText",     record.SummaryText);
        cmd.Parameters.AddWithValue("$fullResultJson",  record.FullResultJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<(List<ReviewRecord> Records, int TotalCount)> SearchAsync(
        string? repo, string? titleKeyword, string? severity, string? fromDate, string? toDate,
        int page, int pageSize, CancellationToken ct)
    {
        var conditions = new List<string>();
        var cmd = new SqliteCommand();

        if (!string.IsNullOrWhiteSpace(repo))
        {
            conditions.Add("LOWER(RepositoryName) = LOWER($repo)");
            cmd.Parameters.AddWithValue("$repo", repo);
        }
        if (!string.IsNullOrWhiteSpace(titleKeyword))
        {
            conditions.Add("LOWER(PrTitle) LIKE $keyword");
            cmd.Parameters.AddWithValue("$keyword", $"%{titleKeyword.ToLowerInvariant()}%");
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            conditions.Add("LOWER(OverallSeverity) = LOWER($severity)");
            cmd.Parameters.AddWithValue("$severity", severity);
        }
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            conditions.Add("ReviewedAt >= $fromDate");
            cmd.Parameters.AddWithValue("$fromDate", fromDate);
        }
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            conditions.Add("ReviewedAt <= $toDate");
            cmd.Parameters.AddWithValue("$toDate", toDate + "T23:59:59Z");
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        var offset = (page - 1) * pageSize;

        await using var conn = await OpenAsync(ct);

        // Count query
        cmd.Connection = conn;
        cmd.CommandText = $"SELECT COUNT(*) FROM ReviewRecord {where}";
        var total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));

        // Page query
        cmd.CommandText = $"""
            SELECT Id, ReviewedAt, ProjectName, RepositoryName, PrId, PrTitle,
                   AuthorName, TargetBranch, FilesReviewed, OverallSeverity, SummaryText
            FROM ReviewRecord {where}
            ORDER BY Id DESC
            LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$limit",  pageSize);
        cmd.Parameters.AddWithValue("$offset", offset);

        var records = new List<ReviewRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(new ReviewRecord
            {
                Id              = reader.GetInt32(0),
                ReviewedAt      = reader.GetString(1),
                ProjectName     = reader.GetString(2),
                RepositoryName  = reader.GetString(3),
                PrId            = reader.GetInt32(4),
                PrTitle         = reader.GetString(5),
                AuthorName      = reader.IsDBNull(6)  ? string.Empty : reader.GetString(6),
                TargetBranch    = reader.IsDBNull(7)  ? string.Empty : reader.GetString(7),
                FilesReviewed   = reader.IsDBNull(8)  ? 0             : reader.GetInt32(8),
                OverallSeverity = reader.IsDBNull(9)  ? string.Empty : reader.GetString(9),
                SummaryText     = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            });
        }

        return (records, total);
    }

    public async Task<ReviewRecord?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ReviewRecord WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ReviewRecord
        {
            Id              = reader.GetInt32(0),
            ReviewedAt      = reader.GetString(1),
            ProjectName     = reader.GetString(2),
            RepositoryName  = reader.GetString(3),
            PrId            = reader.GetInt32(4),
            PrTitle         = reader.GetString(5),
            AuthorName      = reader.IsDBNull(6)  ? string.Empty : reader.GetString(6),
            TargetBranch    = reader.IsDBNull(7)  ? string.Empty : reader.GetString(7),
            FilesReviewed   = reader.IsDBNull(8)  ? 0             : reader.GetInt32(8),
            OverallSeverity = reader.IsDBNull(9)  ? string.Empty : reader.GetString(9),
            SummaryText     = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            FullResultJson  = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
        };
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
