using System.Globalization;
using Microsoft.Data.Sqlite;
using SubGuard.Core.Domain;
using SubGuard.Core.Models;

namespace SubGuard.Core.Persistence;

public sealed partial class SubGuardRepository : IDisposable
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
        DatabasePath = databasePath;
        InitializeDatabase();
    }

    public string DatabasePath { get; }

    public int GetSchemaVersion()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<Subcontractor> GetSubcontractors()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, contact_name, email, phone, original_contract_value,
                   approved_change_order_total, retainage_percent, project_id
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
                Phone = reader.GetString(4),
                OriginalContractValue = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                ApprovedChangeOrderTotal = decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                RetainagePercent = decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                ProjectId = reader.IsDBNull(8) ? null : reader.GetInt32(8)
            });
        }

        return items;
    }

    public IReadOnlyList<Project> GetProjects()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, project_number, name, owner_name, status
            FROM projects
            ORDER BY project_number COLLATE NOCASE, id;
            """;

        using var reader = command.ExecuteReader();
        var items = new List<Project>();
        while (reader.Read())
        {
            items.Add(new Project
            {
                Id = reader.GetInt32(0),
                ProjectNumber = reader.GetString(1),
                Name = reader.GetString(2),
                OwnerName = reader.GetString(3),
                Status = Enum.Parse<ProjectStatus>(reader.GetString(4), ignoreCase: true)
            });
        }

        return items;
    }

    public Project SaveProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var number = Required(project.ProjectNumber, nameof(project.ProjectNumber));
        var name = Required(project.Name, nameof(project.Name));
        if (!Enum.IsDefined(project.Status))
        {
            throw new ArgumentOutOfRangeException(nameof(project), "Project status must be Active or Closed.");
        }

        using var transaction = _connection.BeginTransaction();
        int id;
        string action;
        try
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            if (project.Id == 0)
            {
                command.CommandText = """
                    INSERT INTO projects (project_number, name, owner_name, status)
                    VALUES ($number, $name, $owner, $status)
                    RETURNING id;
                    """;
                action = "Created";
            }
            else
            {
                command.CommandText = """
                    UPDATE projects
                    SET project_number = $number, name = $name, owner_name = $owner, status = $status
                    WHERE id = $id
                    RETURNING id;
                    """;
                command.Parameters.AddWithValue("$id", project.Id);
                action = "Updated";
            }

            command.Parameters.AddWithValue("$number", number);
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$owner", project.OwnerName.Trim());
            command.Parameters.AddWithValue("$status", project.Status.ToString());
            var result = command.ExecuteScalar();
            if (result is null)
            {
                throw new InvalidOperationException($"Project {project.Id} no longer exists.");
            }

            id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"Project number '{number}' is already in use.", exception);
        }

        AddAuditEntry(transaction, "Project", id, action, $"{number} {name}");
        transaction.Commit();
        return project with { Id = id, ProjectNumber = number, Name = name };
    }

    public bool DeleteProject(int id)
    {
        using var transaction = _connection.BeginTransaction();
        using var infoCommand = _connection.CreateCommand();
        infoCommand.Transaction = transaction;
        infoCommand.CommandText = """
            SELECT project_number, (SELECT COUNT(*) FROM subcontractors WHERE project_id = $id)
            FROM projects WHERE id = $id;
            """;
        infoCommand.Parameters.AddWithValue("$id", id);
        string number;
        using (var reader = infoCommand.ExecuteReader())
        {
            if (!reader.Read())
            {
                return false;
            }

            number = reader.GetString(0);
            if (reader.GetInt64(1) > 0)
            {
                throw new InvalidOperationException(
                    "This project still has subcontracts. Reassign or delete them first, or close the project instead.");
            }
        }

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM projects WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AddAuditEntry(transaction, "Project", id, "Deleted", number);
        transaction.Commit();
        return true;
    }

    public IReadOnlyList<PayApplication> GetPayApplications()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.application_number, p.subcontractor_id, s.name,
                   p.period_start, p.period_end, p.amount, p.notes, p.status,
                   p.submitted_at, p.approved_at, p.retainage_percent_snapshot,
                   p.invoice_reconciliation_status
            FROM pay_applications p
            INNER JOIN subcontractors s ON s.id = p.subcontractor_id
            ORDER BY p.period_end DESC, p.id DESC;
            """;

        var items = new List<PayApplication>();
        using (var reader = command.ExecuteReader())
        {
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
                    Notes = reader.GetString(7),
                    Status = Enum.Parse<ClaimStatus>(reader.GetString(8), ignoreCase: true),
                    SubmittedAt = reader.IsDBNull(9)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ApprovedAt = reader.IsDBNull(10)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    RetainagePercent = decimal.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                    InvoiceReconciliationStatus = Enum.Parse<InvoiceReconciliationStatus>(reader.GetString(12), ignoreCase: true)
                });
            }
        }

        return items
            .Select(application => application with { Lines = GetPayApplicationLines(application.Id) })
            .ToArray();
    }

    public IReadOnlyList<ScheduleOfValuesLine> GetScheduleOfValues(int subcontractorId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, subcontractor_id, line_number, description, scheduled_value,
                   previously_billed_snapshot
            FROM schedule_of_values_lines
            WHERE subcontractor_id = $subcontractorId
            ORDER BY line_number, id;
            """;
        command.Parameters.AddWithValue("$subcontractorId", subcontractorId);

        using var reader = command.ExecuteReader();
        var lines = new List<ScheduleOfValuesLine>();
        while (reader.Read())
        {
            lines.Add(new ScheduleOfValuesLine
            {
                Id = reader.GetInt32(0),
                SubcontractorId = reader.GetInt32(1),
                LineNumber = reader.GetInt32(2),
                Description = reader.GetString(3),
                ScheduledValue = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                PreviouslyBilledSnapshot = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }

        return lines;
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
        if (subcontractor.OriginalContractValue < 0 ||
            subcontractor.ApprovedChangeOrderTotal < 0 ||
            subcontractor.RetainagePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subcontractor),
                "Contract values and approved change orders must be non-negative; retainage must be from 0 to 100 percent.");
        }

        using var transaction = _connection.BeginTransaction();

        int id;
        string action;
        if (subcontractor.Id == 0)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO subcontractors
                    (name, contact_name, email, phone, original_contract_value, approved_change_order_total, retainage_percent, project_id)
                VALUES ($name, $contact, $email, $phone, $originalValue, $changeOrders, $retainage, $projectId)
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
                SET name = $name, contact_name = $contact, email = $email, phone = $phone,
                    original_contract_value = $originalValue,
                    approved_change_order_total = $changeOrders,
                    retainage_percent = $retainage,
                    project_id = $projectId
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

    public IReadOnlyList<ScheduleOfValuesLine> SaveScheduleOfValues(
        int subcontractorId,
        IReadOnlyList<ScheduleOfValuesLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (subcontractorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(subcontractorId));
        }

        if (lines.Any(line =>
                line.LineNumber <= 0 ||
                string.IsNullOrWhiteSpace(line.Description) ||
                line.ScheduledValue < 0 ||
                line.PreviouslyBilledSnapshot < 0))
        {
            throw new ArgumentException(
                "SOV lines need a positive line number, a description, and non-negative values.",
                nameof(lines));
        }

        if (lines.Select(line => line.LineNumber).Distinct().Count() != lines.Count)
        {
            throw new ArgumentException("SOV line numbers must be unique.", nameof(lines));
        }

        using var transaction = _connection.BeginTransaction();
        using var existingCommand = _connection.CreateCommand();
        existingCommand.Transaction = transaction;
        existingCommand.CommandText = "SELECT id FROM schedule_of_values_lines WHERE subcontractor_id = $id;";
        existingCommand.Parameters.AddWithValue("$id", subcontractorId);
        var existingIds = new HashSet<int>();
        using (var reader = existingCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                existingIds.Add(reader.GetInt32(0));
            }
        }

        var retainedIds = lines.Where(line => line.Id > 0).Select(line => line.Id).ToHashSet();
        foreach (var removedId in existingIds.Except(retainedIds))
        {
            using var deleteCommand = _connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = """
                DELETE FROM schedule_of_values_lines
                WHERE id = $id AND subcontractor_id = $subcontractorId;
                """;
            deleteCommand.Parameters.AddWithValue("$id", removedId);
            deleteCommand.Parameters.AddWithValue("$subcontractorId", subcontractorId);
            deleteCommand.ExecuteNonQuery();
        }

        foreach (var line in lines)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            if (line.Id == 0)
            {
                command.CommandText = """
                    INSERT INTO schedule_of_values_lines
                        (subcontractor_id, line_number, description, scheduled_value, previously_billed_snapshot)
                    VALUES ($subcontractorId, $lineNumber, $description, $scheduledValue, $previouslyBilled)
                    RETURNING id;
                    """;
            }
            else
            {
                if (!existingIds.Contains(line.Id))
                {
                    throw new InvalidOperationException($"SOV line {line.Id} does not belong to this subcontractor.");
                }

                command.CommandText = """
                    UPDATE schedule_of_values_lines
                    SET line_number = $lineNumber, description = $description,
                        scheduled_value = $scheduledValue,
                        previously_billed_snapshot = $previouslyBilled
                    WHERE id = $id AND subcontractor_id = $subcontractorId;
                    """;
                command.Parameters.AddWithValue("$id", line.Id);
            }

            command.Parameters.AddWithValue("$subcontractorId", subcontractorId);
            command.Parameters.AddWithValue("$lineNumber", line.LineNumber);
            command.Parameters.AddWithValue("$description", line.Description.Trim());
            command.Parameters.AddWithValue("$scheduledValue", line.ScheduledValue.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$previouslyBilled", line.PreviouslyBilledSnapshot.ToString(CultureInfo.InvariantCulture));
            if (line.Id == 0)
            {
                command.ExecuteScalar();
            }
            else if (command.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException($"SOV line {line.Id} no longer exists.");
            }
        }

        AddAuditEntry(transaction, "Schedule of values", subcontractorId, "Updated", $"Subcontractor #{subcontractorId}");
        transaction.Commit();
        return GetScheduleOfValues(subcontractorId);
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

        if (application.Lines.Count == 0 ||
            application.Lines.Any(line => line.Amount < 0) ||
            application.Lines.All(line => line.Amount == 0))
        {
            throw new ArgumentException("Enter a positive claim amount using non-negative SOV line amounts.", nameof(application));
        }

        if (application.Lines.Select(line => line.ScheduleOfValuesLineId).Distinct().Count() != application.Lines.Count)
        {
            throw new ArgumentException("Each SOV line can appear only once in a claim.", nameof(application));
        }

        var total = application.Lines.Sum(line => line.Amount);
        var start = application.PeriodStart.ToString("O", CultureInfo.InvariantCulture);
        var end = application.PeriodEnd.ToString("O", CultureInfo.InvariantCulture);
        var amount = total.ToString(CultureInfo.InvariantCulture);
        using var transaction = _connection.BeginTransaction();

        if (application.Status != ClaimStatus.Draft)
        {
            throw new InvalidOperationException("Only draft claims can be created or edited.");
        }

        if (application.Id != 0)
        {
            using var statusCommand = _connection.CreateCommand();
            statusCommand.Transaction = transaction;
            statusCommand.CommandText = "SELECT status FROM pay_applications WHERE id = $id;";
            statusCommand.Parameters.AddWithValue("$id", application.Id);
            var status = statusCommand.ExecuteScalar() as string;
            if (status is null)
            {
                throw new InvalidOperationException($"Pay application {application.Id} no longer exists.");
            }

            if (Enum.Parse<ClaimStatus>(status, ignoreCase: true) != ClaimStatus.Draft)
            {
                throw new InvalidOperationException("Only draft claims can be edited.");
            }
        }

        decimal originalValue;
        decimal approvedChangeOrderTotal;
        decimal retainagePercent;
        using (var subcontractCommand = _connection.CreateCommand())
        {
            subcontractCommand.Transaction = transaction;
            subcontractCommand.CommandText = """
                SELECT original_contract_value, approved_change_order_total, retainage_percent
                FROM subcontractors
                WHERE id = $id;
                """;
            subcontractCommand.Parameters.AddWithValue("$id", application.SubcontractorId);
            using var reader = subcontractCommand.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException($"Subcontractor {application.SubcontractorId} no longer exists.");
            }

            originalValue = decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            approvedChangeOrderTotal = decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            retainagePercent = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        }

        ValidateNonOverlappingPeriod(transaction, application);
        var checkedLines = new List<PayApplicationLine>(application.Lines.Count);
        foreach (var claimLine in application.Lines)
        {
            var sovLine = GetSovLine(transaction, application.SubcontractorId, claimLine.ScheduleOfValuesLineId)
                ?? throw new InvalidOperationException($"SOV line {claimLine.ScheduleOfValuesLineId} no longer exists.");
            var priorBilled = sovLine.PreviouslyBilledSnapshot +
                              GetPriorLineClaimAmounts(transaction, claimLine.ScheduleOfValuesLineId, application.Id);
            var result = BillingRuleEvaluator.EvaluateR3(sovLine.ScheduledValue, priorBilled, claimLine.Amount);
            if (!result.IsValid)
            {
                throw new InvalidOperationException($"SOV line {sovLine.LineNumber}: {result.Message}");
            }

            checkedLines.Add(claimLine with
            {
                LineNumber = sovLine.LineNumber,
                Description = sovLine.Description,
                PreviouslyBilledSnapshot = priorBilled
            });
        }

        var priorClaims = GetClaimsForSubcontractor(transaction, application.SubcontractorId);
        var r4 = BillingRuleEvaluator.EvaluateR4(
            originalValue,
            approvedChangeOrderTotal,
            priorClaims,
            application.SubcontractorId,
            application.Id == 0 ? null : application.Id,
            total);
        if (!r4.IsValid)
        {
            throw new InvalidOperationException(r4.Message);
        }

        int id;
        string action;
        if (application.Id == 0)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pay_applications
                    (application_number, subcontractor_id, period_start, period_end, amount, notes, status, retainage_percent_snapshot)
                VALUES ($number, $subcontractorId, $start, $end, $amount, $notes, $status, $retainage)
                RETURNING id;
                """;
            AddPayApplicationParameters(command, application, number, start, end, amount, application.Status);
            command.Parameters.AddWithValue("$retainage", retainagePercent.ToString(CultureInfo.InvariantCulture));
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
                    period_start = $start, period_end = $end, amount = $amount, notes = $notes,
                    status = $status, invoice_reconciliation_status = 'Pending',
                    retainage_percent_snapshot = $retainage
                WHERE id = $id;
                """;
            AddPayApplicationParameters(command, application, number, start, end, amount, application.Status);
            command.Parameters.AddWithValue("$retainage", retainagePercent.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", application.Id);
            if (command.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException($"Pay application {application.Id} no longer exists.");
            }

            id = application.Id;
            action = "Updated";
        }

        using (var deleteLines = _connection.CreateCommand())
        {
            deleteLines.Transaction = transaction;
            deleteLines.CommandText = "DELETE FROM pay_application_lines WHERE pay_application_id = $id;";
            deleteLines.Parameters.AddWithValue("$id", id);
            deleteLines.ExecuteNonQuery();
        }

        foreach (var line in checkedLines)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO pay_application_lines
                    (pay_application_id, schedule_of_values_line_id, line_number, description,
                     previously_billed_snapshot, amount)
                VALUES ($applicationId, $sovLineId, $lineNumber, $description, $previouslyBilled, $amount);
                """;
            command.Parameters.AddWithValue("$applicationId", id);
            command.Parameters.AddWithValue("$sovLineId", line.ScheduleOfValuesLineId);
            command.Parameters.AddWithValue("$lineNumber", line.LineNumber);
            command.Parameters.AddWithValue("$description", line.Description);
            command.Parameters.AddWithValue("$previouslyBilled", line.PreviouslyBilledSnapshot.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$amount", line.Amount.ToString(CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        AddAuditEntry(transaction, "Pay application", id, action, number);
        transaction.Commit();

        return application with
        {
            Id = id,
            ApplicationNumber = number,
            Amount = total,
            RetainagePercent = retainagePercent,
            Lines = checkedLines
        };
    }

    public bool DeletePayApplication(int id)
    {
        using var transaction = _connection.BeginTransaction();
        using var numberCommand = _connection.CreateCommand();
        numberCommand.Transaction = transaction;
        numberCommand.CommandText = "SELECT application_number, status FROM pay_applications WHERE id = $id;";
        numberCommand.Parameters.AddWithValue("$id", id);
        string? number;
        using (var reader = numberCommand.ExecuteReader())
        {
            if (!reader.Read())
            {
                transaction.Commit();
                return false;
            }

            number = reader.GetString(0);
            if (Enum.Parse<ClaimStatus>(reader.GetString(1), ignoreCase: true) != ClaimStatus.Draft)
            {
                throw new InvalidOperationException("Only draft claims can be deleted.");
            }
        }

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

    private IReadOnlyList<PayApplicationLine> GetPayApplicationLines(int applicationId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT schedule_of_values_line_id, line_number, description, previously_billed_snapshot, amount
            FROM pay_application_lines
            WHERE pay_application_id = $id
            ORDER BY line_number, schedule_of_values_line_id;
            """;
        command.Parameters.AddWithValue("$id", applicationId);
        using var reader = command.ExecuteReader();
        var lines = new List<PayApplicationLine>();
        while (reader.Read())
        {
            lines.Add(new PayApplicationLine
            {
                ScheduleOfValuesLineId = reader.GetInt32(0),
                LineNumber = reader.GetInt32(1),
                Description = reader.GetString(2),
                PreviouslyBilledSnapshot = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                Amount = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture)
            });
        }

        return lines;
    }

    private static void ValidateNonOverlappingPeriod(SqliteTransaction transaction, PayApplication application)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, period_start, period_end
            FROM pay_applications
            WHERE subcontractor_id = $subcontractorId AND id <> $currentId;
            """;
        command.Parameters.AddWithValue("$subcontractorId", application.SubcontractorId);
        command.Parameters.AddWithValue("$currentId", application.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var start = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Date;
            var end = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Date;
            if (application.PeriodStart.Date <= end && application.PeriodEnd.Date >= start)
            {
                throw new InvalidOperationException("Claim periods for the same subcontract cannot overlap.");
            }
        }
    }

    private static ScheduleOfValuesLine? GetSovLine(
        SqliteTransaction transaction,
        int subcontractorId,
        int lineId)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, subcontractor_id, line_number, description, scheduled_value,
                   previously_billed_snapshot
            FROM schedule_of_values_lines
            WHERE id = $lineId AND subcontractor_id = $subcontractorId;
            """;
        command.Parameters.AddWithValue("$lineId", lineId);
        command.Parameters.AddWithValue("$subcontractorId", subcontractorId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ScheduleOfValuesLine
            {
                Id = reader.GetInt32(0),
                SubcontractorId = reader.GetInt32(1),
                LineNumber = reader.GetInt32(2),
                Description = reader.GetString(3),
                ScheduledValue = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                PreviouslyBilledSnapshot = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            }
            : null;
    }

    private static decimal GetPriorLineClaimAmounts(
        SqliteTransaction transaction,
        int sovLineId,
        int currentApplicationId)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.amount
            FROM pay_application_lines l
            INNER JOIN pay_applications p ON p.id = l.pay_application_id
            WHERE l.schedule_of_values_line_id = $lineId AND p.id <> $currentApplicationId;
            """;
        command.Parameters.AddWithValue("$lineId", sovLineId);
        command.Parameters.AddWithValue("$currentApplicationId", currentApplicationId);
        using var reader = command.ExecuteReader();
        var amount = 0m;
        while (reader.Read())
        {
            amount += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
        }

        return amount;
    }

    private static IReadOnlyList<ClaimBillingHistory> GetClaimsForSubcontractor(
        SqliteTransaction transaction,
        int subcontractorId)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, subcontractor_id, status, amount
            FROM pay_applications
            WHERE subcontractor_id = $subcontractorId;
            """;
        command.Parameters.AddWithValue("$subcontractorId", subcontractorId);
        using var reader = command.ExecuteReader();
        var claims = new List<ClaimBillingHistory>();
        while (reader.Read())
        {
            claims.Add(new ClaimBillingHistory(
                reader.GetInt32(0),
                reader.GetInt32(1),
                Enum.Parse<ClaimStatus>(reader.GetString(2), ignoreCase: true),
                decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }

        return claims;
    }

    private void InitializeDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_number TEXT NOT NULL UNIQUE COLLATE NOCASE,
                name TEXT NOT NULL,
                owner_name TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Active'
            );

            CREATE TABLE IF NOT EXISTS subcontractors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                contact_name TEXT NOT NULL DEFAULT '',
                email TEXT NOT NULL DEFAULT '',
                phone TEXT NOT NULL DEFAULT '',
                original_contract_value TEXT NOT NULL DEFAULT '0',
                approved_change_order_total TEXT NOT NULL DEFAULT '0',
                retainage_percent TEXT NOT NULL DEFAULT '0'
            );

            CREATE TABLE IF NOT EXISTS pay_applications (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                application_number TEXT NOT NULL,
                subcontractor_id INTEGER NOT NULL REFERENCES subcontractors(id) ON DELETE RESTRICT,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                amount TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Draft',
                submitted_at TEXT NULL,
                approved_at TEXT NULL,
                invoice_reconciliation_status TEXT NOT NULL DEFAULT 'Pending',
                retainage_percent_snapshot TEXT NOT NULL DEFAULT '0'
            );

            CREATE TABLE IF NOT EXISTS schedule_of_values_lines (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                subcontractor_id INTEGER NOT NULL REFERENCES subcontractors(id) ON DELETE CASCADE,
                line_number INTEGER NOT NULL,
                description TEXT NOT NULL,
                scheduled_value TEXT NOT NULL,
                previously_billed_snapshot TEXT NOT NULL DEFAULT '0',
                UNIQUE (subcontractor_id, line_number)
            );

            CREATE TABLE IF NOT EXISTS pay_application_lines (
                pay_application_id INTEGER NOT NULL REFERENCES pay_applications(id) ON DELETE CASCADE,
                schedule_of_values_line_id INTEGER NOT NULL REFERENCES schedule_of_values_lines(id) ON DELETE RESTRICT,
                line_number INTEGER NOT NULL,
                description TEXT NOT NULL,
                previously_billed_snapshot TEXT NOT NULL,
                amount TEXT NOT NULL,
                PRIMARY KEY (pay_application_id, schedule_of_values_line_id)
            );

            CREATE TABLE IF NOT EXISTS audit_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                occurred_at TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id INTEGER NOT NULL,
                action TEXT NOT NULL,
                summary TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS compliance_records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                subcontractor_id INTEGER NOT NULL REFERENCES subcontractors(id) ON DELETE CASCADE,
                type TEXT NOT NULL,
                document_number TEXT NOT NULL,
                expiry_date TEXT NULL,
                file_path TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS lien_waivers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pay_application_id INTEGER NOT NULL REFERENCES pay_applications(id) ON DELETE CASCADE,
                type TEXT NOT NULL,
                status TEXT NOT NULL,
                amount TEXT NOT NULL,
                signed_at TEXT NULL,
                file_path TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS invoices (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pay_application_id INTEGER NOT NULL REFERENCES pay_applications(id) ON DELETE CASCADE,
                invoice_number TEXT NOT NULL,
                provenance TEXT NOT NULL DEFAULT 'Manual'
            );

            CREATE TABLE IF NOT EXISTS invoice_lines (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                invoice_id INTEGER NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
                description TEXT NOT NULL,
                amount TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS pay_application_compliance_records (
                pay_application_id INTEGER NOT NULL REFERENCES pay_applications(id) ON DELETE CASCADE,
                compliance_record_id INTEGER NOT NULL REFERENCES compliance_records(id) ON DELETE RESTRICT,
                PRIMARY KEY (pay_application_id, compliance_record_id)
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumn("subcontractors", "original_contract_value", "TEXT NOT NULL DEFAULT '0'");
        EnsureColumn("subcontractors", "approved_change_order_total", "TEXT NOT NULL DEFAULT '0'");
        EnsureColumn("subcontractors", "retainage_percent", "TEXT NOT NULL DEFAULT '0'");
        EnsureColumn("pay_applications", "status", "TEXT NOT NULL DEFAULT 'Approved'");
        EnsureColumn("pay_applications", "submitted_at", "TEXT NULL");
        EnsureColumn("pay_applications", "approved_at", "TEXT NULL");
        EnsureColumn("pay_applications", "invoice_reconciliation_status", "TEXT NOT NULL DEFAULT 'Pending'");
        EnsureColumn("pay_applications", "retainage_percent_snapshot", "TEXT NOT NULL DEFAULT '0'");
        EnsureColumn("subcontractors", "project_id", "INTEGER NULL REFERENCES projects(id) ON DELETE RESTRICT");
        using var versionCommand = _connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version = 3;";
        versionCommand.ExecuteNonQuery();
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var exists = false;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1) == column)
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private static void AddSubcontractorParameters(SqliteCommand command, Subcontractor subcontractor, string name)
    {
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$contact", subcontractor.ContactName.Trim());
        command.Parameters.AddWithValue("$email", subcontractor.Email.Trim());
        command.Parameters.AddWithValue("$phone", subcontractor.Phone.Trim());
        command.Parameters.AddWithValue("$originalValue", subcontractor.OriginalContractValue.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$changeOrders", subcontractor.ApprovedChangeOrderTotal.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$retainage", subcontractor.RetainagePercent.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$projectId", (object?)subcontractor.ProjectId ?? DBNull.Value);
    }

    private static void AddPayApplicationParameters(
        SqliteCommand command,
        PayApplication application,
        string number,
        string start,
        string end,
        string amount,
        ClaimStatus status)
    {
        command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$subcontractorId", application.SubcontractorId);
        command.Parameters.AddWithValue("$start", start);
        command.Parameters.AddWithValue("$end", end);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$notes", application.Notes.Trim());
        command.Parameters.AddWithValue("$status", status.ToString());
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
