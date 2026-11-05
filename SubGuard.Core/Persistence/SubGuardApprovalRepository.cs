using System.Globalization;
using Microsoft.Data.Sqlite;
using SubGuard.Core.Domain;
using SubGuard.Core.Models;

namespace SubGuard.Core.Persistence;

public sealed partial class SubGuardRepository
{
    public IReadOnlyList<ComplianceRecord> GetComplianceRecords(int subcontractorId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, subcontractor_id, type, document_number, expiry_date, file_path
            FROM compliance_records
            WHERE subcontractor_id = $id
            ORDER BY type, document_number, id;
            """;
        command.Parameters.AddWithValue("$id", subcontractorId);
        using var reader = command.ExecuteReader();
        var records = new List<ComplianceRecord>();
        while (reader.Read())
        {
            records.Add(ReadComplianceRecord(reader));
        }

        return records;
    }

    public ComplianceRecord SaveComplianceRecord(ComplianceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.SubcontractorId <= 0 ||
            !Enum.IsDefined(record.Type) ||
            string.IsNullOrWhiteSpace(record.DocumentNumber))
        {
            throw new ArgumentException("Select a compliance type and enter a document number.", nameof(record));
        }

        using var transaction = _connection.BeginTransaction();
        if (record.Id != 0 && IsComplianceRecordLocked(transaction, record.Id))
        {
            throw new InvalidOperationException("Compliance evidence linked to an approved application is read-only.");
        }

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        if (record.Id == 0)
        {
            command.CommandText = """
                INSERT INTO compliance_records
                    (subcontractor_id, type, document_number, expiry_date, file_path)
                VALUES ($subcontractorId, $type, $number, $expiry, $filePath)
                RETURNING id;
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE compliance_records
                SET subcontractor_id = $subcontractorId, type = $type,
                    document_number = $number, expiry_date = $expiry, file_path = $filePath
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", record.Id);
        }

