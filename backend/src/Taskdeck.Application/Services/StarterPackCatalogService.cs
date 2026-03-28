using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public sealed class StarterPackCatalogService : IStarterPackCatalogService
{
    private readonly IStarterPackManifestValidator _manifestValidator;

    public StarterPackCatalogService(IStarterPackManifestValidator manifestValidator)
    {
        _manifestValidator = manifestValidator;
    }

    public IReadOnlyList<StarterPackCatalogEntryDto> GetCatalog()
    {
        List<StarterPackCatalogEntryDto> catalog =
        [
            BuildCommonLabelsPack(),
            BuildCommonColumnFlowPack(),
            BuildEngineeringSprintBlueprint(),
            BuildSupportTriageBlueprint(),
            BuildClientOnboardingBlueprint(),
            BuildContentCalendarBlueprint(),
            BuildReleaseChecklistBlueprint(),
            BuildBugTrackerBlueprint(),
            BuildPersonalKanbanBlueprint(),
            BuildOnboardingPlanBlueprint(),
            BuildResearchProjectBlueprint()
        ];

        ValidateCatalog(catalog);
        return catalog;
    }

    private void ValidateCatalog(IReadOnlyList<StarterPackCatalogEntryDto> catalog)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog)
        {
            if (!ids.Add(entry.Id))
            {
                errors.Add($"Duplicate first-party pack id '{entry.Id}'.");
            }

            if (!string.Equals(entry.Id, entry.Manifest.PackId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Catalog id '{entry.Id}' does not match manifest packId '{entry.Manifest.PackId}'.");
            }

            if (!string.Equals(entry.Manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            {
                errors.Add($"Pack '{entry.Id}' must use schemaVersion 1.0.");
            }

            var validation = _manifestValidator.Validate(entry.Manifest);
            if (!validation.IsValid)
            {
                foreach (var validationError in validation.Errors)
                {
                    errors.Add($"Pack '{entry.Id}' invalid at {validationError.Path}: {validationError.Message}");
                }
            }
        }

        var hasLabelPack = catalog.Any(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.LabelPack, StringComparison.Ordinal));
        if (!hasLabelPack)
        {
            errors.Add("First-party catalog must include at least one label-pack entry.");
        }

        var hasColumnFlow = catalog.Any(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.ColumnFlow, StringComparison.Ordinal));
        if (!hasColumnFlow)
        {
            errors.Add("First-party catalog must include at least one column-flow entry.");
        }

        var boardBlueprintCount = catalog.Count(entry =>
            string.Equals(entry.Category, StarterPackCatalogCategories.BoardBlueprint, StringComparison.Ordinal));
        if (boardBlueprintCount == 0)
        {
            errors.Add("First-party catalog must include at least one board-blueprint entry.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static StarterPackCatalogEntryDto BuildCommonLabelsPack()
    {
        return new StarterPackCatalogEntryDto(
            Id: "common-labels-core",
            Category: StarterPackCatalogCategories.LabelPack,
            Title: "Common Labels Core",
            Summary: "Reusable label taxonomy for priority, risk, and review state.",
            Highlights:
            [
                "Priority and blocked labels",
                "Review-state labels",
                "Compatible with existing boards"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "common-labels-core",
                DisplayName = "Common Labels Core",
                Description = "Reusable label taxonomy for common delivery workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "labels", "core"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D", Description = "High urgency" },
                    new StarterPackLabelDto { Name = "priority-medium", Color = "#F59E0B", Description = "Medium urgency" },
                    new StarterPackLabelDto { Name = "blocked", Color = "#6B7280", Description = "Blocked by dependency" },
                    new StarterPackLabelDto { Name = "needs-review", Color = "#2563EB", Description = "Awaiting review" }
                ],
                Columns = [],
                Templates = [],
                SeedCards = []
            });
    }

    private static StarterPackCatalogEntryDto BuildCommonColumnFlowPack()
    {
        return new StarterPackCatalogEntryDto(
            Id: "common-column-flow-kanban",
            Category: StarterPackCatalogCategories.ColumnFlow,
            Title: "Common Column Flow - Kanban",
            Summary: "Standard delivery lane flow with explicit review and completion states.",
            Highlights:
            [
                "Backlog to Done lane progression",
                "WIP limits for active lanes",
                "No labels or seed cards required"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "common-column-flow-kanban",
                DisplayName = "Common Column Flow - Kanban",
                Description = "Reusable kanban column flow for most product delivery teams.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "columns"]
                },
                Tags = ["starter", "columns", "kanban"],
                Labels = [],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Backlog", Position = 0 },
                    new StarterPackColumnDto { Name = "Ready", Position = 1 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 2, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 3, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Done", Position = 4 }
                ],
                Templates = [],
                SeedCards = []
            });
    }

    private static StarterPackCatalogEntryDto BuildEngineeringSprintBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-engineering-sprint",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Engineering Sprint",
            Summary: "Sprint-ready engineering board with triage labels and bug template.",
            Highlights:
            [
                "Sprint lane defaults",
                "Engineering-focused labels",
                "Bug-report template and kickoff card"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-engineering-sprint",
                DisplayName = "Board Blueprint - Engineering Sprint",
                Description = "Starter blueprint for engineering sprint execution.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "engineering"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "priority-high", Color = "#E85D5D", Description = "High urgency" },
                    new StarterPackLabelDto { Name = "bug", Color = "#DC2626", Description = "Defect issue" },
                    new StarterPackLabelDto { Name = "tech-debt", Color = "#7C3AED", Description = "Technical debt task" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Backlog", Position = 0 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 1, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 2, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Done", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "bug-report",
                        Title = "Bug Report",
                        Description = "Template for reproducible defect triage.",
                        Checklist = ["Reproduction steps", "Expected behavior", "Actual behavior"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Plan sprint goals",
                        Description = "Define sprint scope and ownership.",
                        ColumnName = "Backlog",
                        Labels = ["priority-high"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildSupportTriageBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-support-triage",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Support Triage",
            Summary: "Support-centric queue with urgency and SLA-aware handling labels.",
            Highlights:
            [
                "Intake and triage lane model",
                "Customer support labels",
                "Response template for ticket handling"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-support-triage",
                DisplayName = "Board Blueprint - Support Triage",
                Description = "Starter blueprint for support intake and SLA triage.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "support"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "customer-impact", Color = "#2563EB", Description = "Customer-facing issue" },
                    new StarterPackLabelDto { Name = "sla-risk", Color = "#B91C1C", Description = "At risk of SLA breach" },
                    new StarterPackLabelDto { Name = "waiting-on-customer", Color = "#0D9488", Description = "Pending customer reply" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Inbox", Position = 0 },
                    new StarterPackColumnDto { Name = "Triage", Position = 1, WipLimit = 6 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 2, WipLimit = 5 },
                    new StarterPackColumnDto { Name = "Resolved", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "support-response",
                        Title = "Support Response",
                        Description = "Template for support issue handling.",
                        Checklist = ["Initial response", "Root cause", "Resolution summary"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Define triage rotation",
                        Description = "Assign weekly support triage ownership.",
                        ColumnName = "Inbox",
                        Labels = ["customer-impact"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildContentCalendarBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-content-calendar",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Content Calendar",
            Summary: "Editorial planning board with review workflow and publishing cadence.",
            Highlights:
            [
                "Editorial lane structure",
                "Content workflow labels",
                "Brief template and planning seed card"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-content-calendar",
                DisplayName = "Board Blueprint - Content Calendar",
                Description = "Starter blueprint for content planning and publishing workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "content"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "needs-draft", Color = "#7C3AED", Description = "Drafting required" },
                    new StarterPackLabelDto { Name = "needs-review", Color = "#D97706", Description = "Editorial review required" },
                    new StarterPackLabelDto { Name = "publish-week", Color = "#059669", Description = "Target publish week" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Ideas", Position = 0 },
                    new StarterPackColumnDto { Name = "Drafting", Position = 1, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Review", Position = 2, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Scheduled", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "content-brief",
                        Title = "Content Brief",
                        Description = "Template for article and campaign planning.",
                        Checklist = ["Audience", "Core message", "Call to action"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Plan weekly editorial slate",
                        Description = "Choose top topics and assign owners.",
                        ColumnName = "Ideas",
                        TemplateId = "content-brief",
                        Labels = ["publish-week"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildReleaseChecklistBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-release-checklist",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Release Checklist",
            Summary: "Ship-readiness tracker with staged gates from planning through production.",
            Highlights:
            [
                "Staged release lane progression",
                "Risk labels with rollback tracking",
                "Go/no-go checklist template and kickoff card"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-release-checklist",
                DisplayName = "Board Blueprint - Release Checklist",
                Description = "Starter blueprint for release planning and staged deployment gates.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "release"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "high-risk", Color = "#DC2626", Description = "High-risk change" },
                    new StarterPackLabelDto { Name = "low-risk", Color = "#059669", Description = "Low-risk change" },
                    new StarterPackLabelDto { Name = "rollback-plan", Color = "#7C3AED", Description = "Rollback plan required" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Planning", Position = 0 },
                    new StarterPackColumnDto { Name = "Development", Position = 1, WipLimit = 5 },
                    new StarterPackColumnDto { Name = "QA", Position = 2, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Staging", Position = 3, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Released", Position = 4 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "go-no-go-checklist",
                        Title = "Go/No-Go Checklist",
                        Description = "Template for release gate review.",
                        Checklist = ["Tests passing", "Rollback plan documented", "Stakeholder sign-off"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Define release scope and owners",
                        Description = "Confirm what ships, who owns each area, and the target date.",
                        ColumnName = "Planning",
                        TemplateId = "go-no-go-checklist",
                        Labels = ["high-risk"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildBugTrackerBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-bug-tracker",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Bug Tracker",
            Summary: "Defect lifecycle board from report through verification and closure.",
            Highlights:
            [
                "Full defect triage lane model",
                "Severity labels for triage prioritization",
                "Bug report template with reproduction checklist"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-bug-tracker",
                DisplayName = "Board Blueprint - Bug Tracker",
                Description = "Starter blueprint for structured defect tracking and resolution.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "bugs"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "severity-critical", Color = "#DC2626", Description = "System-down or data-loss severity" },
                    new StarterPackLabelDto { Name = "severity-major", Color = "#F59E0B", Description = "Major functionality impaired" },
                    new StarterPackLabelDto { Name = "severity-minor", Color = "#2563EB", Description = "Minor issue or cosmetic defect" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Reported", Position = 0 },
                    new StarterPackColumnDto { Name = "Confirmed", Position = 1, WipLimit = 6 },
                    new StarterPackColumnDto { Name = "Fixing", Position = 2, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Testing", Position = 3, WipLimit = 4 },
                    new StarterPackColumnDto { Name = "Closed", Position = 4 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "bug-report",
                        Title = "Bug Report",
                        Description = "Template for structured defect reporting.",
                        Checklist = ["Reproduction steps", "Expected behavior", "Actual behavior", "Environment details"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Triage incoming bug reports",
                        Description = "Review new reports, confirm reproduction, and assign severity.",
                        ColumnName = "Reported",
                        Labels = ["severity-major"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildPersonalKanbanBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-personal-kanban",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Personal Kanban",
            Summary: "Lightweight three-column board for individual task management.",
            Highlights:
            [
                "Minimal To Do / Doing / Done flow",
                "Focus and someday labels",
                "Low ceremony — fast to start"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-personal-kanban",
                DisplayName = "Board Blueprint - Personal Kanban",
                Description = "Starter blueprint for simple personal task tracking.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "personal"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "focus", Color = "#DC2626", Description = "Top priority today" },
                    new StarterPackLabelDto { Name = "someday", Color = "#6B7280", Description = "Nice to do eventually" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "To Do", Position = 0 },
                    new StarterPackColumnDto { Name = "Doing", Position = 1, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Done", Position = 2 }
                ],
                Templates = [],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Capture everything on your mind",
                        Description = "Brain-dump tasks here, then prioritize.",
                        ColumnName = "To Do",
                        Labels = ["focus"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildOnboardingPlanBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-onboarding-plan",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Onboarding Plan",
            Summary: "Week-by-week onboarding board for new team members or hires.",
            Highlights:
            [
                "Weekly progression columns",
                "Category labels for learning areas",
                "Onboarding task template with completion checklist"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-onboarding-plan",
                DisplayName = "Board Blueprint - Onboarding Plan",
                Description = "Starter blueprint for structured team-member onboarding.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "onboarding"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "tooling", Color = "#2563EB", Description = "Dev-environment and tool setup" },
                    new StarterPackLabelDto { Name = "culture", Color = "#7C3AED", Description = "Team norms and processes" },
                    new StarterPackLabelDto { Name = "domain", Color = "#059669", Description = "Product and domain knowledge" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Week 1", Position = 0 },
                    new StarterPackColumnDto { Name = "Week 2", Position = 1 },
                    new StarterPackColumnDto { Name = "Week 3", Position = 2 },
                    new StarterPackColumnDto { Name = "Ongoing", Position = 3 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "onboarding-task",
                        Title = "Onboarding Task",
                        Description = "Template for a trackable onboarding activity.",
                        Checklist = ["Owner assigned", "Completed", "Follow-up scheduled"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Set up development environment",
                        Description = "Install tools, clone repos, and verify local build.",
                        ColumnName = "Week 1",
                        TemplateId = "onboarding-task",
                        Labels = ["tooling"]
                    },
                    new StarterPackSeedCardDto
                    {
                        Title = "Meet the team and review norms",
                        Description = "Introductions, team charter walkthrough, and communication channels.",
                        ColumnName = "Week 1",
                        Labels = ["culture"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildResearchProjectBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-research-project",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Research Project",
            Summary: "Structured research board from exploration through analysis and documentation.",
            Highlights:
            [
                "Discovery-to-documentation lane flow",
                "Research-area labels",
                "Research brief template"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-research-project",
                DisplayName = "Board Blueprint - Research Project",
                Description = "Starter blueprint for structured research and investigation workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "research"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "user-research", Color = "#2563EB", Description = "User interviews and surveys" },
                    new StarterPackLabelDto { Name = "technical-spike", Color = "#7C3AED", Description = "Technical feasibility investigation" },
                    new StarterPackLabelDto { Name = "data-analysis", Color = "#059669", Description = "Quantitative data exploration" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "Explore", Position = 0 },
                    new StarterPackColumnDto { Name = "Hypothesize", Position = 1 },
                    new StarterPackColumnDto { Name = "Experiment", Position = 2, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Analyze", Position = 3, WipLimit = 3 },
                    new StarterPackColumnDto { Name = "Document", Position = 4 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "research-brief",
                        Title = "Research Brief",
                        Description = "Template for framing a research question.",
                        Checklist = ["Research question defined", "Method chosen", "Findings summarized"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Define research questions",
                        Description = "Identify the key unknowns and frame testable hypotheses.",
                        ColumnName = "Explore",
                        TemplateId = "research-brief",
                        Labels = ["user-research"]
                    }
                ]
            });
    }

    private static StarterPackCatalogEntryDto BuildClientOnboardingBlueprint()
    {
        return new StarterPackCatalogEntryDto(
            Id: "board-blueprint-client-onboarding",
            Category: StarterPackCatalogCategories.BoardBlueprint,
            Title: "Board Blueprint - Client Onboarding",
            Summary: "Client-onboarding workflow with clear intake, follow-up, and completion lanes.",
            Highlights:
            [
                "Business-facing onboarding lane model",
                "Client follow-up and internal review labels",
                "Two seeded kickoff cards for immediate demo readiness"
            ],
            Manifest: new StarterPackManifestDto
            {
                SchemaVersion = "1.0",
                PackId = "board-blueprint-client-onboarding",
                DisplayName = "Board Blueprint - Client Onboarding",
                Description = "Starter blueprint for accounting and business-operations client onboarding workflows.",
                Compatibility = new StarterPackCompatibilityDto
                {
                    MinTaskdeckVersion = "1.0.0",
                    RequiredFeatures = ["boards", "labels", "cards"]
                },
                Tags = ["starter", "blueprint", "operations", "onboarding"],
                Labels =
                [
                    new StarterPackLabelDto { Name = "client-action", Color = "#2563EB", Description = "Action required from the client" },
                    new StarterPackLabelDto { Name = "internal-review", Color = "#B45309", Description = "Needs internal team review" },
                    new StarterPackLabelDto { Name = "waiting-on-client", Color = "#0D9488", Description = "Blocked pending client response" }
                ],
                Columns =
                [
                    new StarterPackColumnDto { Name = "New Intake", Position = 0 },
                    new StarterPackColumnDto { Name = "Waiting on Client", Position = 1, WipLimit = 8 },
                    new StarterPackColumnDto { Name = "Ready for Review", Position = 2, WipLimit = 6 },
                    new StarterPackColumnDto { Name = "In Progress", Position = 3, WipLimit = 6 },
                    new StarterPackColumnDto { Name = "Completed", Position = 4 }
                ],
                Templates =
                [
                    new StarterPackCardTemplateDto
                    {
                        TemplateId = "client-onboarding-task",
                        Title = "Client Onboarding Task",
                        Description = "Template for a client onboarding action with explicit evidence requirements.",
                        Checklist = ["Owner assigned", "Due date confirmed", "Evidence linked"]
                    }
                ],
                SeedCards =
                [
                    new StarterPackSeedCardDto
                    {
                        Title = "Review new onboarding intake",
                        Description = "Confirm scope, timeline, and ownership before requesting documents.",
                        ColumnName = "New Intake",
                        TemplateId = "client-onboarding-task",
                        Labels = ["internal-review"]
                    },
                    new StarterPackSeedCardDto
                    {
                        Title = "Confirm onboarding owner and due date",
                        Description = "Assign accountable owner and target kickoff date for this client.",
                        ColumnName = "Ready for Review",
                        Labels = ["internal-review"]
                    }
                ]
            });
    }
}
