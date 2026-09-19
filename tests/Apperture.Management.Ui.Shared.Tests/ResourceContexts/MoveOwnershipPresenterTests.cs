using Apperture.Core.Domain.ResourceContexts;
using Apperture.Management.Client;
using Apperture.Management.Ui.Shared;
using FluentAssertions;
using Xunit;

namespace Apperture.Management.Ui.Shared.Tests.ResourceContexts;

/// <summary>
/// HSK-B Task 1: <see cref="MoveOwnershipPresenter"/> maps blocker/effect codes plus their structured
/// fields to operator copy. Known-code output must use Context display names and never a raw context id
/// or raw resource id in <c>Text</c>/<c>LinkText</c> (ids belong only in <c>Href</c>).
/// </summary>
public sealed class MoveOwnershipPresenterTests
{
    private const string RawCtx = "ctx-raw-9f3a";
    private const string RawTargetCtx = "ctx-raw-target-77c1";
    private const string RawResource = "res-raw-4d2e";

    private static readonly IReadOnlyDictionary<string, string> ContextNames = new Dictionary<string, string>
    {
        [RawCtx] = "CampusNexus",
        [RawTargetCtx] = "Canvas",
        ["shared"] = "Shared",
    };

    // A fake of ResourceContextViewState.DescribeContext: names for known ids, "Unknown context" otherwise.
    private static string Describe(string id) =>
        ContextNames.TryGetValue(id, out var name) ? name : "Unknown context";

    private static ResourceContextRef Ref(string id, string name) => new(id, name, name.ToLowerInvariant());

    private static ResourceMovePreviewDto Preview(
        string resourceType = "Integration",
        bool canMove = true,
        IReadOnlyList<ResourceMoveBlockerDto>? blockers = null,
        IReadOnlyList<ResourceMoveEffectDto>? effects = null,
        ResourceContextRef? target = null) =>
        new(resourceType, "res-1", Ref("ctx-src", "Source"), target ?? Ref(RawTargetCtx, "Canvas"),
            canMove, blockers!, effects!);

    private static MovePresentation Present(
        ResourceMovePreviewDto preview,
        string resourceName = "Payroll Sync",
        IReadOnlyDictionary<string, string>? relatedNames = null,
        string targetName = "Canvas") =>
        MoveOwnershipPresenter.Present(preview, "CampusNexus", targetName, resourceName, Describe, relatedNames);

    private static ResourceMoveBlockerDto Blocker(
        string code,
        string message = "server message",
        string? relatedType = null,
        string? relatedId = null,
        string? relatedName = null,
        string? relatedCtx = null) =>
        new(code, message, relatedType, relatedId, relatedName, relatedCtx);

    private static ResourceMoveEffectDto Effect(
        string code, string message = "server message", string? relatedType = null, params string[] ids) =>
        new(code, message, relatedType, ids);

    private static IEnumerable<string> AllText(MovePresentation p) =>
        p.WillMove.Concat(p.UsedBy).Concat(p.BlockedBy).Concat(p.Notes)
            .SelectMany(i => new[] { i.Text, i.LinkText ?? "" })
            .Append(p.History);

    // ---- History / noun / null tolerance -----------------------------------------------------------

    [Fact]
    public void History_line_names_the_source_context()
    {
        var p = Present(Preview());

        p.History.Should().Be("Historical activity remains attributed to CampusNexus.");
    }

    [Fact]
    public void Null_Source_and_Target_refs_are_tolerated()
    {
        // MoveOwnershipDialogModelTests builds previews with default! Source/Target.
        var preview = new ResourceMovePreviewDto("Integration", "int-1", default!, default!, true, [], []);

        var act = () => Present(preview);

        act.Should().NotThrow();
    }

