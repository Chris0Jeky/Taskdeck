using System.Text.RegularExpressions;
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
        // Automation surfaces that must NOT call board mutation methods directly
        var automationDirs = new[]
        {
            "src/Taskdeck.Application/Services/Tools",     // write tool executors
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
        // - the AutomationExecutorService itself
        var allowedFilePatterns = new[]
        {
            "OperationHandler",       // Pipeline handlers execute approved operations
            "ExecutionAuditRecorder",  // Records audit for executed operations
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

        var toolNamePattern = new Regex(@"(?:Name[:\s]*""([^""]+)""|""approve[^""]*"")", RegexOptions.Compiled);
        var allToolNames = new List<string>();

        foreach (var file in orchestratorFiles.Distinct())
        {
            var content = ReadFile(file);
            var names = toolNamePattern.Matches(content)
                .Select(m => m.Groups[1].Value)
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
            @"(?:\[McpServerTool\s*\(\s*Name\s*=\s*""([^""]+)""\s*\)|""approve[^""]*"")",
            RegexOptions.Compiled);

        var allToolNames = new List<string>();

        foreach (var dir in integrationDirs)
        {
            foreach (var file in GetSourceFiles(dir))
            {
                var content = ReadFile(file);
                var names = toolNamePattern.Matches(content)
                    .Select(m => m.Groups[1].Value)
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
            @"new\s+HttpClient\s*\(|" +
            @"IHttpClientFactory|" +
            @"\.AddHttpClient\s*[<(]|" +
            @"HttpClient\s+\w+\s*=",
            RegexOptions.Compiled);

        // Known/expected outbound HTTP usage sites (file names without extension)
        // Update this list when adding new legitimate outbound HTTP callsites.
        var expectedSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "OpenAiLlmProvider",
            "GeminiLlmProvider",
            "OutboundWebhookDeliveryWorker",
            "OutboundWebhookDeliveryService",
            "ApplicationServiceRegistration",
            "WorkerRegistration",
            "InfrastructureServiceRegistration",
            "DependencyInjection",
            "LlmProviderRegistration",
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
    /// INV-10: MCP tool hash-pinning.
    /// Each MCP tool definition should include a content hash so that tool
    /// schema changes are detectable and auditable.
    /// </summary>
    [Fact(Skip = "TODO: requires MCP tool hash-pinning implementation — each tool definition must include a content hash for change detection")]
    public void Invariant10_McpToolHashPinning()
    {
        // When implemented, this test should:
        // 1. Load all MCP tool definitions
        // 2. Assert each has a non-empty SchemaHash property
        // 3. Verify hash matches the computed hash of the schema
        // 4. Optionally compare against a checked-in hash manifest
    }

    // ─── Invariant 11: Local analytics no user content ─────────────────

    /// <summary>
    /// INV-11: Local analytics contains no user content.
    /// TelemetryGuard ensures analytics events never include PII, card content,
    /// board names, or other user-generated data.
    /// </summary>
    [Fact(Skip = "TODO: requires TelemetryGuard implementation — analytics events must contain no PII or user content (bucketed counts only)")]
    public void Invariant11_LocalAnalytics_NoUserContent()
    {
        // When implemented, this test should:
        // 1. Instantiate TelemetryGuard
        // 2. Attempt to emit events with PII-like content
        // 3. Assert they are rejected or scrubbed
        // 4. Verify allowed events pass through with bucketed/aggregated data only
    }

    // ─── Invariant 12: Source spans reference source payload ────────────

    /// <summary>
    /// INV-12: Source spans reference source payload.
    /// Every automation output (proposal, chat message, tool result) must carry
    /// a provenance span linking back to the originating source payload.
    /// </summary>
    [Fact(Skip = "TODO: requires provenance model implementation — automation outputs must carry source spans referencing originating payloads")]
    public void Invariant12_SourceSpans_ReferenceSourcePayload()
    {
        // When implemented, this test should:
        // 1. Create a proposal via the automation pipeline
        // 2. Assert the proposal carries a SourceSpan with:
        //    - SourceType (capture, chat, mcp)
        //    - SourceId (reference to originating payload)
        //    - OffsetStart / OffsetEnd (character positions in source)
        // 3. Verify the span resolves to valid content
    }
}
