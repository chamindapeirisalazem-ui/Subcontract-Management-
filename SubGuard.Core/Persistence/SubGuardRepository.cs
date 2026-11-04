using System.Globalization;
using Microsoft.Data.Sqlite;
using SubGuard.Core.Models;

namespace SubGuard.Core.Persistence;

public sealed class SubGuardRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SubGuardRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (databasePath != ":memory:")
        {
            var fullPath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            databasePath = fullPath;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();
    }

    public IReadOnlyList<Subcontractor> GetSubcontractors()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, contact_name, email, phone
            FROM subcontractors
            ORDER BY name COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<Subcontractor>();
        while (reader.Read())
        {
            items.Add(new Subcontractor
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ContactName = reader.GetString(2),
                Email = reader.GetString(3),
                Phone = reader.GetString(4)
            });
        }

        return items;
    }

    public IReadOnlyList<PayApplication> GetPayApplications()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.application_number, p.subcontractor_id, s.name,
                   p.period_start, p.period_end, p.amount, p.notes
            FROM pay_applications p
            INNER JOIN subcontractors s ON s.id = p.subcontractor_id
            ORDER BY p.period_end DESC, p.id DESC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<PayApplication>();
        while (reader.Read())
        {
            items.Add(new PayApplication
            {
                Id = reader.GetInt32(0),
                ApplicationNumber = reader.GetString(1),
                SubcontractorId = reader.GetInt32(2),
                SubcontractorName = reader.GetString(3),
                PeriodStart = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                PeriodEnd = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Amount = decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                Notes = reader.GetString(7)
            });
        }

        return items;
    }

    public IReadOnlyList<AuditEntry> GetAuditEntries()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, occurred_at, entity_type, entity_id, action, summary
            FROM audit_entries
            ORDER BY id DESC;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<AuditEntry>();
        while (reader.Read())
        {
            items.Add(new AuditEntry
            {
                Id = reader.GetInt64(0),
                OccurredAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                EntityType = reader.GetString(2),
                EntityId = reader.GetInt32(3),
                Action = reader.GetString(4),
                Summary = reader.GetString(5)
            });
        }

        return items;
    }

    public Subcontractor SaveSubcontractor(Subcontractor subcontractor)
    {
        ArgumentNullException.ThrowIfNull(subcontractor);
        var name = Required(subcontractor.Name, nameof(subcontractor.Name));
        using var transaction = _connection.BeginTransaction();

        int id;
        string action;
        if (subcontractor.Id == 0)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO subcontractors (name, contact_name, email, phone)
                VALUES ($name, $contact, $email, $phone)
                RETURNING id;
                """;
            AddSubcontractorParameters(command, subcontractor, name);
            id = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            action = "Created";
        }
        else
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE subcontractors
                SET name = $name, contact_name = $contact, email = $email, phone = $phone
                WHERE id = $id;
                """;
            AddSubcontractorParameters(command, subcontractor, name);
            command.Parameters.AddWithValue("$id", subcontractor.Id);
            if (command.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException($"Subcontractor {subcontractor.Id} no longer exists.");
            }

            id = subcontractor.Id;
            action = "Updated";
        }

        AddAuditEntry(transaction, "Subcontractor", id, action, name);
        transaction.Commit();

        return subcontractor with { Id = id, Name = name };
    }

    public bool DeleteSubcontractor(int id)
    {
        using var transaction = _connection.BeginTransaction();
        using var nameCommand = _connection.CreateCommand();
        nameCommand.Transaction = transaction;
        nameCommand.CommandText = "SELECT name FROM subcontractors WHERE id = $id;";
        nameCommand.Parameters.AddWithValue("$id", id);
        var name = nameCommand.ExecuteScalar() as string;

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM subcontractors WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        if (command.ExecuteNonQuery() == 0)
        {
            transaction.Commit();
            return false;
        }

        AddAuditEntry(transaction, "Subcontractor", id, "Deleted", name ?? $"Subcontractor #{id}");
        transaction.Commit();
        return true;
    }

    public PayApplication SavePayApplication(PayApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var number = Required(application.ApplicationNumber, nameof(application.ApplicationNumber));
        if (application.SubcontractorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(application.SubcontractorId), "Select a subcontractor.");
        }

        if (application.PeriodStart.Date > application.PeriodEnd.Date)
        {
            throw new ArgumentException("The period start must be on or before the period end.", nameof(application));
        }

        var start = application.PeriodStart.ToString("O", CultureInfo.InvariantCulture);
        var end = application.PeriodEnd.ToString("O", CultureInfo.InvariantCulture);
        var amount = application.Amount.ToString(CultureInfo.InvariantCulture);
        using var transaction = _connection.BeginTransaction();

        int id;
        string action;
        if (application.Id == 0)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pay_applications
                    (application_number, subcontractor_id, period_start, period_end, amount, notes)
                VALUES ($number, $subcontractorId, $start, $end, $amount, $notes)
                RETURNING id;
                """;
            AddPayApplicationParameters(command, application, number, start, end, amount);
            id = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            action = "Created";
        }
        else
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE pay_applications
                SET application_number = $number, subcontractor_id = $subcontractorId,
                    period_start = $start, period_end = $end, amount = $amount, notes = $notes
                WHERE id = $id;
                """;
            AddPayApplicationParameters(command, application, number, start, end, amount);
            command.Parameters.AddWithValue("$id", application.Id);
            if (command.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException($"Pay application {application.Id} no longer exists.");
            }

            id = application.Id;
            action = "Updated";
        }

        AddAuditEntry(transaction, "Pay application", id, action, number);
        transaction.Commit();

        return application with { Id = id, ApplicationNumber = number };
    }

    public bool DeletePayApplication(int id)
    {
        using var transaction = _connection.BeginTransaction();
        using var numberCommand = _connection.CreateCommand();
        numberCommand.Transaction = transaction;
        numberCommand.CommandText = "SELECT application_number FROM pay_applications WHERE id = $id;";
        numberCommand.Parameters.AddWithValue("$id", id);
        var number = numberCommand.ExecuteScalar() as string;

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM pay_applications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        if (command.ExecuteNonQuery() == 0)
        {
            transaction.Commit();
            return false;
        }

        AddAuditEntry(transaction, "Pay application", id, "Deleted", number ?? $"Pay application #{id}");
        transaction.Commit();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connection.Dispose();
        _disposed = true;
    }

    private void InitializeDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS subcontractors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                contact_name TEXT NOT NULL DEFAULT '',
                email TEXT NOT NULL DEFAULT '',
                phone TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS pay_applications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                application_number TEXT NOT NULL,
                subcontractor_id INTEGER NOT NULL REFERENCES subcontractors(id) ON DELETE RESTRICT,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                amount TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS audit_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_at TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id INTEGER NOT NULL,
                action TEXT NOT NULL,
                summary TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static void AddSubcontractorParameters(SqliteCommand command, Subcontractor subcontractor, string name)
    {
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$contact", subcontractor.ContactName.Trim());
        command.Parameters.AddWithValue("$email", subcontractor.Email.Trim());
        command.Parameters.AddWithValue("$phone", subcontractor.Phone.Trim());
    }

    private static void AddPayApplicationParameters(
        SqliteCommand command,
        PayApplication application,
        string number,
        string start,
        string end,
        string amount)
    {
        command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$subcontractorId", application.SubcontractorId);
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$end", end);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$notes", application.Notes.Trim());
    }

    private void AddAuditEntry(SqliteTransaction transaction, string entityType, int entityId, string action, string summary)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_entries (occurred_at, entity_type, entity_id, action, summary)
            VALUES ($occurredAt, $entityType, $entityId, $action, $summary);
            """;
        command.Parameters.AddWithValue("$occurredAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$summary", summary);
        command.ExecuteNonQuery();
    }

    private static string Required(string value, string parameterName)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("A value is required.", parameterName)
            : trimmed;
    }
}
