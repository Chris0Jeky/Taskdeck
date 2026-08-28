using System.Text.RegularExpressions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Architecture.Tests;

/// <summary>
/// CI-visible tests for the 12 roadmap invariants defined in the v4 roadmap.
/// These guard the safety floor for the review-first automation model.
///
/// Convention:
///   - Invariants testable against the current codebase have passing [Fact] tests.
///   - Invariants requiring unbuilt infrastructure use [Fact(Skip = "...")] with
///     clear scope comments describing what is needed.
/// </summary>
// INV-11 mutates TelemetryGuard's process-wide static options via Configure(). This class
// joins the "TelemetryGuardGlobalState" collection (anchored by [CollectionDefinition] in
// TelemetryGuardGlobalStateCollection.cs) so any future TelemetryGuard-touching test class in
// THIS assembly CAN opt into serialization by joining the same collection (xUnit runs distinct
// classes in parallel by default). Today RoadmapInvariantTests is the only member, so the live
// INV-11 isolation still comes from its Configure/restore-in-finally pattern; the collection
// just makes the convention real and discoverable. (xUnit collections serialize within a single
// assembly only — TelemetryGuardTests lives in Taskdeck.Application.Tests, a separate process.)
[Collection("TelemetryGuardGlobalState")]
public class RoadmapInvariantTests
{
    // ─── Helpers ────────────────────────────────────────────────────────

