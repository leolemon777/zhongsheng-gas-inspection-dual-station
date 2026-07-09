using System.IO;
using Microsoft.Data.Sqlite;
using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class InspectionRecordStore
{
    private readonly string _databasePath;

    public InspectionRecordStore()
    {
        AppStoragePaths.CopyLegacyFileIfNeeded("inspection-records.db");
        _databasePath = AppStoragePaths.GetDataFilePath("inspection-records.db");
        Initialize();
    }

    public event EventHandler? RecordsChanged;

    public async Task AddAsync(InspectionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inspection_records
                (id, started_at, ended_at, station_id, station_name, product_code, p1, p2, leak_rate, result, max_leak_rate, fill_seconds, stabilize_seconds, hold_seconds)
            VALUES
                ($id, $started_at, $ended_at, $station_id, $station_name, $product_code, $p1, $p2, $leak_rate, $result, $max_leak_rate, $fill_seconds, $stabilize_seconds, $hold_seconds);
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("N"));
        command.Parameters.AddWithValue("$started_at", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$ended_at", record.EndedAt.ToString("O"));
        command.Parameters.AddWithValue("$station_id", record.StationId);
        command.Parameters.AddWithValue("$station_name", record.StationName);
        command.Parameters.AddWithValue("$product_code", (object?)record.ProductCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$p1", record.P1);
        command.Parameters.AddWithValue("$p2", record.P2);
        command.Parameters.AddWithValue("$leak_rate", record.LeakRate);
        command.Parameters.AddWithValue("$result", record.Result);
        command.Parameters.AddWithValue("$max_leak_rate", record.MaxLeakRate);
        command.Parameters.AddWithValue("$fill_seconds", record.FillSeconds);
        command.Parameters.AddWithValue("$stabilize_seconds", record.StabilizeSeconds);
        command.Parameters.AddWithValue("$hold_seconds", record.HoldSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
        RecordsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<InspectionRecord>> GetLatestAsync(int count, int stationId, CancellationToken cancellationToken)
    {
        List<InspectionRecord> records = [];
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, started_at, ended_at, station_id, station_name, product_code, p1, p2, leak_rate, result, max_leak_rate, fill_seconds, stabilize_seconds, hold_seconds
            FROM inspection_records
            WHERE station_id = $station_id
            ORDER BY ended_at DESC
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$station_id", stationId);
        command.Parameters.AddWithValue("$count", count);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new InspectionRecord(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.GetDecimal(10),
                reader.GetDouble(11),
                reader.GetDouble(12),
                reader.GetDouble(13)));
        }

        return records;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS inspection_records (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                station_id INTEGER NOT NULL DEFAULT 1,
                station_name TEXT NOT NULL DEFAULT '旧记录',
                product_code TEXT NULL,
                p1 REAL NOT NULL,
                p2 REAL NOT NULL,
                leak_rate REAL NOT NULL,
                result TEXT NOT NULL,
                max_leak_rate REAL NOT NULL,
                fill_seconds REAL NOT NULL,
                stabilize_seconds REAL NOT NULL,
                hold_seconds REAL NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_inspection_records_ended_at
            ON inspection_records (ended_at DESC);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "station_id", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "station_name", "TEXT NOT NULL DEFAULT '旧记录'");
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName, string definition)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "PRAGMA table_info(inspection_records);";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE inspection_records ADD COLUMN {columnName} {definition};";
        alterCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }
}
