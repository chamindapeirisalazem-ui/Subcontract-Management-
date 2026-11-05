using Microsoft.Data.Sqlite;
using SubGuard.Core.Models;
using SubGuard.Core.Persistence;

namespace SubGuard.Core.Tests;

public sealed class ProjectRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"subguard-projects-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_directory, "subguard.db");

    [Fact]
    public void SavesUpdatesAndDeletesProjectsWithAudit()
    {
        using var repository = new SubGuardRepository(DatabasePath);

        var created = repository.SaveProject(new Project
        {
            ProjectNumber = " P-100 ",
            Name = "Harbor Tower",
            OwnerName = "Harbor LLC"
        });
        var closed = repository.SaveProject(created with { Status = ProjectStatus.Closed });

        Assert.Equal("P-100", closed.ProjectNumber);
        var stored = Assert.Single(repository.GetProjects());
        Assert.Equal(ProjectStatus.Closed, stored.Status);
        Assert.Equal("Harbor LLC", stored.OwnerName);
        Assert.True(repository.DeleteProject(created.Id));
        Assert.False(repository.DeleteProject(created.Id));
        Assert.Equal(["Deleted", "Updated", "Created"], repository.GetAuditEntries().Select(e => e.Action));
    }

    [Fact]
    public void RejectsDuplicateProjectNumbersAndBlankRequiredFields()
    {
        using var repository = new SubGuardRepository(DatabasePath);
        repository.SaveProject(new Project { ProjectNumber = "P-1", Name = "One" });

        Assert.Throws<InvalidOperationException>(() =>
            repository.SaveProject(new Project { ProjectNumber = "p-1", Name = "Two" }));
        Assert.ThrowsAny<ArgumentException>(() =>
            repository.SaveProject(new Project { ProjectNumber = " ", Name = "Three" }));
        Assert.Single(repository.GetProjects());
    }

    [Fact]
    public void LinksSubcontractsToProjectsAndBlocksDeletingProjectsInUse()
    {
        using var repository = new SubGuardRepository(DatabasePath);
        var project = repository.SaveProject(new Project { ProjectNumber = "P-2", Name = "Depot" });
        var subcontract = repository.SaveSubcontractor(new Subcontractor { Name = "Apex", ProjectId = project.Id });

        Assert.Equal(project.Id, Assert.Single(repository.GetSubcontractors()).ProjectId);
        Assert.Throws<InvalidOperationException>(() => repository.DeleteProject(project.Id));

        repository.SaveSubcontractor(subcontract with { ProjectId = null });
        Assert.Null(Assert.Single(repository.GetSubcontractors()).ProjectId);
        Assert.True(repository.DeleteProject(project.Id));
    }

    [Fact]
    public void MigratesExistingDatabaseWithoutLosingSubcontractData()
    {
        Directory.CreateDirectory(_directory);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = DatabasePath,
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
                    phone TEXT NOT NULL DEFAULT '',
                    original_contract_value TEXT NOT NULL DEFAULT '0',
                    approved_change_order_total TEXT NOT NULL DEFAULT '0',
                    retainage_percent TEXT NOT NULL DEFAULT '0'
                );
                INSERT INTO subcontractors (name, original_contract_value) VALUES ('Legacy Electric', '1500');
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }

        using var repository = new SubGuardRepository(DatabasePath);

        var legacy = Assert.Single(repository.GetSubcontractors());
        Assert.Equal("Legacy Electric", legacy.Name);
        Assert.Equal(1500m, legacy.OriginalContractValue);
        Assert.Null(legacy.ProjectId);
        Assert.Empty(repository.GetProjects());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