    private static IReadOnlyList<string> GetSourceFiles(string relativeDir)
    {
        var dir = ArchitectureTestPaths.GetBackendPath(relativeDir);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string ReadFile(string path) => File.ReadAllText(path);

    // ─── Invariant 1: No automation surface mutates boards directly ─────

    /// <summary>
    /// INV-01: No automation surface mutates boards directly.
    /// Automation code paths (Capture, Chat, MCP, tool executors) must produce
    /// proposals, not direct board mutations. Manual board UI (BoardController)
    /// actions are excluded from this constraint.
    ///
    /// Strategy: scan automation-surface source files for direct board mutation
    /// calls (CardService.CreateCardAsync, ColumnService.CreateColumnAsync, etc.)
    /// and assert they only appear in the executor pipeline (which runs after
    /// proposal approval) or in manual UI controllers.
    /// </summary>
    [Fact]
    public void Invariant01_AutomationSurfaces_DoNotMutateBoards_Directly()
    {
        // Automation surfaces that must NOT call board mutation methods directly.
        // Scan the entire Application/Services tree plus MCP tools so that new
        // automation entrypoints (ChatService, CaptureService, etc.) are covered.
        var automationDirs = new[]
        {
            "src/Taskdeck.Application/Services",           // all application services
            "src/Taskdeck.Api/Mcp",                        // MCP tools
        };

        // Patterns indicating direct board mutation (these should go through proposals)
        var directMutationPatterns = new[]
        {
            @"_boardService\.\s*(?:DeleteBoard|ArchiveBoard)Async",
            @"_cardService\.\s*(?:CreateCard|UpdateCard|DeleteCard|MoveCard|ArchiveCard)Async",
            @"_columnService\.\s*(?:CreateColumn|UpdateColumn|DeleteColumn)Async",
        };

        // Files that are explicitly allowed to call mutations:
        // - operation handlers in the executor pipeline (run AFTER approval)
        // - the AutomationExecutorService itself (delegates to pipeline)
        // - the core service implementations (BoardService, CardService, ColumnService)
        //   which ARE the mutation layer, not consumers of it
        var allowedFilePatterns = new[]
        {
            "OperationHandler",       // Pipeline handlers execute approved operations
            "ExecutionAuditRecorder",  // Records audit for executed operations
            "AutomationExecutorService", // Executor delegates to pipeline after approval
            "BoardService",           // Core service — IS the mutation layer
            "CardService",            // Core service — IS the mutation layer
            "ColumnService",          // Core service — IS the mutation layer
        };

        var violations = new List<string>();

        foreach (var dir in automationDirs)
        {
            foreach (var file in GetSourceFiles(dir))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (allowedFilePatterns.Any(p => fileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var content = ReadFile(file);
                foreach (var pattern in directMutationPatterns)
                {
                    var matches = Regex.Matches(content, pattern);
                    foreach (Match match in matches)
                    {
                        violations.Add(
                            $"{ArchitectureTestPaths.ToBackendRelativePath(file)}: " +
                            $"direct board mutation call '{match.Value}' found in automation surface");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"INV-01 violation: automation surfaces must produce proposals, not direct mutations.{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations));
    }

    // ─── Invariant 2: Chat write tools create proposals only ────────────

    /// <summary>
    /// INV-02: Chat write tools create proposals only.
    /// Every write tool schema name must start with "propose_" indicating it
    /// creates a proposal rather than performing a direct mutation.
    /// </summary>
    [Fact]
    public void Invariant02_WriteToolSchemas_CreateProposalsOnly()
    {
        // Scan WriteToolSchemas.cs for method definitions and verify naming
        var writeToolSchemaFiles = GetSourceFiles("src/Taskdeck.Application/Services/Tools")
            .Where(f => Path.GetFileName(f).Equals("WriteToolSchemas.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(writeToolSchemaFiles.Count > 0, "WriteToolSchemas.cs not found");

        var content = ReadFile(writeToolSchemaFiles[0]);

        // Extract all tool names from the schema definitions
        var toolNamePattern = new Regex(@"Name:\s*""([^""]+)""", RegexOptions.Compiled);
        var toolNames = toolNamePattern.Matches(content)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.True(toolNames.Count > 0, "No tool names found in WriteToolSchemas.cs");

        var violations = toolNames
            .Where(name => !name.StartsWith("propose_", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"INV-02 violation: all write tool schemas must create proposals (name starts with 'propose_'). " +
            $"Violating tools: {string.Join(", ", violations)}");
    }

    // ─── Invariant 3: MCP exposes proposal CRUD — never approve_proposal ──

    /// <summary>
    /// INV-03: MCP exposes proposal create/read/status — never approve_proposal.
    /// Scan all MCP tool classes for registered tool names and assert that
    /// "approve_proposal" is absent.
    /// </summary>
    [Fact]
    public void Invariant03_McpTools_NeverExposeApproveProposal()
    {
        var mcpFiles = GetSourceFiles("src/Taskdeck.Api/Mcp");
        Assert.True(mcpFiles.Count > 0, "No MCP tool files found");

        var toolNamePattern = new Regex(@"\[McpServerTool\s*\(\s*Name\s*=\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
        var allToolNames = new List<string>();

        foreach (var file in mcpFiles)
        {
            var content = ReadFile(file);
            var names = toolNamePattern.Matches(content)
                .Select(m => m.Groups[1].Value)
                .ToList();
            allToolNames.AddRange(names);
        }

        Assert.True(allToolNames.Count > 0, "No MCP tools found across MCP source files");

        var approveTools = allToolNames
            .Where(n => n.Contains("approve", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            approveTools.Count == 0,
            $"INV-03 violation: MCP must never expose approve_proposal. " +
            $"Found: {string.Join(", ", approveTools)}");
    }

    // ─── Invariant 4: Agents cannot approve proposals ──────────────────

    /// <summary>
    /// INV-04: Agents cannot approve proposals.
    /// The tool-calling orchestrator's tool bundles must not include any
    /// tool whose name contains "approve". Scan the orchestrator and tool
    /// registry source for registered tool names.
    /// </summary>
    [Fact]
    public void Invariant04_AgentToolBundles_CannotApproveProposals()
    {
        // Scan the ToolCallingChatOrchestrator and tool schemas
        var orchestratorFiles = GetSourceFiles("src/Taskdeck.Application/Services")
            .Where(f => Path.GetFileName(f).Contains("ToolCalling", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).Contains("ToolSchema", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).Contains("ToolExecutor", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).Contains("ToolRegistry", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Also scan the Tools subdirectory
        orchestratorFiles.AddRange(GetSourceFiles("src/Taskdeck.Application/Services/Tools"));

        var toolNamePattern = new Regex(@"(?:Name[:\s]*""([^""]+)""|""(approve_[^""]*)""\s*)", RegexOptions.Compiled);
        var allToolNames = new List<string>();

        foreach (var file in orchestratorFiles.Distinct())
        {
            var content = ReadFile(file);
            var names = toolNamePattern.Matches(content)
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
            allToolNames.AddRange(names);
        }

        var approveTools = allToolNames
            .Where(n => n.Contains("approve", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            approveTools.Count == 0,
            $"INV-04 violation: agent tool bundles must not contain approve_proposal. " +
            $"Found: {string.Join(", ", approveTools)}");
    }

    // ─── Invariant 5: Integrations cannot approve proposals ────────────

    /// <summary>
    /// INV-05: Integrations cannot approve proposals.
    /// Same check as INV-04 applied to integration adapter source.
    /// Since Taskdeck's integration adapters currently go through the same
    /// MCP tool surface, this test reuses the MCP scan and also checks
    /// any integration-specific directories.
    /// </summary>
    [Fact]
    public void Invariant05_IntegrationAdapters_CannotApproveProposals()
    {
        // Check both MCP tools (primary integration surface) and any
        // integration-specific adapter code
        var integrationDirs = new[]
        {
            "src/Taskdeck.Api/Mcp",
            "src/Taskdeck.Infrastructure/Integrations",
            "src/Taskdeck.Application/Integrations",
        };

        var toolNamePattern = new Regex(
            @"(?:\[McpServerTool\s*\(\s*Name\s*=\s*""([^""]+)""\s*\)|""(approve_[^""]*)""\s*)",
            RegexOptions.Compiled);

        var allToolNames = new List<string>();

        foreach (var dir in integrationDirs)
        {
            foreach (var file in GetSourceFiles(dir))
            {
                var content = ReadFile(file);
                var names = toolNamePattern.Matches(content)
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
                allToolNames.AddRange(names);
            }
        }

        var approveTools = allToolNames
            .Where(n => n.Contains("approve", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            approveTools.Count == 0,
            $"INV-05 violation: integration adapters must not contain approve_proposal. " +
            $"Found: {string.Join(", ", approveTools)}");
    }

    // ─── Invariant 6: Proposal execution requires Status == Approved ───

    /// <summary>
    /// INV-06: Proposal execution requires Status == Approved.
    /// Verify that AutomationExecutorService rejects proposals that are not
    /// in Approved status. This is a source-level assertion that the guard
    /// exists in the execution path.
    /// </summary>
    [Fact]
    public void Invariant06_ProposalExecution_RequiresApprovedStatus()
    {
        var executorFiles = GetSourceFiles("src/Taskdeck.Application/Services")
            .Where(f => Path.GetFileName(f).Equals("AutomationExecutorService.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(executorFiles.Count > 0, "AutomationExecutorService.cs not found");

        var content = ReadFile(executorFiles[0]);

        // The service must check for Approved status before executing
        Assert.Contains("ProposalStatus.Approved", content);

        // And it must reject non-Approved proposals
        Assert.Contains("Cannot execute proposal in status", content);
    }

    // ─── Invariant 7: Proposal execution uses idempotency keys and
    //     expected-version checks ────────────────────────────────────────

    /// <summary>
    /// INV-07: Proposal execution uses idempotency keys and expected-version checks.
    /// Verify that:
    ///   a) The executor requires an idempotencyKey parameter
    ///   b) Already-applied proposals are treated idempotently (not re-executed)
    /// </summary>
    [Fact]
    public void Invariant07_ProposalExecution_UsesIdempotencyKeys()
    {
        var executorFiles = GetSourceFiles("src/Taskdeck.Application/Services")
            .Where(f => Path.GetFileName(f).Equals("AutomationExecutorService.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(executorFiles.Count > 0, "AutomationExecutorService.cs not found");

        var content = ReadFile(executorFiles[0]);

        // Must accept idempotencyKey
        Assert.Contains("idempotencyKey", content);

        // Must reject empty idempotency key
        Assert.Contains("IdempotencyKey cannot be empty", content);

        // Must handle already-applied proposals idempotently
        Assert.Contains("ProposalStatus.Applied", content);
    }

    // ─── Invariant 8: EgressEnvelope — all outbound HTTP constrained ───

    /// <summary>
    /// INV-08: EgressEnvelope — all outbound HTTP constrained.
    /// Scans backend source files for HttpClient construction, IHttpClientFactory
    /// usage, and AddHttpClient registrations. Maintains an expected list of
    /// registered outbound sites. Fails if a new unregistered usage appears.
    /// </summary>
    [Fact]
    public void Invariant08_EgressEnvelope_OutboundHttpConstrained()
    {
        var srcDirs = new[]
        {
            "src/Taskdeck.Application",
            "src/Taskdeck.Infrastructure",
            "src/Taskdeck.Api",
        };

        var httpPatterns = new Regex(
            @"new\s+HttpClient\s*\(|" +                      // direct construction
            @"IHttpClientFactory|" +                          // factory injection
            @"\.AddHttpClient\s*[<(]|" +                      // DI registration
            @"HttpClient\s+\w+\s*=|" +                        // local assignment
            @"HttpClient\s+_?\w+\s*[;,]",                     // field declaration or ctor parameter
            RegexOptions.Compiled);

        // Known/expected outbound HTTP usage sites (file names without extension)
        // Update this list when adding new legitimate outbound HTTP callsites.
        var expectedSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OpenAiLlmProvider",
            "OpenAiCompatibleLlmProvider",
            "OllamaLlmProvider",
            "OutboundWebhookDeliveryWorker",
            "WorkerRegistration",
            "DependencyInjection",
            "LlmProviderRegistration",
            "GitHubConnectorProvider",     // typed-client for GitHub API health check
            "Program",
        };

        var unknownSites = new List<string>();

        foreach (var dir in srcDirs)
        {
            foreach (var file in GetSourceFiles(dir))
            {
                var content = ReadFile(file);
                if (!httpPatterns.IsMatch(content))
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(file);
                if (!expectedSites.Contains(fileName))
                {
                    unknownSites.Add(
                        $"{ArchitectureTestPaths.ToBackendRelativePath(file)} uses HttpClient but is not in the expected outbound sites list");
                }
            }
        }

        Assert.True(
            unknownSites.Count == 0,
            $"INV-08 violation: unregistered outbound HTTP usage detected. " +
            $"Add the file to the expectedSites list in this test if legitimate, or " +
            $"route through EgressEnvelope when implemented.{Environment.NewLine}" +
            string.Join(Environment.NewLine, unknownSites));
    }

    // ─── Invariant 9: Where-your-data-goes registry ────────────────────

    /// <summary>
    /// INV-09: Where-your-data-goes registry.
    /// Requires a DataFlowRegistry that enumerates every external destination
    /// user data can reach, with purpose and legal basis annotations.
    /// </summary>
    [Fact(Skip = "TODO: requires DataFlowRegistry implementation — tracks all external data destinations with purpose/legal-basis annotations")]
    public void Invariant09_WhereYourDataGoes_Registry()
    {
        // When implemented, this test should:
        // 1. Instantiate DataFlowRegistry
        // 2. Enumerate all registered destinations
        // 3. Assert each has a non-empty Purpose and LegalBasis
        // 4. Cross-reference with INV-08 outbound HTTP sites
    }

    // ─── Invariant 10: MCP tool hash-pinning ───────────────────────────

    /// <summary>
    /// INV-10: MCP tool definition hash mechanism.
    /// This invariant verifies deterministic detection of changes to a declared
    /// tool's name, description, or input schema. It does not assert runtime
    /// approval or invocation enforcement: the shipped code has no user-driven
    /// definition-recording or approval lifecycle.
    /// </summary>
    [Fact]
    public void Invariant10_McpToolDefinitionHashes_DetectDefinitionDrift()
    {
        // McpToolDefinitionHashService hashes a tool's (name, description, inputSchema) so
        // definition changes are detectable. It is mechanism-only until a user-driven approval
        // lifecycle can supply records and approvals to an invocation-time enforcement path.
        const string name = "propose_create_card";
        const string description = "Create a card via a proposal.";
        const string schema = "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"}}}";

        var hash = McpToolDefinitionHashService.ComputeDefinitionHash(name, description, schema);

        // Non-empty, lowercase 64-char hex SHA-256.
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);

        // Deterministic for identical definitions.
        Assert.Equal(hash, McpToolDefinitionHashService.ComputeDefinitionHash(name, description, schema));

        // Change-detecting: any field change yields a different hash.
        Assert.NotEqual(hash, McpToolDefinitionHashService.ComputeDefinitionHash(name + "_v2", description, schema));
        Assert.NotEqual(hash, McpToolDefinitionHashService.ComputeDefinitionHash(name, description + " (v2)", schema));
        Assert.NotEqual(hash, McpToolDefinitionHashService.ComputeDefinitionHash(name, description, schema + " "));

        // Moving a character across the name/description boundary changes the hash.
        Assert.NotEqual(
            McpToolDefinitionHashService.ComputeDefinitionHash("ab", "c", schema),
            McpToolDefinitionHashService.ComputeDefinitionHash("a", "bc", schema));

        // The property the length prefix specifically guards: inputs that would COLLIDE under
        // bare-delimiter concatenation (both render "a|b|c|<schema>") stay distinct because their
        // length prefixes differ ("N:1:a|D:3:b|c" vs "N:3:a|b|D:1:c"). This would regress if the
        // framing dropped the length prefixes but kept the '|' delimiters.
        Assert.NotEqual(
            McpToolDefinitionHashService.ComputeDefinitionHash("a", "b|c", schema),
            McpToolDefinitionHashService.ComputeDefinitionHash("a|b", "c", schema));

        // Connect the invariant to the declared MCP tool surface. This guards the mechanism and
        // the existing inventory; it deliberately does not claim invocation-time enforcement.
        var mcpToolNames = GetSourceFiles("src/Taskdeck.Api/Mcp")
            .SelectMany(f => Regex.Matches(ReadFile(f), @"\[McpServerTool\s*\(\s*Name\s*=\s*""([^""]+)""\s*\)")
                .Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        var expectedMcpToolNames = new[]
        {
            "archive_card",
            "create_capture",
            "create_card",
            "create_column",
            "dismiss_proposal",
            "get_board_summary",
            "get_proposal_status",
            "list_proposals",
            "move_card",
            "search_cards",
            "update_card",
        };

        Assert.Equal(expectedMcpToolNames, mcpToolNames.OrderBy(name => name, StringComparer.Ordinal));

        // The hash-service lifecycle has no production caller. This is intentional: enabling a
        // deny gate before users can record and approve definitions would deny every tool, while
        // automatic approval would not be user approval. When a user-driven lifecycle is introduced,
        // replace this assertion with end-to-end approved, missing, and stale-definition invocation tests.
        var approvalLifecycleMethods = new[]
        {
            "IsToolApprovedAsync(",
            "RecordToolDefinitionAsync(",
            "ApproveToolAsync(",
        };
        var approvalLifecycleCallers = GetSourceFiles("src")
            .Where(file => !Path.GetFileName(file).Equals(
                "McpToolDefinitionHashService.cs", StringComparison.OrdinalIgnoreCase))
            .Where(file => approvalLifecycleMethods.Any(method =>
                ReadFile(file).Contains(method, StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(approvalLifecycleCallers);

        var toolHashes = mcpToolNames
            .Select(n => McpToolDefinitionHashService.ComputeDefinitionHash(n, description, schema))
            .ToList();
        Assert.All(toolHashes, h => Assert.Matches("^[0-9a-f]{64}$", h));
        Assert.Equal(mcpToolNames.Count, toolHashes.Distinct().Count()); // distinct real tools -> no hash collisions
    }

    // ─── Invariant 11: Local analytics no user content ─────────────────

    /// <summary>
    /// INV-11: Local analytics contains no user content.
    /// TelemetryGuard ensures analytics events never include PII, card content,
    /// board names, or other user-generated data.
    /// </summary>
    [Fact]
    public void Invariant11_LocalAnalytics_NoUserContent()
    {
        // TelemetryGuard enforces an allowlist of content-free metric keys and rejects PII /
        // user content in values. It holds process-global static options, so configure to the
        // shipped default allowlist for a known baseline and restore it in finally to keep this
        // assembly isolated (xUnit gives no cross-class ordering guarantee).
        TelemetryGuard.Configure(new TelemetryGuardOptions());
        try
        {
            // Allowed: bucketed numeric counts and enumerated string values.
            Assert.True(TelemetryGuard.Validate("capture.count", 5).IsValid);
            Assert.True(TelemetryGuard.Validate("workspace.mode", "guided").IsValid);

            // Rejected: keys not on the allowlist (no arbitrary metric names).
            Assert.False(TelemetryGuard.Validate("user.email", "anything").IsValid);

            // Rejected: PII in an allowlisted key's value (email + URL), including encoded bypass.
            Assert.False(TelemetryGuard.Validate("workspace.mode", "user@example.com").IsValid);
            Assert.False(TelemetryGuard.Validate("workspace.mode", "https://example.com/u/42").IsValid);
            Assert.False(TelemetryGuard.Validate("workspace.mode", "user%40example.com").IsValid);

            // Rejected: complex objects that could smuggle user content past primitive checks.
            Assert.False(TelemetryGuard.Validate("capture.count", new { secret = "data" }).IsValid);

            // Rejected: free-text string on a numeric-only key (no user content via the value shape).
            Assert.False(TelemetryGuard.Validate("capture.count", "free text").IsValid);
        }
        finally
        {
            TelemetryGuard.Configure(new TelemetryGuardOptions());
        }
    }

    // ─── Invariant 12: Proposal evidence references source payload ───────

    /// <summary>
    /// INV-12: the integrity contract of a proposal's provenance chain.
    /// This is a domain-contract spec, not a completeness scan. It deliberately does NOT assert
    /// that every proposal carries provenance (nothing in the shipped model forces that), nor that
    /// every field carries evidence (the non-transcript path legally produces inferred fields with
    /// zero evidence links). It asserts only what the domain types enforce about the chain
    /// (<see cref="ProposalProvenance"/> → <see cref="ProvenanceField"/> →
    /// <see cref="ProvenanceEvidenceLink"/>) once it is built: an evidence link resolves to a
    /// concrete source-payload span (e.g. a transcript range); an extractive field must carry the
    /// quote it claims; transcript evidence must reference its transcript; spans are ordered; and
    /// a field or link cannot be attached across different outputs.
    ///
    /// Rewritten under issue #1305 AC3: the original RFAI-02 spike vocabulary
    /// (SourceSpan / IntentCandidate / EvidenceLink / IntentEnvelopeV1) was unmapped, table-less
    /// scaffolding and was removed. The shipped evidence-span capability lives in the mapped
    /// <c>ProvenanceEvidenceLink</c> / <c>ProvenanceField</c> tables, so the same guards are now
    /// asserted against those types — preserving the intent, not merely deleting the test.
    /// </summary>
    [Fact]
    public void Invariant12_ProposalEvidence_ReferencesSourcePayload()
    {
        // An automation output (a proposal) carries a provenance chain that ties each derived
        // field back to the source payload it came from.
        var proposalId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        var provenance = new ProposalProvenance(proposalId, "corr-123", "mock");

        // An extractive field must carry the verbatim quote it was extracted from — this guards
        // against fabricated evidence (an extraction claim with nothing behind it).
        var field = new ProvenanceField(
            "Title", ProvenanceKind.Extractive, confidence: 0.9, provenance.Id,
            extractiveQuote: "ship the API review card");
        provenance.AddField(field);

        // The evidence link resolves to a concrete source-payload span: a transcript range.
        var link = new ProvenanceEvidenceLink(
            ProvenanceEvidenceLink.TranscriptSourceType,
            transcriptId.ToString("D"),
            field.Id,
            label: "contains the request",
            spanStart: 10,
            spanEnd: 34,
            transcriptId: transcriptId);
        field.AddEvidenceLink(link);

        // The chain resolves end to end: output → field → evidence → source-payload span.
        Assert.Equal(proposalId, provenance.ProposalId);            // provenance ties to the output
        var boundField = Assert.Single(provenance.Fields);
        Assert.Equal(provenance.Id, boundField.ProposalProvenanceId);
        var boundLink = Assert.Single(boundField.EvidenceLinks);
        Assert.Equal(field.Id, boundLink.ProvenanceFieldId);        // evidence resolves back to the field
        Assert.Equal(ProvenanceEvidenceLink.TranscriptSourceType, boundLink.SourceType);
        Assert.Equal(transcriptId, boundLink.TranscriptId);         // ...and to the originating payload
        Assert.Equal(transcriptId.ToString("D"), boundLink.SourceId);
        Assert.Equal(10, boundLink.SpanStart);
        Assert.Equal(34, boundLink.SpanEnd);

        // Integrity is enforced so INV-12 cannot go false-green:

        // (a) an extractive field with no quote is rejected — no unbacked extraction claims.
        Assert.Throws<DomainException>(() =>
            new ProvenanceField("Title", ProvenanceKind.Extractive, 0.9, provenance.Id));

        // (b) transcript evidence must actually reference a transcript payload: a missing
        //     transcript id, or a SourceId that does not match it, is rejected.
        Assert.Throws<DomainException>(() =>
            new ProvenanceEvidenceLink(ProvenanceEvidenceLink.TranscriptSourceType,
                transcriptId.ToString("D"), field.Id));                                    // no transcript id
        Assert.Throws<DomainException>(() =>
            new ProvenanceEvidenceLink(ProvenanceEvidenceLink.TranscriptSourceType,
                Guid.NewGuid().ToString("D"), field.Id, transcriptId: transcriptId));      // id mismatch

        // (c) an inverted span (end before start) is rejected.
        Assert.Throws<DomainException>(() =>
            new ProvenanceEvidenceLink("capture", "cap-1", field.Id, spanStart: 15, spanEnd: 10));

        // (d) evidence can only attach to a field of THIS output — a link built for a foreign
        //     field id is rejected, so evidence cannot be laundered across outputs.
        var foreignLink = new ProvenanceEvidenceLink("capture", "cap-2", Guid.NewGuid());
        Assert.Throws<DomainException>(() => field.AddEvidenceLink(foreignLink));

        // (e) a field can only attach to THIS provenance — a field built for a foreign provenance
        //     id is rejected by AddField.
        var foreignField = new ProvenanceField("Title", ProvenanceKind.Inferred, 0.5, Guid.NewGuid());
        Assert.Throws<DomainException>(() => provenance.AddField(foreignField));
    }
}
