# ADR-0022: Analytics Export — CSV First, PDF Deferred

- **Status**: Accepted
- **Date**: 2026-04-08
- **Deciders**: Project maintainers

## Context

Issue #78 (ANL-02) requires exportable analytics reports with reproducible filters. The original scope called for both CSV and PDF formats. This ADR records the decision to ship CSV first and defer PDF export.

## Decision

Ship CSV export as the initial analytics export format. Defer PDF export to a future iteration.

CSV export includes:
- Schema version header for forward compatibility
- Documented column layout per section (Summary, Throughput, CycleTime, WIP, Blocked)
- CSV-injection-safe field sanitization (leading `=`, `+`, `-`, `@`, tab, CR stripped)
- Reproducible filter parameters embedded as comment headers (board_id, from, to, exported_at)
- UTF-8 BOM for reliable Excel opening

## Alternatives Considered

- **Ship both CSV and PDF simultaneously**: PDF generation requires either a third-party library (e.g., QuestPDF, iTextSharp) or an HTML-to-PDF headless browser pipeline. Both add significant dependency weight to a local-first SQLite application. The marginal user value of PDF over CSV is low for developer-facing analytics, since CSV is directly importable into spreadsheets, scripts, and BI tools. Rejected for this iteration.

- **PDF only**: CSV is more machine-friendly and more useful for the developer audience. PDF is a presentation format better suited for stakeholder reports, which is not a current Taskdeck use case. Rejected.

- **Server-side PDF via Chromium headless**: Adds ~300 MB runtime dependency. Entirely disproportionate for a local-first tool. Rejected.

## Consequences

- CSV export is available immediately via `GET /api/metrics/boards/{boardId}/export`.
- PDF export can be added later with minimal disruption — the export pipeline is designed with a format-agnostic interface (`IMetricsExportService`).
- If PDF is later needed, the recommended approach is a lightweight .NET PDF library (e.g., QuestPDF) added to the Infrastructure layer, keeping Application layer pure.

## References

- Issue: #78 (ANL-02: Exportable analytics reports)
- Related: `BoardMetricsService`, `MetricsExportService`
