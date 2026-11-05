# SubGuard Desktop

SubGuard Desktop is an offline-first Windows WPF app for managing subcontractors, schedules of values, and pay applications.

## Run

Requires the .NET 10 SDK and Windows.

```powershell
dotnet restore SubGuard.sln
dotnet run --project SubGuard.Desktop\SubGuard.Desktop.csproj
```

The SQLite database is created at `%LOCALAPPDATA%\SubGuard\subguard.db`.

## Included

- Create, edit, and delete subcontractor records.
- Maintain each subcontract's original value, approved change-order total, and SOV lines.
- Enter pay applications as SOV line claims with live R3/R4 validation and a save-time recheck.
- Prevent overlapping claim periods and reject negative line claims; zero lines are allowed when the total claim is positive.
- View an append-only audit history of successful record changes.
- Keep claims and SOV lines linked to subcontractors; a subcontractor with linked claims cannot be deleted.

R3 limits prior billed plus the current claim to each SOV scheduled value. R4 limits prior approved/paid claims plus the current claim to the original subcontract value plus approved change orders. Existing milestone-1 pay applications migrate as approved history; new claims are drafts. Drafts remain editable; approved/paid history is read-only so it cannot be removed from billing-cap calculations.

R1, R2, R5, R6, approval workflow, AI intake, and PDF export remain out of scope.

## Verify

```powershell
dotnet test SubGuard.sln
dotnet build SubGuard.sln
```