    [Fact]
    public void Null_Blockers_and_Effects_lists_are_tolerated()
    {
        var preview = new ResourceMovePreviewDto("Integration", "int-1", default!, default!, false, null!, null!);

        var p = Present(preview);

        p.WillMove.Should().BeEmpty();
        p.UsedBy.Should().BeEmpty();
        p.BlockedBy.Should().BeEmpty();
        p.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Null_Target_is_tolerated_by_a_blocker_that_uses_the_target_name()
    {
        var preview = new ResourceMovePreviewDto(
            "Connection", "c-1", default!, default!, false,
            [Blocker(ResourceMoveBlockerCodes.AlreadyInTarget)], []);

        var p = Present(preview, targetName: "Canvas");

        p.BlockedBy.Should().ContainSingle().Which.Text.Should().Be("This connection is already in Canvas.");
    }

    // ---- WillMove ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Integration", "Integration 'Payroll Sync'")]
    [InlineData("Connection", "Connection 'Payroll Sync'")]
    [InlineData("Schedule", "Schedule 'Payroll Sync'")]
    [InlineData("integration", "Integration 'Payroll Sync'")]
    [InlineData("SCHEDULE", "Schedule 'Payroll Sync'")]
    [InlineData("Widget", "Resource 'Payroll Sync'")]
    public void WillMove_leads_with_the_named_resource_when_allowed(string resourceType, string expected)
    {
        var p = Present(Preview(resourceType));

        p.WillMove.Should().HaveCount(1);
        p.WillMove[0].Text.Should().Be(expected);
        p.WillMove[0].LinkText.Should().BeNull();
        p.WillMove[0].Href.Should().BeNull();
    }

    [Theory]
    [InlineData("Integration", "This integration")]
    [InlineData("Connection", "This connection")]
    [InlineData("Schedule", "This schedule")]
    [InlineData("Widget", "This resource")]
    public void WillMove_falls_back_to_a_neutral_noun_when_the_resource_name_is_unknown(string resourceType, string expected)
    {
        var p = MoveOwnershipPresenter.Present(Preview(resourceType), "CampusNexus", "Canvas", null, Describe);

        p.WillMove.Should().ContainSingle().Which.Text.Should().Be(expected);
    }

    [Fact]
    public void WillMove_treats_a_blank_resource_name_as_unknown()
    {
        var p = Present(Preview(), resourceName: "   ");

        p.WillMove.Should().ContainSingle().Which.Text.Should().Be("This integration");
    }

    [Fact]
    public void WillMove_is_empty_when_the_move_is_blocked()
    {
        var p = Present(Preview(
            canMove: false,
            blockers: [Blocker(ResourceMoveBlockerCodes.AlreadyInTarget)],
            effects: [Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, relatedType: "Schedule", ids: "s-1")]));

        p.WillMove.Should().BeEmpty();
    }

