using Microsoft.Data.Sqlite;
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
        var subcontractor = repository.SaveSubcontractor(new Subcontractor { Name = "Acme Electrical" });

        var saved = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-001",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 1, 1),
            PeriodEnd = new DateTime(2026, 1, 31),
            Amount = 12345.67m,
            Notes = "January work"
        });

        var application = Assert.Single(repository.GetPayApplications());
        Assert.Equal(saved.Id, application.Id);
        Assert.Equal("Acme Electrical", application.SubcontractorName);
        Assert.Equal(12345.67m, application.Amount);
        Assert.Throws<SqliteException>(() => repository.DeleteSubcontractor(subcontractor.Id));
        Assert.Single(repository.GetSubcontractors());
        Assert.Equal(2, repository.GetAuditEntries().Count);
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
        var subcontractor = repository.SaveSubcontractor(new Subcontractor { Name = "Summit Plumbing" });
        var created = repository.SavePayApplication(new PayApplication
        {
            ApplicationNumber = "PAY-100",
            SubcontractorId = subcontractor.Id,
            PeriodStart = new DateTime(2026, 3, 1),
            PeriodEnd = new DateTime(2026, 3, 31),
            Amount = 500m
        });

        var updated = repository.SavePayApplication(created with { Amount = 750m, Notes = "Revised" });

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(750m, Assert.Single(repository.GetPayApplications()).Amount);
        Assert.True(repository.DeletePayApplication(updated.Id));
        Assert.Empty(repository.GetPayApplications());
        Assert.Equal(
            ["Deleted", "Updated", "Created", "Created"],
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

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"subguard-tests-{Guid.NewGuid():N}");

        public SubGuardRepository OpenRepository() =>
            new(Path.Combine(_directory, "subguard.db"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
