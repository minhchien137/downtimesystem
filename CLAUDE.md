# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run (development)
dotnet run

# Publish (Windows x64 release)
dotnet publish -c Release -r win-x64

# EF Core migrations
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

There are no automated tests in this project.

## Architecture

**Stack:** ASP.NET Core 8 MVC + SignalR, Entity Framework Core 8, SQL Server

The application is a real-time machine downtime tracking system for a manufacturing floor. It is served under the path prefix `/downtime` (set via `UsePathBase` in `Program.cs`) and is designed to run behind an nginx reverse proxy.

### Role-based access

Authentication is session-based (no ASP.NET Identity). Roles are stored in session and enforced manually in controllers:
- **Production** – operators who log STOP/RUN events
- **Technical** – technicians who respond to downtime calls
- **DRI** – department representatives (PDE, QUAL, FAC, IT) who handle escalations
- **Admin** – full access

### Downtime workflow (state machine)

The core business flow is a multi-actor escalation chain:

```
Operator → STOP event (SVN_Downtime_Infos_Devel, State="STOP")
         → SignalR broadcast → TechnicianGroup

Technician → reviews → if "Not E&F issue" (PIENotEF)
           → notifies Operator to "Call DRI"

Operator → ProdCallDRI() → SignalR → DRIGroup (by department)

DRI → accepts / resolves / escalates

Operator → RUN event (State="RUN") → closes cycle
```

Key controller actions: `StatusController.CreateDownTime`, `PIENotEF`, `ProdCallDRI`.

### Real-time communication

`DowntimeHub` (SignalR) uses named groups for targeted delivery:
- `TechnicianGroup` – all technicians
- `Operator_[username]` – individual operator
- `DRIGroup` – all DRI members

Hub methods called from client JS correspond 1:1 to server-side `IHubContext<DowntimeHub>` calls in `StatusController`.

### Anomaly detection

`AnomalyDetectionService` calls the SQL Server stored procedure `SVN_DetectAnomalies`, which returns multiple result sets. The service maps them to anomaly records with types (Spike, Frequency, Severity) and levels (Warning, Critical) based on Z-score analysis. Results are displayed in `AnomalyController`.

### Database

All tables are prefixed `SVN_` or `SM_`. Key tables:
- `SVN_Downtime_Infos_Devel` – downtime event records
- `SVN_Downtime_TechResponses` – tech team responses (FK → Infos_Devel)
- `SVN_Notifications` – audit trail for all notifications
- `SVN_Downtime_Accounts` – user credentials and roles
- `SM_EmployInfo` – employee master data (used for DRI lookups)

`ApplicationDbContext` is the single DbContext. Some entities (e.g. `SVN_target`) are keyless (`HasNoKey()`).

### Key libraries
- **ClosedXML** – Excel report export
- **Tesseract + ZXing.Net + SkiaSharp** – OCR and barcode/QR reading from uploaded images
- **Newtonsoft.Json** – JSON serialization in controllers
