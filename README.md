# SubGuard Desktop

SubGuard Desktop is an offline-first Windows WPF starter for managing subcontractors and their pay applications. This first milestone provides local CRUD and an audit history; it does not evaluate approval or compliance policies.

## Run

Requires the .NET 10 SDK and Windows.

```powershell
dotnet restore SubGuard.sln
dotnet run --project SubGuard.Desktop\SubGuard.Desktop.csproj
```

The SQLite database is created at `%LOCALAPPDATA%\SubGuard\subguard.db`.

## Included

- Create, edit, and delete subcontractor records.
- Create, edit, and delete pay applications, with period dates, amount, and notes.
- View an append-only audit history of successful record changes.
- Keep pay applications linked to subcontractors; a subcontractor with linked applications cannot be deleted.

R1–R6 policy enforcement, AI intake, and PDF export are intentionally deferred until their requirements are confirmed.

## Verify

```powershell
dotnet test SubGuard.sln
dotnet build SubGuard.sln
```
