using System.Security.Cryptography;
using System.Text;
using Npgsql;

const string ContractVersion = "course-1";

if (args.Length == 0)
{
    PrintError(
        "request.invalid",
        "command is required");

    return 1;
}

try
{
    return await ExecuteAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);

    PrintError(
        "internal.error",
        "unexpected CLI error");

    return 1;
}

static async Task<int> ExecuteAsync(string[] args)
{
    if (args.Length >= 3 &&
        args[0].Equals("migration", StringComparison.OrdinalIgnoreCase) &&
        args[1].Equals("apply", StringComparison.OrdinalIgnoreCase))
    {
        var directory = args[2];

        return await ApplyMigrationsAsync(directory);
    }

    PrintError(
        "request.invalid",
        "unsupported command");

    return 1;
}

static async Task<int> ApplyMigrationsAsync(string directory)
{
    if (!Directory.Exists(directory))
    {
        PrintError(
            "migration.directory_not_found",
            $"migration directory does not exist: {directory}");

        return 1;
    }

    var files = Directory
        .GetFiles(directory, "*.sql", SearchOption.TopDirectoryOnly)
        .OrderBy(Path.GetFileName, StringComparer.Ordinal)
        .ToArray();

    if (files.Length == 0)
    {
        PrintError(
            "migration.no_files",
            "migration directory contains no .sql files");

        return 1;
    }

    await using var connection = await OpenConnectionAsync();

    await SetMigrationRoleAsync(connection);

    foreach (var file in files)
    {
        await ApplyMigrationAsync(connection, file);
    }

    PrintSuccess(
        resource: "migration",
        operation: "applied",
        count: files.Length);

    return 0;
}

static async Task<NpgsqlConnection> OpenConnectionAsync()
{
    var host = GetRequiredEnvironmentVariable("DB_HOST");
    var port = GetEnvironmentVariable("DB_PORT", "5432");
    var database = GetRequiredEnvironmentVariable("DB_NAME");
    var username = GetRequiredEnvironmentVariable("DB_USER");
    var password = GetRequiredEnvironmentVariable("DB_PASSWORD");

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = int.Parse(port),
        Database = database,
        Username = username,
        Password = password,
        ApplicationName = "moduledev-cli",
        Timeout = 10,
        CommandTimeout = 30,
        Pooling = false
    };

    var connection = new NpgsqlConnection(builder.ConnectionString);

    await connection.OpenAsync();

    return connection;
}

static async Task SetMigrationRoleAsync(
    NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand(
        "SET ROLE course_owner;",
        connection);

    await command.ExecuteNonQueryAsync();
}

static async Task ApplyMigrationAsync(
    NpgsqlConnection connection,
    string file)
{
    var filename = Path.GetFileName(file);

    var sql = await File.ReadAllTextAsync(file);

    var checksum = ComputeSha256(sql);

    var existingChecksum = await GetExistingChecksumAsync(
        connection,
        filename);

    if (existingChecksum is not null)
    {
        if (!existingChecksum.Equals(
                checksum,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"migration '{filename}' was already applied with a different checksum");
        }

        Console.Error.WriteLine(
            $"migration already applied: {filename}");

        return;
    }

    if (string.IsNullOrWhiteSpace(sql))
    {
        throw new InvalidOperationException(
            $"migration '{filename}' is empty");
    }

    await using var transaction =
        await connection.BeginTransactionAsync();

    try
    {
        await using (var command =
            new NpgsqlCommand(sql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO runtime.schema_migrations
                (filename, checksum)
            VALUES
                (@filename, @checksum);
            """;

        await using (var command =
            new NpgsqlCommand(insertSql, connection, transaction))
        {
            command.Parameters.AddWithValue(
                "filename",
                filename);

            command.Parameters.AddWithValue(
                "checksum",
                checksum);

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        Console.Error.WriteLine(
            $"migration applied: {filename}");
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task<string?> GetExistingChecksumAsync(
    NpgsqlConnection connection,
    string filename)
{
    const string sql = """
        SELECT checksum
        FROM runtime.schema_migrations
        WHERE filename = @filename;
        """;

    await using var command =
        new NpgsqlCommand(sql, connection);

    command.Parameters.AddWithValue(
        "filename",
        filename);

    var result = await command.ExecuteScalarAsync();

    return result as string;
}

static string ComputeSha256(string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);

    var hash = SHA256.HashData(bytes);

    return Convert.ToHexString(hash).ToLowerInvariant();
}

static string GetRequiredEnvironmentVariable(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"environment variable '{name}' is required");
    }

    return value;
}

static string GetEnvironmentVariable(
    string name,
    string defaultValue)
{
    return Environment.GetEnvironmentVariable(name)
           ?? defaultValue;
}

static void PrintSuccess(
    string resource,
    string operation,
    int count)
{
    Console.WriteLine(
        $$"""
        {
          "status": "ok",
          "result": {
            "resource": "{{resource}}",
            "operation": "{{operation}}",
            "count": {{count}}
          },
          "meta": {
            "contractVersion": "{{ContractVersion}}"
          }
        }
        """);
}

static void PrintError(
    string code,
    string message)
{
    Console.WriteLine(
        $$"""
        {
          "status": "error",
          "code": "{{EscapeJson(code)}}",
          "message": "{{EscapeJson(message)}}",
          "meta": {
            "contractVersion": "{{ContractVersion}}"
          }
        }
        """);
}

static string EscapeJson(string value)
{
    return value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");
}