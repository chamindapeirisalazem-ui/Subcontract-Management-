using Microsoft.Data.Sqlite;
using SubGuard.Core.Domain;
using SubGuard.Core.Models;
using SubGuard.Core.Persistence;

namespace SubGuard.Core.Tests;

public sealed class SubGuardRepositoryTests
{
    [Fact]
    public void SavesUpdatesAndDeletesSubcontractorWithAuditHistory()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();

        var created = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Northwind Concrete",
            ContactName = "Alex",
            Email = "alex@example.test",
            Phone = "555-0100"
        });

        var updated = repository.SaveSubcontractor(created with { Name = "Northwind Concrete LLC" });

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Northwind Concrete LLC", Assert.Single(repository.GetSubcontractors()).Name);
        Assert.True(repository.DeleteSubcontractor(updated.Id));
        Assert.Empty(repository.GetSubcontractors());
        Assert.Equal(
            ["Deleted", "Updated", "Created"],
            repository.GetAuditEntries().Select(entry => entry.Action));
    }

    [Fact]
    public void SavesPayApplicationAndPreventsDeletingItsSubcontractor()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var subcontractor = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Acme Electrical",
            OriginalContractValue = 20000m
        });
        var scheduleLine = Assert.Single(repository.SaveScheduleOfValues(subcontractor.Id,
        [
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 1,
                Description = "Electrical rough-in",
                ScheduledValue = 20000m
            }
        ]));

        var saved = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-001",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Amount = 12345.67m,
            Notes = "January work",
            Lines =
            [
                new PayApplicationLine
                {
                    ScheduleOfValuesLineId = scheduleLine.Id,
                    Amount = 12345.67m
                }
            ]
        });

        var application = Assert.Single(repository.GetPayApplications());
        Assert.Equal(saved.Id, application.Id);
        Assert.Equal("Acme Electrical", application.SubcontractorName);
        Assert.Equal(12345.67m, application.Amount);
        Assert.Throws<SqliteException>(() => repository.DeleteSubcontractor(subcontractor.Id));
        Assert.Single(repository.GetSubcontractors());
        Assert.Equal(3, repository.GetAuditEntries().Count);
    }

    [Fact]
    public void RecordsPersistAcrossRepositoryReopen()
    {
        using var database = new TemporaryDatabase();
        int savedId;
        using (var repository = database.OpenRepository())
        {
            savedId = repository.SaveSubcontractor(new Subcontractor { Name = "Persistent Builder" }).Id;
        }

        using var reopened = database.OpenRepository();
        var restored = Assert.Single(reopened.GetSubcontractors());
        Assert.Equal(savedId, restored.Id);
        Assert.Equal("Persistent Builder", restored.Name);
        Assert.Single(reopened.GetAuditEntries());
    }

    [Fact]
    public void UpdatesAndDeletesPayApplicationWithAuditHistory()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var subcontractor = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Summit Plumbing",
            OriginalContractValue = 1000m
        });
        var scheduleLine = Assert.Single(repository.SaveScheduleOfValues(subcontractor.Id,
        [
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 1,
                Description = "Plumbing",
                ScheduledValue = 1000m
            }
        ]));
        var created = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-100",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 3, 1),
            PeriodEnd = new DateTime(2026, 3, 31),
            Amount = 500m,
            Lines =
            [
                new PayApplicationLine
                {
                    ScheduleOfValuesLineId = scheduleLine.Id,
                    Amount = 500m
                }
            ]
        });

        var updated = repository.SavePayApplication(created with
        {
            Amount = 750m,
            Notes = "Revised",
            Lines = [created.Lines[0] with { Amount = 750m }]
        });

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(750m, Assert.Single(repository.GetPayApplications()).Amount);
        Assert.True(repository.DeletePayApplication(updated.Id));
        Assert.Empty(repository.GetPayApplications());
        Assert.Equal(
            ["Deleted", "Updated", "Created", "Updated", "Created"],
            repository.GetAuditEntries().Select(entry => entry.Action));
        Assert.Equal("PAY-100", repository.GetAuditEntries()[0].Summary);
    }

    [Fact]
    public void RejectsInvalidPayApplicationPeriod()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var subcontractor = repository.SaveSubcontractor(new Subcontractor { Name = "Valid Builder" });

        Assert.Throws<ArgumentException>(() => repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-002",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 2, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Amount = 1m
        }));
        Assert.Empty(repository.GetPayApplications());
    }

    [Fact]
    public void SavePayApplicationEnforcesR3AndR4AgainstPersistedValues()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var subcontractor = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Cap Builder",
            OriginalContractValue = 1000m,
            ApprovedChangeOrderTotal = 100m
        });
        var scheduleLines = repository.SaveScheduleOfValues(subcontractor.Id,
        [
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 1,
                Description = "Line at cap",
                ScheduledValue = 1000m,
                PreviouslyBilledSnapshot = 50m
            },
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 2,
                Description = "Contract value test",
                ScheduledValue = 2000m
            }
        ]);
        var r3Line = scheduleLines[0];
        var r4Line = scheduleLines[1];

        var prior = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-200",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Amount = 950m,
            Lines = [NewClaimLine(r4Line.Id, 950m)]
        });
        database.SetClaimStatus(prior.Id, ClaimStatus.Approved);

        Assert.Equal(950m, Assert.Single(repository.GetPayApplications()).Amount);
        Assert.Throws<InvalidOperationException>(() => repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-201",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 2, 1),
            PeriodEnd = new DateTime(2026, 2, 28),
            Amount = 951m,
            Lines = [NewClaimLine(r3Line.Id, 951m)]
        }));

        Assert.Equal(1, prior.Id);

        Assert.Throws<InvalidOperationException>(() => repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-202",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 2, 1),
            PeriodEnd = new DateTime(2026, 2, 28),
            Amount = 151m,
            Lines = [NewClaimLine(r4Line.Id, 151m)]
        }));
    }

    [Fact]
    public void MigratesExistingPayApplicationsAsApprovedHistory()
    {
        using var database = new TemporaryDatabase();
        Directory.CreateDirectory(Path.GetDirectoryName(database.DatabasePath)!);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE subcontractors (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    contact_name TEXT NOT NULL DEFAULT '',
                    email TEXT NOT NULL DEFAULT '',
                    phone TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE pay_applications (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    application_number TEXT NOT NULL,
                    subcontractor_id INTEGER NOT NULL REFERENCES subcontractors(id),
                    period_start TEXT NOT NULL,
                    period_end TEXT NOT NULL,
                    amount TEXT NOT NULL,
                    notes TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE audit_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_at TEXT NOT NULL,
                    entity_type TEXT NOT NULL,
                    entity_id INTEGER NOT NULL,
                    action TEXT NOT NULL,
                    summary TEXT NOT NULL
                );
                INSERT INTO subcontractors (name) VALUES ('Legacy Builder');
                INSERT INTO pay_applications
                    (application_number, subcontractor_id, period_start, period_end, amount, notes)
                VALUES ('LEGACY-1', 1, '2026-01-01T00:00:00.0000000', '2026-01-31T00:00:00.0000000', '250', '');
                """;
            command.ExecuteNonQuery();
        }

        using var repository = database.OpenRepository();
        var subcontractor = Assert.Single(repository.GetSubcontractors());
        var claim = Assert.Single(repository.GetPayApplications());

        Assert.Equal(0m, subcontractor.OriginalContractValue);
        Assert.Equal(ClaimStatus.Approved, claim.Status);
        Assert.Equal(250m, claim.Amount);
        Assert.Equal(0m, claim.RetainagePercent);
        Assert.Null(claim.SubmittedAt);
        Assert.Equal(InvoiceReconciliationStatus.Pending, claim.InvoiceReconciliationStatus);
        Assert.Empty(claim.Lines);
        Assert.Throws<InvalidOperationException>(() => repository.DeletePayApplication(claim.Id));
    }

    [Fact]
    public void RejectsOverlappingClaimPeriods()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var (subcontractor, scheduleLine) = CreateSubcontractWithLine(repository);
        repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-300",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Lines = [NewClaimLine(scheduleLine.Id, 100m)]
        });

        var exception = Assert.Throws<InvalidOperationException>(() => repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-301",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 31),
            PeriodEnd = new DateTime(2026, 2, 28),
            Lines = [NewClaimLine(scheduleLine.Id, 100m)]
        }));

        Assert.Contains("cannot overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovalWorkflowRevalidatesEvidenceAndFreezesApprovedRecords()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var (subcontractor, _, application) = CreateApprovalClaim(repository);
        AddRequiredCompliance(repository, subcontractor.Id);

        var submitted = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Submit);
        Assert.True(submitted.Succeeded);
        Assert.Equal(ClaimStatus.PendingCompliance, submitted.Status);
        var pendingCompliance = Assert.Single(repository.GetPayApplications());
        Assert.NotNull(pendingCompliance.SubmittedAt);

        var compliancePassed = repository.ExecuteApprovalTransition(
            application.Id,
            ApprovalTransition.CompleteComplianceReview);
        Assert.True(compliancePassed.Succeeded);
        Assert.Equal(ClaimStatus.PendingWaiver, compliancePassed.Status);

        var missingWaiver = repository.ExecuteApprovalTransition(
            application.Id,
            ApprovalTransition.CompleteWaiverReview);
        Assert.False(missingWaiver.Succeeded);
        Assert.Equal(ClaimStatus.PendingWaiver, missingWaiver.Status);
        Assert.Contains(missingWaiver.Evaluation.Rules, rule =>
            rule.RuleCode == "R2" && rule.State == ApprovalRuleState.Failed);

        repository.SaveLienWaiver(new LienWaiver
        {
            PayApplicationId = application.Id,
            Type = WaiverType.Conditional,
            Status = WaiverStatus.Signed,
            Amount = 500m
        });
        var waiverPassed = repository.ExecuteApprovalTransition(
            application.Id,
            ApprovalTransition.CompleteWaiverReview);
        Assert.True(waiverPassed.Succeeded);
        Assert.Equal(ClaimStatus.PendingApproval, waiverPassed.Status);

        repository.SaveInvoice(new InvoiceRecord
        {
            PayApplicationId = application.Id,
            InvoiceNumber = "INV-1",
            Lines = [new InvoiceLine { Description = "Labor", Amount = 200m }]
        });
        repository.SaveInvoice(new InvoiceRecord
        {
            PayApplicationId = application.Id,
            InvoiceNumber = "INV-2",
            Lines = [new InvoiceLine { Description = "Materials", Amount = 300m }]
        });

        var approved = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Approve);
        Assert.True(approved.Succeeded);
        Assert.Equal(ClaimStatus.Approved, approved.Status);
        Assert.Equal(50m, approved.Evaluation.RetainageAmount);
        var persisted = Assert.Single(repository.GetPayApplications());
        Assert.NotNull(persisted.ApprovedAt);
        Assert.Equal(InvoiceReconciliationStatus.Matched, persisted.InvoiceReconciliationStatus);
        Assert.Equal(2, repository.GetInvoices(application.Id).Count);
        Assert.Equal(
            ["Approved", "Waiver review passed", "Compliance passed", "Submitted", "Created"],
            repository.GetAuditEntries()
                .Where(entry => entry.EntityType == "Pay application")
                .Select(entry => entry.Action));
        repository.SaveComplianceRecord(new ComplianceRecord
        {
            SubcontractorId = subcontractor.Id,
            Type = ComplianceType.Insurance,
            DocumentNumber = "INS-LATER",
            ExpiryDate = new DateTime(2026, 1, 15)
        });
        Assert.All(
            repository.EvaluateApproval(application.Id).Rules.Where(rule => rule.RuleCode == "R1"),
            rule => Assert.Equal(ApprovalRuleState.Passed, rule.State));
        Assert.Throws<InvalidOperationException>(() => repository.DeleteLienWaiver(
            repository.GetLienWaivers(application.Id)[0].Id));
        Assert.Throws<InvalidOperationException>(() => repository.DeleteInvoice(
            repository.GetInvoices(application.Id)[0].Id));
        Assert.Throws<InvalidOperationException>(() => repository.SaveComplianceRecord(
            repository.GetComplianceRecords(subcontractor.Id)[0] with { DocumentNumber = "Changed" }));
        Assert.DoesNotContain(repository.GetAuditEntries(), entry => entry.Action.Contains("blocked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FinalApprovalRechecksR3AndR4AndPersistsInvoiceMismatchWithoutChangingStatus()
    {
        using var database = new TemporaryDatabase();
        using var repository = database.OpenRepository();
        var (subcontractor, scheduleLine, application) = CreateApprovalClaim(repository);
        AddRequiredCompliance(repository, subcontractor.Id);
        repository.SaveLienWaiver(new LienWaiver
        {
            PayApplicationId = application.Id,
            Type = WaiverType.Conditional,
            Status = WaiverStatus.Signed,
            Amount = 500m
        });
        repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Submit);
        repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.CompleteComplianceReview);
        repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.CompleteWaiverReview);

        repository.SaveInvoice(new InvoiceRecord
        {
            PayApplicationId = application.Id,
            InvoiceNumber = "INV-MISMATCH",
            Lines = [new InvoiceLine { Description = "Work", Amount = 499.98m }]
        });
        var mismatch = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Approve);
        Assert.False(mismatch.Succeeded);
        Assert.Equal(ClaimStatus.PendingApproval, mismatch.Status);
        Assert.Contains(mismatch.Evaluation.Rules, rule =>
            rule.RuleCode == "R5" && rule.State == ApprovalRuleState.Failed);
        Assert.Equal(InvoiceReconciliationStatus.Mismatch, Assert.Single(repository.GetPayApplications()).InvoiceReconciliationStatus);

        repository.DeleteInvoice(repository.GetInvoices(application.Id)[0].Id);
        repository.SaveScheduleOfValues(subcontractor.Id,
        [
            scheduleLine with { ScheduledValue = 400m }
        ]);
        var r3Failure = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Approve);
        Assert.False(r3Failure.Succeeded);
        Assert.Contains(r3Failure.Evaluation.Rules, rule =>
            rule.RuleCode == "R3" && rule.State == ApprovalRuleState.Failed);

        repository.SaveScheduleOfValues(subcontractor.Id,
        [
            scheduleLine with { ScheduledValue = 1000m }
        ]);
        repository.SaveSubcontractor(subcontractor with { OriginalContractValue = 400m });
        var r4Failure = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Approve);
        Assert.False(r4Failure.Succeeded);
        Assert.Equal(ClaimStatus.PendingApproval, r4Failure.Status);
        Assert.Contains(r4Failure.Evaluation.Rules, rule =>
            rule.RuleCode == "R4" && rule.State == ApprovalRuleState.Failed);

        repository.SaveSubcontractor(subcontractor with { OriginalContractValue = 1000m });
        var noInvoiceApproval = repository.ExecuteApprovalTransition(application.Id, ApprovalTransition.Approve);
        Assert.True(noInvoiceApproval.Succeeded);
        Assert.Equal(
            InvoiceReconciliationStatus.NotApplicable,
            Assert.Single(repository.GetPayApplications()).InvoiceReconciliationStatus);
    }

    private static (Subcontractor Subcontractor, ScheduleOfValuesLine Line, PayApplication Application)
        CreateApprovalClaim(SubGuardRepository repository)
    {
        var subcontractor = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Approval Builder",
            OriginalContractValue = 1000m,
            RetainagePercent = 10m
        });
        var line = Assert.Single(repository.SaveScheduleOfValues(subcontractor.Id,
        [
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 1,
                Description = "Work",
                ScheduledValue = 1000m
            }
        ]));
        var application = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "APPROVAL-1",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Lines = [NewClaimLine(line.Id, 500m)]
        });
        return (subcontractor, line, application);
    }

    private static void AddRequiredCompliance(SubGuardRepository repository, int subcontractorId)
    {
        repository.SaveComplianceRecord(new ComplianceRecord
        {
            SubcontractorId = subcontractorId,
            Type = ComplianceType.Insurance,
            DocumentNumber = "INS-1",
            ExpiryDate = new DateTime(2026, 1, 31)
        });
        repository.SaveComplianceRecord(new ComplianceRecord
        {
            SubcontractorId = subcontractorId,
            Type = ComplianceType.License,
            DocumentNumber = "LIC-1",
            ExpiryDate = new DateTime(2026, 1, 31)
        });
    }

    private static PayApplicationLine NewClaimLine(int lineId, decimal amount) =>
        new()
        {
            ScheduleOfValuesLineId = lineId,
            Amount = amount
        };

    private static (Subcontractor Subcontractor, ScheduleOfValuesLine Line) CreateSubcontractWithLine(
        SubGuardRepository repository)
    {
        var subcontractor = repository.SaveSubcontractor(new Subcontractor
        {
            Name = "Period Builder",
            OriginalContractValue = 1000m
        });
        var line = Assert.Single(repository.SaveScheduleOfValues(subcontractor.Id,
        [
            new ScheduleOfValuesLine
            {
                SubcontractorId = subcontractor.Id,
                LineNumber = 1,
                Description = "Work",
                ScheduledValue = 1000m
            }
        ]));
        return (subcontractor, line);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"subguard-tests-{Guid.NewGuid():N}");

        public string DatabasePath => Path.Combine(_directory, "subguard.db");

        public SubGuardRepository OpenRepository() =>
            new(DatabasePath);

        public void SetClaimStatus(int id, ClaimStatus status)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE pay_applications SET status = $status WHERE id = $id;";
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$id", id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
