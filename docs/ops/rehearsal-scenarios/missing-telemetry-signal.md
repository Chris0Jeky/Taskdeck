# Scenario: Missing Telemetry Signal

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Overview

Simulate a condition where the request correlation ID (`X-Request-Id`) is missing from OpenTelemetry trace spans. Diagnose whether the issue is in middleware configuration, telemetry export pipeline, or span attribute propagation.

## Pre-Conditions

- Repository checked out at a known commit on `main`.
- Backend builds successfully: `dotnet build backend/Taskdeck.sln -c Release`
- OpenTelemetry console exporter enabled for local inspection (no external collector required).
- `curl` or equivalent HTTP client available.

## Injection Method

### Option A: Disable Correlation Middleware

Temporarily comment out or misconfigure the request correlation middleware registration in `Program.cs` to simulate a deployment where correlation IDs stop being propagated to trace spans.

For a non-code-change rehearsal: start the API with OpenTelemetry enabled and verify correlation attributes are present, then start a second instance with `Observability:EnableOpenTelemetry=false` and observe the absence.

```bash
# Start with telemetry enabled and console exporter
Observability__EnableOpenTelemetry=true \
Observability__EnableConsoleExporter=true \
Observability__ServiceName=taskdeck-rehearsal \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

### Option B: Missing OTLP Endpoint

Configure the API to export to a non-existent OTLP collector endpoint. Traces will be generated but silently dropped.

```bash
Observability__EnableOpenTelemetry=true \
Observability__OtlpEndpoint=http://localhost:4317 \
Observability__EnableConsoleExporter=false \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

This simulates a production scenario where the collector is down or misconfigured.

## Expected Diagnosis Path

1. **Make a request and check for correlation headers**:
   ```bash
   # Send a request and inspect response headers
   curl -v http://localhost:5000/health/live 2>&1 | grep -i x-request-id
   ```
   The API should echo back an `X-Request-Id` header. If missing, correlation middleware is not running.

2. **Check console exporter output** (if enabled):
   Look for trace spans in the API console output. Expected attributes:
   - `taskdeck.correlation_id`
   - `taskdeck.request_id`

   If these attributes are absent from spans but the `X-Request-Id` header is present in the HTTP response, the issue is in span attribute propagation (middleware runs but does not tag spans).

3. **Verify OpenTelemetry configuration**:
   ```bash
   # Check appsettings for telemetry config
   cat backend/src/Taskdeck.Api/appsettings.json | grep -A5 Observability
   cat backend/src/Taskdeck.Api/appsettings.Development.json | grep -A5 Observability
   ```

4. **Check for OTLP export errors**:
   If the OTLP endpoint is unreachable, the OpenTelemetry SDK may log warnings. Look for messages containing `OTLP`, `export`, or `gRPC` errors in the console output.

5. **Verify worker spans include expected attributes**:
   ```bash
   # Trigger a worker cycle by submitting a queue item, then check console for worker span attributes
   # Look for: taskdeck.worker.name, taskdeck.llm.request_id
   ```

## Recovery Steps

### Correlation Middleware Missing

1. Verify the middleware registration order in `Program.cs`.
2. Confirm the correlation middleware runs before the endpoint routing middleware.
3. Restart the API and verify `X-Request-Id` appears in response headers.

### OTLP Endpoint Unreachable

1. Verify the collector is running:
   ```bash
   curl -s http://localhost:4317 || echo "OTLP endpoint unreachable"
   ```
2. Fix the endpoint URL in configuration or start the collector.
3. Restart the API (or wait for the next export interval).

### Console Exporter Not Showing Spans

1. Verify `Observability:EnableConsoleExporter` is `true`.
2. Verify `Observability:EnableOpenTelemetry` is `true`.
3. Check that the `Taskdeck.Api` activity source name matches the configured source in the telemetry setup.

## Evidence Checklist

- [ ] Captured output showing the presence or absence of `X-Request-Id` in response headers
- [ ] Console exporter output showing trace spans with or without `taskdeck.correlation_id`
- [ ] Configuration values for `Observability:*` settings used during the rehearsal
- [ ] If OTLP was tested: evidence of export failure (log lines or connection errors)
- [ ] Commands used to diagnose the telemetry pipeline
- [ ] Recovery steps taken and verification of restored telemetry
- [ ] Any findings about error visibility when telemetry export silently fails

## Related Documents

- `docs/ops/OBSERVABILITY_BASELINE.md` -- telemetry contract and expected attributes
- `backend/src/Taskdeck.Api/Telemetry/` -- custom telemetry instrumentation
- `backend/src/Taskdeck.Api/appsettings.json` -- observability configuration
