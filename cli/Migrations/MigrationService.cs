using System.Security.Cryptography;
using System.Text;
using ModuleDev.Cli.Database;
using ModuleDev.Cli.Output;
using Npgsql;

namespace ModuleDev.Cli.Migrations;

public sealed class MigrationService
{
    private readonly CliDatabase _database;
    private readonly CliOutput _output;

    public MigrationService(
        CliDatabase database,
        CliOutput output)
    {
        _database = database;
        _output = output;
    }

    public async Task<int> ApplyAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return _output.Error(
                code: "migration.directory_not_found",
                message: $"migration directory does not exist: {directory}");
        }

        var files = Directory
            .GetFiles(
                directory,
                "*.sql",
                SearchOption.TopDirectoryOnly)
            .OrderBy(
                Path.GetFileName,
                StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            return _output.Error(
                code: "migration.no_files",
                message: "migration directory contains no .sql files");
        }

        await using var connection =
            await _database.OpenOwnerConnectionAsync(
                cancellationToken);

        var appliedCount = 0;

        foreach (var file in files)
        {
            var wasApplied = await ApplyMigrationAsync(
                connection,
                file,
                cancellationToken);

            if (wasApplied)
            {
                appliedCount++;
            }
        }

        return _output.Success(new
        {
            resource = "migration",
            operation = "applied",
            count = appliedCount
        });
    }

    private async Task<bool> ApplyMigrationAsync(
        NpgsqlConnection connection,
        string file,
        CancellationToken cancellationToken)
    {
        var filename = Path.GetFileName(file);

        var sql = await File.ReadAllTextAsync(
            file,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException(
                $"migration '{filename}' is empty");
        }

        var checksum = ComputeSha256(sql);

        var existingChecksum =
            await GetExistingChecksumAsync(
                connection,
                filename,
                cancellationToken);

        if (existingChecksum is not null)
        {
            if (!existingChecksum.Equals(
                    checksum,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"migration '{filename}' was already applied with a different checksum");
            }

            _output.WriteDiagnostic(
                $"migration already applied: {filename}");

            return false;
        }

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await ExecuteMigrationSqlAsync(
                connection,
                transaction,
                sql,
                cancellationToken);

            await InsertMigrationRecordAsync(
                connection,
                transaction,
                filename,
                checksum,
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            _output.WriteDiagnostic(
                $"migration applied: {filename}");

            return true;
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);

            throw;
        }
    }

    private static async Task ExecuteMigrationSqlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            sql,
            connection,
            transaction);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertMigrationRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string filename,
        string checksum,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO runtime.schema_migrations
                (filename, checksum)
            VALUES
                (@filename, @checksum);
            """;

        await using var command = new NpgsqlCommand(
            sql,
            connection,
            transaction);

        command.Parameters.AddWithValue(
            "filename",
            filename);

        command.Parameters.AddWithValue(
            "checksum",
            checksum);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<string?> GetExistingChecksumAsync(
        NpgsqlConnection connection,
        string filename,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT checksum
            FROM runtime.schema_migrations
            WHERE filename = @filename;
            """;

        await using var command = new NpgsqlCommand(
            sql,
            connection);

        command.Parameters.AddWithValue(
            "filename",
            filename);

        var result = await command.ExecuteScalarAsync(
            cancellationToken);

        return result as string;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }
}