        AddComplianceParameters(command, record);
        var id = record.Id == 0
            ? Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture)
            : command.ExecuteNonQuery() == 0
                ? throw new InvalidOperationException($"Compliance record {record.Id} no longer exists.")
                : record.Id;
        AddAuditEntry(transaction, "Compliance evidence", id, record.Id == 0 ? "Created" : "Updated", record.DocumentNumber.Trim());
        transaction.Commit();
        return record with { Id = id, DocumentNumber = record.DocumentNumber.Trim(), FilePath = record.FilePath.Trim() };
    }

    public bool DeleteComplianceRecord(int id)
    {
        using var transaction = _connection.BeginTransaction();
        if (IsComplianceRecordLocked(transaction, id))
        {
            throw new InvalidOperationException("Compliance evidence linked to an approved application is read-only.");
        }

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM compliance_records WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var deleted = command.ExecuteNonQuery() > 0;
        if (deleted)
        {
            AddAuditEntry(transaction, "Compliance evidence", id, "Deleted", $"Compliance record #{id}");
        }

        transaction.Commit();
        return deleted;
    }

    public IReadOnlyList<LienWaiver> GetLienWaivers(int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, pay_application_id, type, status, amount, signed_at, file_path
            FROM lien_waivers
            WHERE pay_application_id = $id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$id", payApplicationId);
        using var reader = command.ExecuteReader();
        var items = new List<LienWaiver>();
        while (reader.Read())
        {
            items.Add(ReadLienWaiver(reader));
        }

        return items;
    }

    public LienWaiver SaveLienWaiver(LienWaiver waiver)
    {
        ArgumentNullException.ThrowIfNull(waiver);
        if (waiver.PayApplicationId <= 0 ||
            !Enum.IsDefined(waiver.Type) ||
            !Enum.IsDefined(waiver.Status) ||
            waiver.Amount < 0)
        {
            throw new ArgumentException("Select a waiver type/status and enter a non-negative amount.", nameof(waiver));
        }

        using var transaction = _connection.BeginTransaction();
        if (waiver.Id != 0)
        {
            var existingApplicationId = GetEvidenceApplicationId(transaction, "lien_waivers", waiver.Id)
                ?? throw new InvalidOperationException($"Lien waiver {waiver.Id} no longer exists.");
            if (existingApplicationId != waiver.PayApplicationId)
            {
                throw new InvalidOperationException("A lien waiver cannot be moved to another pay application.");
            }
        }

        EnsureApplicationEvidenceEditable(transaction, waiver.PayApplicationId);
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        if (waiver.Id == 0)
        {
            command.CommandText = """
                INSERT INTO lien_waivers
                    (pay_application_id, type, status, amount, signed_at, file_path)
                VALUES ($applicationId, $type, $status, $amount, $signedAt, $filePath)
                RETURNING id;
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE lien_waivers
                SET pay_application_id = $applicationId, type = $type, status = $status,
                    amount = $amount, signed_at = $signedAt, file_path = $filePath
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", waiver.Id);
        }

        AddWaiverParameters(command, waiver);
        var id = waiver.Id == 0
            ? Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture)
            : command.ExecuteNonQuery() == 0
                ? throw new InvalidOperationException($"Lien waiver {waiver.Id} no longer exists.")
                : waiver.Id;
        AddAuditEntry(transaction, "Lien waiver", id, waiver.Id == 0 ? "Created" : "Updated", $"Application #{waiver.PayApplicationId}");
        transaction.Commit();
        return waiver with { Id = id, FilePath = waiver.FilePath.Trim() };
    }

    public bool DeleteLienWaiver(int id)
    {
        using var transaction = _connection.BeginTransaction();
        var applicationId = GetEvidenceApplicationId(transaction, "lien_waivers", id);
        if (applicationId is null)
        {
            transaction.Commit();
            return false;
        }

        EnsureApplicationEvidenceEditable(transaction, applicationId.Value);
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM lien_waivers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var deleted = command.ExecuteNonQuery() > 0;
        if (deleted)
        {
            AddAuditEntry(transaction, "Lien waiver", id, "Deleted", $"Application #{applicationId}");
        }

        transaction.Commit();
        return deleted;
    }

    public IReadOnlyList<InvoiceRecord> GetInvoices(int payApplicationId)
    {
        var status = GetInvoiceReconciliationStatus(payApplicationId);
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, pay_application_id, invoice_number, provenance
            FROM invoices
            WHERE pay_application_id = $id
            ORDER BY invoice_number, id;
            """;
        command.Parameters.AddWithValue("$id", payApplicationId);
        var rows = new List<(int Id, int ApplicationId, string Number, string Provenance)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        return rows.Select(row => new InvoiceRecord
        {
            Id = row.Id,
            PayApplicationId = row.ApplicationId,
            InvoiceNumber = row.Number,
            Provenance = row.Provenance,
            ReconciliationStatus = status,
            Lines = GetInvoiceLines(row.Id)
        }).ToArray();
    }

    public InvoiceRecord SaveInvoice(InvoiceRecord invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var number = invoice.InvoiceNumber.Trim();
        if (invoice.PayApplicationId <= 0 ||
            string.IsNullOrWhiteSpace(number) ||
            invoice.Lines.Count == 0 ||
            invoice.Lines.Any(line => string.IsNullOrWhiteSpace(line.Description) || line.Amount < 0))
        {
            throw new ArgumentException("Enter an invoice number and at least one non-negative invoice line.", nameof(invoice));
        }

        using var transaction = _connection.BeginTransaction();
        if (invoice.Id != 0)
        {
            var existingApplicationId = GetEvidenceApplicationId(transaction, "invoices", invoice.Id)
                ?? throw new InvalidOperationException($"Invoice {invoice.Id} no longer exists.");
            if (existingApplicationId != invoice.PayApplicationId)
            {
                throw new InvalidOperationException("An invoice cannot be moved to another pay application.");
            }
        }

        EnsureApplicationEvidenceEditable(transaction, invoice.PayApplicationId);
        int id;
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            if (invoice.Id == 0)
            {
                command.CommandText = """
                    INSERT INTO invoices (pay_application_id, invoice_number, provenance)
                    VALUES ($applicationId, $number, $provenance)
                    RETURNING id;
                    """;
            }
            else
            {
                command.CommandText = """
                    UPDATE invoices
                    SET pay_application_id = $applicationId, invoice_number = $number, provenance = $provenance
                    WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$id", invoice.Id);
            }

            command.Parameters.AddWithValue("$applicationId", invoice.PayApplicationId);
            command.Parameters.AddWithValue("$number", number);
            command.Parameters.AddWithValue("$provenance", string.IsNullOrWhiteSpace(invoice.Provenance) ? "Manual" : invoice.Provenance.Trim());
            id = invoice.Id == 0
                ? Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture)
                : command.ExecuteNonQuery() == 0
                    ? throw new InvalidOperationException($"Invoice {invoice.Id} no longer exists.")
                    : invoice.Id;
        }

        using (var deleteLines = _connection.CreateCommand())
        {
            deleteLines.Transaction = transaction;
            deleteLines.CommandText = "DELETE FROM invoice_lines WHERE invoice_id = $id;";
            deleteLines.Parameters.AddWithValue("$id", id);
            deleteLines.ExecuteNonQuery();
        }

        foreach (var line in invoice.Lines)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO invoice_lines (invoice_id, description, amount)
                VALUES ($invoiceId, $description, $amount);
                """;
            command.Parameters.AddWithValue("$invoiceId", id);
            command.Parameters.AddWithValue("$description", line.Description.Trim());
            command.Parameters.AddWithValue("$amount", line.Amount.ToString(CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        using (var resetStatus = _connection.CreateCommand())
        {
            resetStatus.Transaction = transaction;
            resetStatus.CommandText = "UPDATE pay_applications SET invoice_reconciliation_status = 'Pending' WHERE id = $id;";
            resetStatus.Parameters.AddWithValue("$id", invoice.PayApplicationId);
            resetStatus.ExecuteNonQuery();
        }

        AddAuditEntry(transaction, "Invoice", id, invoice.Id == 0 ? "Created" : "Updated", number);
        transaction.Commit();
        return invoice with
        {
            Id = id,
            InvoiceNumber = number,
            Provenance = string.IsNullOrWhiteSpace(invoice.Provenance) ? "Manual" : invoice.Provenance.Trim(),
            Lines = invoice.Lines.Select(line => line with { InvoiceId = id, Description = line.Description.Trim() }).ToArray(),
            ReconciliationStatus = InvoiceReconciliationStatus.Pending
        };
    }

    public bool DeleteInvoice(int id)
    {
        using var transaction = _connection.BeginTransaction();
        var applicationId = GetEvidenceApplicationId(transaction, "invoices", id);
        if (applicationId is null)
        {
            transaction.Commit();
            return false;
        }

        EnsureApplicationEvidenceEditable(transaction, applicationId.Value);
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM invoices WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var deleted = command.ExecuteNonQuery() > 0;
        if (deleted)
        {
            using var resetStatus = _connection.CreateCommand();
            resetStatus.Transaction = transaction;
            resetStatus.CommandText = "UPDATE pay_applications SET invoice_reconciliation_status = 'Pending' WHERE id = $id;";
            resetStatus.Parameters.AddWithValue("$id", applicationId.Value);
            resetStatus.ExecuteNonQuery();
            AddAuditEntry(transaction, "Invoice", id, "Deleted", $"Application #{applicationId}");
        }

        transaction.Commit();
        return deleted;
    }

    public ApprovalEvaluation EvaluateApproval(int payApplicationId)
    {
        using var transaction = _connection.BeginTransaction();
        var application = GetApprovalApplication(transaction, payApplicationId)
            ?? throw new InvalidOperationException($"Pay application {payApplicationId} no longer exists.");
        var input = GetApprovalInput(transaction, application);
        var evaluation = ApprovalRuleEvaluator.EvaluateAll(input);
        transaction.Commit();
        return evaluation;
    }

    public ApprovalTransitionResult ExecuteApprovalTransition(int payApplicationId, ApprovalTransition transition)
    {
        using var transaction = _connection.BeginTransaction();
        var application = GetApprovalApplication(transaction, payApplicationId)
            ?? throw new InvalidOperationException($"Pay application {payApplicationId} no longer exists.");
        var expectedStatus = transition switch
        {
            ApprovalTransition.Submit => ClaimStatus.Draft,
            ApprovalTransition.CompleteComplianceReview => ClaimStatus.PendingCompliance,
            ApprovalTransition.CompleteWaiverReview => ClaimStatus.PendingWaiver,
            ApprovalTransition.Approve => ClaimStatus.PendingApproval,
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };
        if (application.Status != expectedStatus)
        {
            throw new InvalidOperationException($"Cannot perform {transition} while the application is {application.Status}.");
        }

        var input = GetApprovalInput(transaction, application);
        var fullEvaluation = ApprovalRuleEvaluator.EvaluateAll(input);
        var applicableRules = transition switch
        {
            ApprovalTransition.Submit => [],
            ApprovalTransition.CompleteComplianceReview => fullEvaluation.Rules
                .Where(rule => rule.RuleCode == "R1").ToArray(),
            ApprovalTransition.CompleteWaiverReview => fullEvaluation.Rules
                .Where(rule => rule.RuleCode is "R1" or "R2").ToArray(),
            ApprovalTransition.Approve => fullEvaluation.Rules,
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };
        var evaluation = new ApprovalEvaluation(
            applicableRules,
            fullEvaluation.RetainagePercent,
            fullEvaluation.RetainageAmount);
        if (transition == ApprovalTransition.Approve)
        {
            var reconciliationStatus = GetReconciliationStatus(fullEvaluation);
            using var reconciliation = _connection.CreateCommand();
            reconciliation.Transaction = transaction;
            reconciliation.CommandText = "UPDATE pay_applications SET invoice_reconciliation_status = $status WHERE id = $id;";
            reconciliation.Parameters.AddWithValue("$status", reconciliationStatus.ToString());
            reconciliation.Parameters.AddWithValue("$id", payApplicationId);
            reconciliation.ExecuteNonQuery();
        }

        if (!evaluation.CanAdvance)
        {
            transaction.Commit();
            return new ApprovalTransitionResult(false, application.Status, evaluation, "Approval requirements are not satisfied.");
        }

        var (nextStatus, action, setSubmittedAt, setApprovedAt) = transition switch
        {
            ApprovalTransition.Submit => (ClaimStatus.PendingCompliance, "Submitted", true, false),
            ApprovalTransition.CompleteComplianceReview => (ClaimStatus.PendingWaiver, "Compliance passed", false, false),
            ApprovalTransition.CompleteWaiverReview => (ClaimStatus.PendingApproval, "Waiver review passed", false, false),
            ApprovalTransition.Approve => (ClaimStatus.Approved, "Approved", false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(transition))
        };
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE pay_applications
                SET status = $status,
                    submitted_at = CASE WHEN $setSubmitted = 1 THEN $now ELSE submitted_at END,
                    approved_at = CASE WHEN $setApproved = 1 THEN $now ELSE approved_at END
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$status", nextStatus.ToString());
            update.Parameters.AddWithValue("$setSubmitted", setSubmittedAt ? 1 : 0);
            update.Parameters.AddWithValue("$setApproved", setApprovedAt ? 1 : 0);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", payApplicationId);
            update.ExecuteNonQuery();
        }

        if (transition == ApprovalTransition.Approve)
        {
            using var linkRecords = _connection.CreateCommand();
            linkRecords.Transaction = transaction;
            linkRecords.CommandText = """
                INSERT OR IGNORE INTO pay_application_compliance_records (pay_application_id, compliance_record_id)
                SELECT $applicationId, id
                FROM compliance_records
                WHERE subcontractor_id = $subcontractorId;
                """;
            linkRecords.Parameters.AddWithValue("$applicationId", payApplicationId);
            linkRecords.Parameters.AddWithValue("$subcontractorId", application.SubcontractorId);
            linkRecords.ExecuteNonQuery();
        }

        AddAuditEntry(transaction, "Pay application", payApplicationId, action, application.ApplicationNumber);
        transaction.Commit();
        return new ApprovalTransitionResult(true, nextStatus, evaluation, $"{action}.");
    }

    private ApprovalEvaluationInput GetApprovalInput(SqliteTransaction transaction, PayApplication application)
    {
        var subcontractor = GetSubcontractor(transaction, application.SubcontractorId)
            ?? throw new InvalidOperationException($"Subcontractor {application.SubcontractorId} no longer exists.");
        var compliance = application.Status is ClaimStatus.Approved or ClaimStatus.Paid
            ? GetApprovedComplianceRecords(transaction, application.Id)
            : GetComplianceRecords(transaction, application.SubcontractorId);
        var waivers = GetLienWaivers(transaction, application.Id);
        var invoices = GetInvoices(transaction, application.Id);
        var lines = GetApprovalLines(transaction, application);
        var claims = GetClaimsForSubcontractor(transaction, application.SubcontractorId);
        return new ApprovalEvaluationInput(application, subcontractor, claims, lines, compliance, waivers, invoices);
    }

    private IReadOnlyList<ComplianceRecord> GetApprovedComplianceRecords(
        SqliteTransaction transaction,
        int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT record.id, record.subcontractor_id, record.type, record.document_number,
                   record.expiry_date, record.file_path
            FROM pay_application_compliance_records link
            INNER JOIN compliance_records record ON record.id = link.compliance_record_id
            WHERE link.pay_application_id = $id
            ORDER BY record.type, record.document_number, record.id;
            """;
        command.Parameters.AddWithValue("$id", payApplicationId);
        using var reader = command.ExecuteReader();
        var records = new List<ComplianceRecord>();
        while (reader.Read())
        {
            records.Add(ReadComplianceRecord(reader));
        }

        return records;
    }

    private PayApplication? GetApprovalApplication(SqliteTransaction transaction, int id)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.id, p.application_number, p.subcontractor_id, s.name,
                   p.period_start, p.period_end, p.amount, p.notes, p.status,
                   p.submitted_at, p.approved_at, p.retainage_percent_snapshot,
                   p.invoice_reconciliation_status
            FROM pay_applications p
            INNER JOIN subcontractors s ON s.id = p.subcontractor_id
            WHERE p.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        PayApplication application;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            application = new PayApplication
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
                SubmittedAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ApprovedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                RetainagePercent = decimal.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
                InvoiceReconciliationStatus = Enum.Parse<InvoiceReconciliationStatus>(reader.GetString(12), ignoreCase: true)
            };
        }

        return application with { Lines = GetPayApplicationLines(transaction, id) };
    }

    private IReadOnlyList<ApprovalLine> GetApprovalLines(SqliteTransaction transaction, PayApplication application)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.schedule_of_values_line_id, l.line_number, l.description,
                   s.scheduled_value, s.previously_billed_snapshot, l.amount
            FROM pay_application_lines l
            INNER JOIN schedule_of_values_lines s ON s.id = l.schedule_of_values_line_id
            WHERE l.pay_application_id = $applicationId
            ORDER BY l.line_number, l.schedule_of_values_line_id;
            """;
        command.Parameters.AddWithValue("$applicationId", application.Id);
        var rows = new List<(int LineId, int Number, string Description, decimal Scheduled, decimal Snapshot, decimal Claim)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                    decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
            }
        }

        var lines = new List<ApprovalLine>(rows.Count);
        foreach (var row in rows)
        {
            using var prior = _connection.CreateCommand();
            prior.Transaction = transaction;
            prior.CommandText = """
                SELECT l.amount
                FROM pay_application_lines l
                INNER JOIN pay_applications p ON p.id = l.pay_application_id
                WHERE l.schedule_of_values_line_id = $lineId AND p.id <> $applicationId;
                """;
            prior.Parameters.AddWithValue("$lineId", row.LineId);
            prior.Parameters.AddWithValue("$applicationId", application.Id);
            var previouslyClaimed = 0m;
            using (var reader = prior.ExecuteReader())
            {
                while (reader.Read())
                {
                    previouslyClaimed += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
                }
            }

            lines.Add(new ApprovalLine(
                row.Number,
                row.Description,
                decimal.Parse(row.Scheduled.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                row.Snapshot + previouslyClaimed,
                row.Claim));
        }

        return lines;
    }

    private Subcontractor? GetSubcontractor(SqliteTransaction transaction, int id)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, name, contact_name, email, phone, original_contract_value,
                   approved_change_order_total, retainage_percent
            FROM subcontractors
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new Subcontractor
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ContactName = reader.GetString(2),
                Email = reader.GetString(3),
                Phone = reader.GetString(4),
                OriginalContractValue = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                ApprovedChangeOrderTotal = decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                RetainagePercent = decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
            }
            : null;
    }

    private IReadOnlyList<ComplianceRecord> GetComplianceRecords(SqliteTransaction transaction, int subcontractorId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, subcontractor_id, type, document_number, expiry_date, file_path
            FROM compliance_records
            WHERE subcontractor_id = $id
            ORDER BY type, document_number, id;
            """;
        command.Parameters.AddWithValue("$id", subcontractorId);
        using var reader = command.ExecuteReader();
        var items = new List<ComplianceRecord>();
        while (reader.Read())
        {
            items.Add(ReadComplianceRecord(reader));
        }

        return items;
    }

    private IReadOnlyList<LienWaiver> GetLienWaivers(SqliteTransaction transaction, int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, pay_application_id, type, status, amount, signed_at, file_path
            FROM lien_waivers
            WHERE pay_application_id = $id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$id", payApplicationId);
        using var reader = command.ExecuteReader();
        var items = new List<LienWaiver>();
        while (reader.Read())
        {
            items.Add(ReadLienWaiver(reader));
        }

        return items;
    }

    private IReadOnlyList<InvoiceRecord> GetInvoices(SqliteTransaction transaction, int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, pay_application_id, invoice_number, provenance
            FROM invoices
            WHERE pay_application_id = $id
            ORDER BY invoice_number, id;
            """;
        command.Parameters.AddWithValue("$id", payApplicationId);
        var rows = new List<(int Id, int ApplicationId, string Number, string Provenance)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var status = GetInvoiceReconciliationStatus(transaction, payApplicationId);
        return rows.Select(row => new InvoiceRecord
        {
            Id = row.Id,
            PayApplicationId = row.ApplicationId,
            InvoiceNumber = row.Number,
            Provenance = row.Provenance,
            Lines = GetInvoiceLines(transaction, row.Id),
            ReconciliationStatus = status
        }).ToArray();
    }

    private IReadOnlyList<InvoiceLine> GetInvoiceLines(SqliteTransaction transaction, int invoiceId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, invoice_id, description, amount
            FROM invoice_lines
            WHERE invoice_id = $id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$id", invoiceId);
        using var reader = command.ExecuteReader();
        var lines = new List<InvoiceLine>();
        while (reader.Read())
        {
            lines.Add(new InvoiceLine
            {
                Id = reader.GetInt32(0),
                InvoiceId = reader.GetInt32(1),
                Description = reader.GetString(2),
                Amount = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)
            });
        }

        return lines;
    }

    private IReadOnlyList<InvoiceLine> GetInvoiceLines(int invoiceId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, invoice_id, description, amount
            FROM invoice_lines
            WHERE invoice_id = $id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$id", invoiceId);
        using var reader = command.ExecuteReader();
        var lines = new List<InvoiceLine>();
        while (reader.Read())
        {
            lines.Add(new InvoiceLine
            {
                Id = reader.GetInt32(0),
                InvoiceId = reader.GetInt32(1),
                Description = reader.GetString(2),
                Amount = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)
            });
        }

        return lines;
    }

    private IReadOnlyList<PayApplicationLine> GetPayApplicationLines(SqliteTransaction transaction, int applicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
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

    private bool IsComplianceRecordLocked(SqliteTransaction transaction, int complianceRecordId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pay_application_compliance_records link
                INNER JOIN pay_applications application ON application.id = link.pay_application_id
                WHERE link.compliance_record_id = $id
                  AND application.status IN ('Approved', 'Paid')
            );
            """;
        command.Parameters.AddWithValue("$id", complianceRecordId);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private void EnsureApplicationEvidenceEditable(SqliteTransaction transaction, int applicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status FROM pay_applications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", applicationId);
        var status = command.ExecuteScalar() as string
            ?? throw new InvalidOperationException($"Pay application {applicationId} no longer exists.");
        if (Enum.Parse<ClaimStatus>(status, ignoreCase: true) is ClaimStatus.Approved or ClaimStatus.Paid)
        {
            throw new InvalidOperationException("Evidence linked to an approved application is read-only.");
        }
    }

    private int? GetEvidenceApplicationId(SqliteTransaction transaction, string table, int id)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = table switch
        {
            "invoices" => "SELECT pay_application_id FROM invoices WHERE id = $id;",
            "lien_waivers" => "SELECT pay_application_id FROM lien_waivers WHERE id = $id;",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        command.Parameters.AddWithValue("$id", id);
        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private InvoiceReconciliationStatus GetInvoiceReconciliationStatus(int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT invoice_reconciliation_status FROM pay_applications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", payApplicationId);
        var value = command.ExecuteScalar() as string;
        return value is null
            ? InvoiceReconciliationStatus.Pending
            : Enum.Parse<InvoiceReconciliationStatus>(value, ignoreCase: true);
    }

    private InvoiceReconciliationStatus GetInvoiceReconciliationStatus(SqliteTransaction transaction, int payApplicationId)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT invoice_reconciliation_status FROM pay_applications WHERE id = $id;";
        command.Parameters.AddWithValue("$id", payApplicationId);
        var value = command.ExecuteScalar() as string;
        return value is null
            ? InvoiceReconciliationStatus.Pending
            : Enum.Parse<InvoiceReconciliationStatus>(value, ignoreCase: true);
    }

    private static InvoiceReconciliationStatus GetReconciliationStatus(ApprovalEvaluation evaluation)
    {
        var invoiceRule = evaluation.Rules.Single(rule => rule.RuleCode == "R5");
        return invoiceRule.State switch
        {
            ApprovalRuleState.NotApplicable => InvoiceReconciliationStatus.NotApplicable,
            ApprovalRuleState.Passed => InvoiceReconciliationStatus.Matched,
            ApprovalRuleState.Failed => InvoiceReconciliationStatus.Mismatch,
            _ => InvoiceReconciliationStatus.Pending
        };
    }

    private static ComplianceRecord ReadComplianceRecord(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(0),
            SubcontractorId = reader.GetInt32(1),
            Type = Enum.Parse<ComplianceType>(reader.GetString(2), ignoreCase: true),
            DocumentNumber = reader.GetString(3),
            ExpiryDate = reader.IsDBNull(4)
                ? null
                : DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            FilePath = reader.GetString(5)
        };

    private static LienWaiver ReadLienWaiver(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(0),
            PayApplicationId = reader.GetInt32(1),
            Type = Enum.Parse<WaiverType>(reader.GetString(2), ignoreCase: true),
            Status = Enum.Parse<WaiverStatus>(reader.GetString(3), ignoreCase: true),
            Amount = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            SignedAt = reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            FilePath = reader.GetString(6)
        };

    private static void AddComplianceParameters(SqliteCommand command, ComplianceRecord record)
    {
        command.Parameters.AddWithValue("$subcontractorId", record.SubcontractorId);
        command.Parameters.AddWithValue("$type", record.Type.ToString());
        command.Parameters.AddWithValue("$number", record.DocumentNumber.Trim());
        command.Parameters.AddWithValue(
            "$expiry",
            record.ExpiryDate is null
                ? DBNull.Value
                : record.ExpiryDate.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$filePath", record.FilePath.Trim());
    }

    private static void AddWaiverParameters(SqliteCommand command, LienWaiver waiver)
    {
        command.Parameters.AddWithValue("$applicationId", waiver.PayApplicationId);
        command.Parameters.AddWithValue("$type", waiver.Type.ToString());
        command.Parameters.AddWithValue("$status", waiver.Status.ToString());
        command.Parameters.AddWithValue("$amount", waiver.Amount.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$signedAt",
            waiver.SignedAt is null
                ? DBNull.Value
                : waiver.SignedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$filePath", waiver.FilePath.Trim());
    }
}
