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
- Track Insurance, License, W9, SafetyCert, and Other compliance evidence, plus individual lien waivers and invoices with line-level amounts.
- Submit pay applications through PendingCompliance, PendingWaiver, PendingApproval, and Approved, with each gate revalidated against current SQLite data.
- Prevent overlapping claim periods and reject negative line claims; zero lines are allowed when the total claim is positive.
- View an append-only audit history of successful record changes.
- Keep claims and SOV lines linked to subcontractors; a subcontractor with linked claims cannot be deleted.

R1 requires at least one Insurance and License record and checks every such record through the claim period end; missing or expired records block the gate. R2 requires a signed Conditional waiver whose amount covers the gross claim. R3 limits prior billed plus the current claim to each SOV scheduled value, and R4 limits prior Approved/Paid claims plus the current claim to the original subcontract value plus approved change orders. R5 compares the combined invoice-line total with the gross claim within $0.01; no invoice is not applicable. R6 displays subcontract retainage as informational only and never deducts it.

Lifecycle timestamps use the UTC app clock, successful state changes are audited without actor identity, and approved application evidence is read-only. Existing milestone-1 pay applications migrate as approved history; new claims are drafts.

AI intake and PDF export remain out of scope.

## Verify

```powershell
dotnet test SubGuard.sln
dotnet build SubGuard.sln
```