    [Fact]
    public void WillMove_lists_resolved_attached_schedules_as_links()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "2 schedule(s) move with this integration.",
                "Schedule", "sch-1", "sch-2")
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-1")] = "Nightly export",
            [MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-2")] = "Hourly poll",
        };

        var p = Present(preview, relatedNames: names);

        p.WillMove.Should().HaveCount(3);
        p.WillMove[1].Should().Be(new MoveItem("Schedule", "Nightly export", "/schedules/sch-1"));
        p.WillMove[2].Should().Be(new MoveItem("Schedule", "Hourly poll", "/schedules/sch-2"));
    }

    [Fact]
    public void WillMove_summarises_unresolved_attached_schedules_as_a_count()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "3 schedule(s) move with this integration.",
                "Schedule", "sch-1", "sch-2", "sch-3")
        ]);

        var p = Present(preview);

        p.WillMove.Should().HaveCount(2);
        p.WillMove[1].Should().Be(new MoveItem("3 attached schedules"));
    }

    [Fact]
    public void WillMove_uses_the_singular_for_one_unresolved_attached_schedule()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "1 schedule(s) move with this integration.",
                "Schedule", "sch-1")
        ]);

        var p = Present(preview);

        p.WillMove[1].Text.Should().Be("1 attached schedule");
    }

    [Fact]
    public void WillMove_lists_resolved_schedules_and_counts_the_unresolved_remainder()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "3 schedule(s) move with this integration.",
                "Schedule", "sch-1", "sch-2", "sch-3")
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-2")] = "Hourly poll",
        };

        var p = Present(preview, relatedNames: names);

        p.WillMove.Should().HaveCount(3);
        p.WillMove[1].Should().Be(new MoveItem("Schedule", "Hourly poll", "/schedules/sch-2"));
        p.WillMove[2].Should().Be(new MoveItem("2 more attached schedules"));
    }

    [Fact]
    public void WillMove_caps_the_listed_schedules_and_summarises_the_overflow()
    {
        var ids = Enumerable.Range(1, MoveOwnershipPresenter.MaxResolvedNames + 3).Select(i => $"sch-{i}").ToArray();
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "13 schedule(s) move with this integration.",
                "Schedule", ids)
        ]);
        var names = ids.ToDictionary(id => MoveOwnershipPresenter.RelatedNameKey("Schedule", id), id => $"Name {id}");

        var p = Present(preview, relatedNames: names);

        // resource item + MaxResolvedNames links + one overflow line.
        p.WillMove.Should().HaveCount(1 + MoveOwnershipPresenter.MaxResolvedNames + 1);
        p.WillMove.Skip(1).Take(MoveOwnershipPresenter.MaxResolvedNames).Should().OnlyContain(i => i.Href != null);
        p.WillMove[^1].Should().Be(new MoveItem("3 more attached schedules"));
    }

    [Fact]
    public void WillMove_ignores_the_schedules_effect_when_it_carries_no_ids()
    {
        var preview = Preview(effects:
        [
            new ResourceMoveEffectDto(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", null)
        ]);

        var p = Present(preview);

        p.WillMove.Should().ContainSingle();
    }

    [Fact]
    public void Blank_resolved_names_count_as_unresolved()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", "sch-1")
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-1")] = "  ",
        };

        var p = Present(preview, relatedNames: names);

        p.WillMove[1].Should().Be(new MoveItem("1 attached schedule"));
    }

    // ---- UsedBy ------------------------------------------------------------------------------------

    [Fact]
    public void UsedBy_lists_resolved_integrations_as_links()
    {
        var preview = Preview("Connection", effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "Used by 2 integration(s).", "Integration", "int-1", "int-2")
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Integration", "int-1")] = "Payroll Sync",
            [MoveOwnershipPresenter.RelatedNameKey("Integration", "int-2")] = "Grade Push",
        };

        var p = Present(preview, relatedNames: names);

        p.UsedBy.Should().Equal(
            new MoveItem("", "Payroll Sync", "/integrations/int-1"),
            new MoveItem("", "Grade Push", "/integrations/int-2"));
    }

    [Fact]
    public void UsedBy_summarises_unresolved_integrations_as_a_count()
    {
        var preview = Preview("Connection", effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "Used by 2 integration(s).", "Integration", "int-1", "int-2")
        ]);

        var p = Present(preview);

        p.UsedBy.Should().Equal(new MoveItem("Used by 2 integrations"));
    }

    [Fact]
    public void UsedBy_uses_the_singular_for_one_unresolved_integration()
    {
        var preview = Preview("Connection", effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "Used by 1 integration(s).", "Integration", "int-1")
        ]);

        Present(preview).UsedBy.Should().Equal(new MoveItem("Used by 1 integration"));
    }

    [Fact]
    public void UsedBy_counts_the_unresolved_remainder_after_the_resolved_links()
    {
        var preview = Preview("Connection", effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "Used by 3 integration(s).", "Integration", "int-1", "int-2", "int-3")
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Integration", "int-1")] = "Payroll Sync",
        };

        var p = Present(preview, relatedNames: names);

        p.UsedBy.Should().Equal(
            new MoveItem("", "Payroll Sync", "/integrations/int-1"),
            new MoveItem("and 2 more"));
    }

    [Fact]
    public void UsedBy_caps_the_listed_integrations_and_summarises_the_overflow()
    {
        var ids = Enumerable.Range(1, MoveOwnershipPresenter.MaxResolvedNames + 4).Select(i => $"int-{i}").ToArray();
        var preview = Preview("Connection", effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", ids)
        ]);
        var names = ids.ToDictionary(id => MoveOwnershipPresenter.RelatedNameKey("Integration", id), id => $"Name {id}");

        var p = Present(preview, relatedNames: names);

        p.UsedBy.Should().HaveCount(MoveOwnershipPresenter.MaxResolvedNames + 1);
        p.UsedBy[^1].Should().Be(new MoveItem("and 4 more"));
    }

    [Fact]
    public void UsedBy_is_shown_even_when_the_move_is_blocked()
    {
        var preview = Preview("Connection", canMove: false,
            blockers: [Blocker(ResourceMoveBlockerCodes.AlreadyInTarget)],
            effects: [Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", "int-1")]);

        var p = Present(preview);

        p.UsedBy.Should().ContainSingle();
    }

    // ---- Notes (informational + unknown effects) ---------------------------------------------------

    [Theory]
    [InlineData(ResourceMoveEffectCodes.BindingReferencesMissingConnection)]
    [InlineData(ResourceMoveEffectCodes.BindingReferencesMissingIntegration)]
    public void Binding_references_missing_effects_become_notes_using_the_server_message(string code)
    {
        var preview = Preview(effects:
        [
            Effect(code, "Binding for slot 'primary' references a missing connection; the move does not make this worse.",
                "Binding", "binding-9")
        ]);

        var p = Present(preview);

        p.Notes.Should().Equal(new MoveItem(
            "Binding for slot 'primary' references a missing connection; the move does not make this worse."));
        p.UsedBy.Should().BeEmpty();
        p.WillMove.Should().ContainSingle(); // only the resource item
    }

    [Fact]
    public void An_unknown_effect_code_falls_back_to_a_readable_note()
    {
        var preview = Preview(effects: [Effect("some_future_effect", "Something new will happen.")]);

        var p = Present(preview);

        p.Notes.Should().Equal(new MoveItem("Something new will happen."));
    }

    [Fact]
    public void An_unknown_effect_with_a_blank_message_is_dropped()
    {
        var preview = Preview(effects: [Effect("some_future_effect", "  ")]);

        Present(preview).Notes.Should().BeEmpty();
    }

    // ---- BlockedBy: one test per blocker code ------------------------------------------------------

    [Fact]
    public void Blocker_target_not_found_uses_fixed_copy_and_never_the_target_name()
    {
        // The server sets Target.Name to the raw id when the context is missing.
        var preview = Preview(
            canMove: false,
            blockers: [Blocker(ResourceMoveBlockerCodes.TargetNotFound,
                message: $"Target context '{RawTargetCtx}' was not found.")],
            target: Ref(RawTargetCtx, RawTargetCtx));

        var p = Present(preview, targetName: RawTargetCtx);

        p.BlockedBy.Should().Equal(new MoveItem("The selected target context no longer exists."));
    }

    [Fact]
    public void Blocker_target_archived_names_the_archived_context()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.TargetArchived, relatedType: "ResourceContext",
                relatedId: RawTargetCtx, relatedName: "Old Canvas", relatedCtx: RawTargetCtx)
        ]);

        var p = Present(preview);

        p.BlockedBy.Should().Equal(new MoveItem("Old Canvas is archived and can't receive new ownership."));
    }

    [Fact]
    public void Blocker_target_archived_falls_back_to_the_target_name_when_the_related_name_is_missing()
    {
        var preview = Preview(canMove: false, blockers: [Blocker(ResourceMoveBlockerCodes.TargetArchived)]);

        var p = Present(preview, targetName: "Canvas");

        p.BlockedBy.Should().Equal(new MoveItem("Canvas is archived and can't receive new ownership."));
    }

    [Theory]
    [InlineData("Integration", "This integration is already in Canvas.")]
    [InlineData("Connection", "This connection is already in Canvas.")]
    [InlineData("Schedule", "This schedule is already in Canvas.")]
    [InlineData("Widget", "This resource is already in Canvas.")]
    public void Blocker_already_in_target_names_the_resource_kind_and_target(string resourceType, string expected)
    {
        var preview = Preview(resourceType, canMove: false, blockers: [Blocker(ResourceMoveBlockerCodes.AlreadyInTarget)]);

        Present(preview).BlockedBy.Should().Equal(new MoveItem(expected));
    }

    [Fact]
    public void Blocker_binding_connection_unreachable_names_the_owner_context_and_links_to_the_connection()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                relatedType: "Connection", relatedId: "conn-1", relatedName: "Registrar DB", relatedCtx: RawCtx)
        ]);

        var p = Present(preview);

        var item = p.BlockedBy.Should().ContainSingle().Subject;
        item.Text.Should().StartWith(
            "Connection 'Registrar DB' is owned by CampusNexus, so it would become unreachable from Canvas.");
        item.LinkText.Should().Be("View connection");
        item.Href.Should().Be("/connections/conn-1");
    }

    [Fact]
    public void Blocker_binding_connection_unreachable_offers_the_verified_remedy()
    {
        // Rule (ResourceOwnershipMoveService integration validator + ResourceContextReferenceRule): the
        // blocker fires when the connection's owner is neither the target nor Shared. Moving the
        // connection into the target, or into Shared, each satisfy CanReference -- so both remedies are true.
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                relatedType: "Connection", relatedId: "conn-1", relatedName: "Registrar DB", relatedCtx: RawCtx)
        ]);

        var p = Present(preview, targetName: "Canvas");

        p.BlockedBy[0].Text.Should().EndWith(
            "Move the connection to Canvas or Shared first, or choose another destination.");
    }

    [Fact]
    public void Blocker_binding_connection_unreachable_does_not_repeat_Shared_when_the_target_is_Shared()
    {
        var preview = Preview(canMove: false,
            target: Ref("shared", "Shared"),
            blockers:
            [
                Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                    relatedType: "Connection", relatedId: "conn-1", relatedName: "Registrar DB", relatedCtx: RawCtx)
            ]);

        var p = Present(preview, targetName: "Shared");

        p.BlockedBy[0].Text.Should().EndWith("Move the connection to Shared first, or choose another destination.");
        p.BlockedBy[0].Text.Should().NotContain("Shared or Shared");
    }

    [Fact]
    public void Blocker_binding_connection_unreachable_uses_neutral_wording_when_the_name_and_owner_are_missing()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable, relatedType: "Connection", relatedId: "conn-1")
        ]);

        var p = Present(preview);

        // A null owner context means Shared per the brief.
        p.BlockedBy[0].Text.Should().StartWith("A connection is owned by Shared, so it would become unreachable from Canvas.");
        p.BlockedBy[0].Href.Should().Be("/connections/conn-1");
    }

    [Fact]
    public void Blocker_binding_connection_unreachable_has_no_link_without_a_resource_id()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable, relatedName: "Registrar DB", relatedCtx: RawCtx)
        ]);

        var item = Present(preview).BlockedBy.Should().ContainSingle().Subject;
        item.LinkText.Should().BeNull();
        item.Href.Should().BeNull();
    }

    [Fact]
    public void Blocker_schedule_context_inconsistent_uses_the_server_message_and_links_to_the_schedule()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.ScheduleContextInconsistent,
                message: "Schedule 'Nightly export' does not currently match this integration's context and must be corrected before it can move.",
                relatedType: "Schedule", relatedId: "sch-1", relatedName: "Nightly export", relatedCtx: null)
        ]);

        var p = Present(preview);

        p.BlockedBy.Should().Equal(new MoveItem(
            "Schedule 'Nightly export' does not currently match this integration's context and must be corrected before it can move.",
            "View schedule", "/schedules/sch-1"));
    }

    [Fact]
    public void Blocker_schedule_context_inconsistent_has_no_link_without_a_resource_id()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.ScheduleContextInconsistent, message: "Fix the schedule first.")
        ]);

        Present(preview).BlockedBy.Should().Equal(new MoveItem("Fix the schedule first."));
    }

    [Fact]
    public void Blocker_binding_integration_unreachable_names_the_integration_and_its_context()
    {
        var preview = Preview("Connection", canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingIntegrationUnreachable,
                relatedType: "Integration", relatedId: "int-1", relatedName: "Payroll Sync", relatedCtx: RawCtx)
        ]);

        var p = Present(preview);

        p.BlockedBy.Should().Equal(new MoveItem(
            "Integration 'Payroll Sync' in CampusNexus would no longer be able to use this connection.",
            "View integration", "/integrations/int-1"));
    }

    [Fact]
    public void Blocker_binding_integration_unreachable_uses_neutral_wording_when_name_and_context_are_missing()
    {
        var preview = Preview("Connection", canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingIntegrationUnreachable, relatedType: "Integration", relatedId: "int-1")
        ]);

        var item = Present(preview).BlockedBy.Should().ContainSingle().Subject;
        item.Text.Should().Be("An integration would no longer be able to use this connection.");
        item.Href.Should().Be("/integrations/int-1");
    }

    [Fact]
    public void Blocker_unowned_binding_requires_shared_uses_the_server_message_without_a_link()
    {
        var preview = Preview("Connection", canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.UnownedBindingRequiresShared,
                message: "A platform-level binding uses this connection; it must stay in Shared.",
                relatedType: "Binding", relatedId: "binding-1")
        ]);

        Present(preview).BlockedBy.Should().Equal(
            new MoveItem("A platform-level binding uses this connection; it must stay in Shared."));
    }

    [Fact]
    public void Blocker_schedule_attached_to_integration_points_at_the_integration()
    {
        var preview = Preview("Schedule", canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.ScheduleAttachedToIntegration,
                relatedType: "Integration", relatedId: "int-1", relatedName: "Payroll Sync", relatedCtx: RawCtx)
        ]);

        var p = Present(preview);

        p.BlockedBy.Should().Equal(new MoveItem(
            "This schedule belongs to integration 'Payroll Sync'. Move the integration instead — its schedules move with it.",
            "Go to integration", "/integrations/int-1"));
    }

    [Fact]
    public void Blocker_schedule_attached_to_integration_drops_the_name_clause_when_the_name_is_missing()
    {
        var preview = Preview("Schedule", canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.ScheduleAttachedToIntegration, relatedType: "Integration", relatedId: "int-1")
        ]);

        var item = Present(preview).BlockedBy.Should().ContainSingle().Subject;
        item.Text.Should().Be("This schedule belongs to an integration. Move the integration instead — its schedules move with it.");
        item.Href.Should().Be("/integrations/int-1");
    }

    [Fact]
    public void An_unknown_blocker_code_falls_back_to_a_readable_message()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker("some_future_blocker", message: "Something new is in the way.", relatedId: "x-1")
        ]);

        Present(preview).BlockedBy.Should().Equal(new MoveItem("Something new is in the way."));
    }

    [Fact]
    public void An_unknown_blocker_code_with_a_blank_message_still_says_the_move_is_blocked()
    {
        var preview = Preview(canMove: false, blockers: [Blocker("some_future_blocker", message: " ")]);

        Present(preview).BlockedBy.Should().ContainSingle().Which.Text.Should().Be("This move is blocked.");
    }

    [Fact]
    public void Multiple_blockers_are_all_listed_in_order()
    {
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.AlreadyInTarget),
            Blocker(ResourceMoveBlockerCodes.TargetNotFound),
        ]);

        Present(preview).BlockedBy.Select(i => i.Text).Should().Equal(
            "This integration is already in Canvas.",
            "The selected target context no longer exists.");
    }

    // ---- No raw ids in known-code copy -------------------------------------------------------------

    [Fact]
    public void Known_blocker_copy_never_contains_a_raw_context_or_resource_id_but_uses_the_described_names()
    {
        // Server messages for the three leaky codes deliberately embed raw ids; the presenter must not
        // reuse them. Ids appear only in Href.
        var leakyServerMessage = $"leak {RawCtx} {RawTargetCtx} {RawResource}";
        var preview = Preview(canMove: false, target: Ref(RawTargetCtx, RawTargetCtx), blockers:
        [
            Blocker(ResourceMoveBlockerCodes.TargetNotFound, leakyServerMessage),
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable, leakyServerMessage,
                "Connection", RawResource, "Registrar DB", RawCtx),
            Blocker(ResourceMoveBlockerCodes.BindingIntegrationUnreachable, leakyServerMessage,
                "Integration", RawResource, "Payroll Sync", RawCtx),
            Blocker(ResourceMoveBlockerCodes.TargetArchived, leakyServerMessage,
                "ResourceContext", RawTargetCtx, "Old Canvas", RawTargetCtx),
            Blocker(ResourceMoveBlockerCodes.ScheduleAttachedToIntegration, leakyServerMessage,
                "Integration", RawResource, "Payroll Sync", RawCtx),
        ]);

        // Caller supplies a display name for the target (never the raw-id Target.Name).
        var p = Present(preview, targetName: "Canvas");

        foreach (var text in AllText(p))
        {
            text.Should().NotContain(RawCtx);
            text.Should().NotContain(RawTargetCtx);
            text.Should().NotContain(RawResource);
        }

        p.BlockedBy[1].Text.Should().Contain("CampusNexus");
        p.BlockedBy[2].Text.Should().Contain("CampusNexus");
        p.BlockedBy[1].Href.Should().Contain(RawResource, "ids belong in the href");
    }

    [Fact]
    public void An_unresolvable_owner_context_renders_the_describeContext_placeholder_never_the_id()
    {
        const string unknownCtx = "ctx-raw-unknown-1";
        var preview = Preview(canMove: false, blockers:
        [
            Blocker(ResourceMoveBlockerCodes.BindingConnectionUnreachable,
                relatedType: "Connection", relatedId: RawResource, relatedName: "Registrar DB", relatedCtx: unknownCtx)
        ]);

        var p = Present(preview);

        p.BlockedBy[0].Text.Should().Contain("Unknown context").And.NotContain(unknownCtx);
    }

    [Fact]
    public void Resolved_names_and_counts_never_leak_resource_ids_into_text()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", "sch-raw-1", "sch-raw-2"),
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", "int-raw-1", "int-raw-2"),
        ]);
        var names = new Dictionary<string, string>
        {
            [MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-raw-1")] = "Nightly export",
            [MoveOwnershipPresenter.RelatedNameKey("Integration", "int-raw-1")] = "Payroll Sync",
        };

        var p = Present(preview, relatedNames: names);

        AllText(p).Should().OnlyContain(t => !t.Contains("-raw-"));
        p.WillMove.Should().Contain(i => i.Href == "/schedules/sch-raw-1");
        p.UsedBy.Should().Contain(i => i.Href == "/integrations/int-raw-1");
    }

    // ---- GetRelatedNamesToResolve / RelatedNameKey -------------------------------------------------

    [Fact]
    public void GetRelatedNamesToResolve_is_empty_when_there_are_no_relevant_effects()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.BindingReferencesMissingConnection, "msg", "Binding", "b-1")
        ]);

        MoveOwnershipPresenter.GetRelatedNamesToResolve(preview).Should().BeEmpty();
    }

    [Fact]
    public void GetRelatedNamesToResolve_tolerates_null_effects()
    {
        var preview = new ResourceMovePreviewDto("Integration", "int-1", default!, default!, true, [], null!);

        MoveOwnershipPresenter.GetRelatedNamesToResolve(preview).Should().BeEmpty();
    }

    [Fact]
    public void GetRelatedNamesToResolve_returns_integrations_and_schedules_deduplicated()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", "int-1", "int-2", "int-1"),
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", "sch-1", "sch-1", "sch-2"),
        ]);

        var refs = MoveOwnershipPresenter.GetRelatedNamesToResolve(preview);

        refs.Should().BeEquivalentTo(new[]
        {
            new MoveRelatedRef("Integration", "int-1"),
            new MoveRelatedRef("Integration", "int-2"),
            new MoveRelatedRef("Schedule", "sch-1"),
            new MoveRelatedRef("Schedule", "sch-2"),
        });
    }

    [Fact]
    public void GetRelatedNamesToResolve_caps_each_type_separately()
    {
        var cap = MoveOwnershipPresenter.MaxResolvedNames;
        var integrationIds = Enumerable.Range(1, cap + 5).Select(i => $"int-{i}").ToArray();
        var scheduleIds = Enumerable.Range(1, cap + 2).Select(i => $"sch-{i}").ToArray();
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", integrationIds),
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", scheduleIds),
        ]);

        var refs = MoveOwnershipPresenter.GetRelatedNamesToResolve(preview);

        refs.Count(r => r.ResourceType == "Integration").Should().Be(cap);
        refs.Count(r => r.ResourceType == "Schedule").Should().Be(cap);
        refs.Where(r => r.ResourceType == "Integration").Select(r => r.Id).Should().Equal(integrationIds.Take(cap));
    }

    [Fact]
    public void GetRelatedNamesToResolve_skips_blank_ids()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.UsedByIntegration, "msg", "Integration", "int-1", "", " ")
        ]);

        MoveOwnershipPresenter.GetRelatedNamesToResolve(preview).Should().Equal(new MoveRelatedRef("Integration", "int-1"));
    }

    [Fact]
    public void RelatedNameKey_is_stable_and_type_qualified()
    {
        MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-1").Should().Be("Schedule:sch-1");
        MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-1")
            .Should().Be(MoveOwnershipPresenter.RelatedNameKey("Schedule", "sch-1"));
        MoveOwnershipPresenter.RelatedNameKey("Schedule", "x")
            .Should().NotBe(MoveOwnershipPresenter.RelatedNameKey("Integration", "x"));
    }

    [Fact]
    public void Refs_from_GetRelatedNamesToResolve_round_trip_through_RelatedNameKey_into_Present()
    {
        var preview = Preview(effects:
        [
            Effect(ResourceMoveEffectCodes.SchedulesMoveWithIntegration, "msg", "Schedule", "sch-1")
        ]);
        var names = MoveOwnershipPresenter.GetRelatedNamesToResolve(preview)
            .ToDictionary(r => MoveOwnershipPresenter.RelatedNameKey(r.ResourceType, r.Id), _ => "Nightly export");

        var p = Present(preview, relatedNames: names);

        p.WillMove[1].Should().Be(new MoveItem("Schedule", "Nightly export", "/schedules/sch-1"));
    }

    [Fact]
    public void MaxResolvedNames_is_ten()
    {
        MoveOwnershipPresenter.MaxResolvedNames.Should().Be(10);
    }
}
