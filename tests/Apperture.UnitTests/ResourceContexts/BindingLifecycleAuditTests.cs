using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Audit;
using Apperture.Core.Application.Connector;
using Apperture.Core.Application.Integration;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Connector;
using Apperture.Core.Domain.ResourceContexts;
using Apperture.Shared.Kernel;
using Apperture.UnitTests.Audit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Apperture.UnitTests.ResourceContexts;

/// <summary>
/// AUD-1.4 slice 4: which audit rows the binding create (integration-owned and contextless capability) and delete record, and
/// what they refuse to change without a unit (M7 at the service; the transactional guarantees are proven against SQL Server in
/// <c>BindingLifecycleAuditSqlServerTests</c>).
/// </summary>
public sealed class BindingLifecycleAuditTests
{
    private const string ModuleId = "Sample.Mod";
    private const string SlotName = "Db";
    private const string ContractType = "System.Data.IDbConnection";

    [Fact]
    public async Task A_creation_records_one_binding_created_row_naming_the_slot_the_integration_and_the_connection()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var app = await h.ResourceContexts.CreateApplicationContextAsync("Application A");
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId, ResourceContextId: app.Id));
        var connection = await CreateConnectionAsync(h, app.Id);
        h.AuditUnit.Operations.Clear();

        var binding = await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.BindingCreated);
        var evt = h.AuditUnit.Single(AuditActionNames.BindingCreated);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Binding, binding.Id.Value));
        evt.Related.ModuleId.Should().Be(ModuleId);
        evt.Related.ResourceContextId.Should().Be(app.Id.Value, "the integration's owning context");
        evt.Fields.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["moduleId"] = ModuleId,
            ["slotName"] = SlotName,
            ["integrationId"] = integration.Id.Value,
            ["connectionId"] = connection.Id.Value,
        });
        (await h.Db.Bindings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_refused_creation_records_nothing_and_stores_nothing()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));
        var connection = await CreateConnectionAsync(h, null);
        await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);
        h.AuditUnit.Operations.Clear();

        var alreadyBound = () => h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id).AsTask();
        var unknownConnection = () => h.Bindings.CreateBindingAsync(ModuleId, "Other", ContractType, new ConnectionId("conn-missing"), integration.Id).AsTask();
        var wrongModule = () => h.Bindings.CreateBindingAsync("Other.Mod", SlotName, ContractType, connection.Id, integration.Id).AsTask();

        await alreadyBound.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already bound*");
        await unknownConnection.Should().ThrowAsync<InvalidOperationException>();
        await wrongModule.Should().ThrowAsync<InvalidOperationException>();
        h.AuditUnit.Actions.Should().BeEmpty();
        (await h.Db.Bindings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_deletion_records_one_binding_deleted_row_and_removes_the_binding()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));
        var connection = await CreateConnectionAsync(h, null);
        var binding = await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);
        h.AuditUnit.Operations.Clear();

        await h.Bindings.DeleteBindingAsync(binding.Id);

        (await h.Db.Bindings.CountAsync()).Should().Be(0);
        h.AuditUnit.Actions.Should().Equal(AuditActionNames.BindingDeleted);
        var evt = h.AuditUnit.Single(AuditActionNames.BindingDeleted);
        evt.Target.Should().Be(new AuditTarget(AuditTargetTypes.Binding, binding.Id.Value));
        evt.Fields["integrationId"].Should().Be(integration.Id.Value);
        evt.Fields["connectionId"].Should().Be(connection.Id.Value);
        evt.Related.ResourceContextId.Should().Be(ResourceContextIds.SharedValue);
    }

    [Fact]
    public async Task Deleting_a_missing_binding_is_the_same_quiet_no_op_as_before_and_records_nothing()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        await h.Bindings.DeleteBindingAsync(new BindingId("bnd-missing"));

        h.AuditUnit.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_contextless_capability_binding_is_audited_on_creation_and_on_deletion()
    {
        await using var h = new ResourceContextOwnershipTestHarness();

        var binding = await h.Bindings.CreateCapabilityBindingAsync(ModuleId, SlotName, ContractType, "capability-1");
        await h.Bindings.DeleteBindingAsync(binding.Id);

        h.AuditUnit.Actions.Should().Equal(AuditActionNames.BindingCreated, AuditActionNames.BindingDeleted);
        var created = h.AuditUnit.Staged.First();
        created.Fields.Should().ContainKey("capabilityId").And.NotContainKey("integrationId").And.NotContainKey("connectionId");
        created.Related.ResourceContextId.Should().BeNull();
        (await h.Db.Bindings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_lost_acknowledgement_returns_the_committed_binding_and_runs_the_attempt_once()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));
        var connection = await CreateConnectionAsync(h, null);
        h.AuditUnit.Operations.Clear();
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Lost;

        var binding = await h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id);

        binding.IntegrationId.Should().Be(integration.Id);
        h.AuditUnit.Operations.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_outcome_is_reported_as_unknown()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var integration = await h.Integrations.CreateIntegrationAsync(new CreateIntegrationRequest("Payments", ModuleId));
        var connection = await CreateConnectionAsync(h, null);
        h.AuditUnit.Acknowledgement = RecordingAuditedUnitOfWork.CommitAcknowledgement.Unknown;

        var act = () => h.Bindings.CreateBindingAsync(ModuleId, SlotName, ContractType, connection.Id, integration.Id).AsTask();

        await act.Should().ThrowAsync<AuditOutcomeUnknownException>();
    }

    [Fact]
    public async Task Every_mutation_refuses_to_run_without_the_audited_unit()
    {
        await using var h = new ResourceContextOwnershipTestHarness();
        var existing = await h.Bindings.CreateCapabilityBindingAsync(ModuleId, SlotName, ContractType, "capability-1");
        var principal = new Mock<IPrincipalContext>();
        principal.Setup(p => p.GetCurrentActorAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IActor>());
        var service = new BindingService(
            principal.Object,
            Mock.Of<IAuthorizationService>(),
            h.BindingRepository,
            h.ConnectionRepository,
            h.Eligibility,
            h.IntegrationRepository,
            h.Db,
            Mock.Of<Apperture.Core.Application.ResourceContexts.IResourceOwnershipMutationGate>());

        var create = () => service.CreateBindingAsync(ModuleId, SlotName, ContractType, new ConnectionId("c"), new Apperture.Core.Domain.Integration.IntegrationId("i")).AsTask();
        var capability = () => service.CreateCapabilityBindingAsync(ModuleId, "Other", ContractType, "capability-2").AsTask();
        var delete = () => service.DeleteBindingAsync(existing.Id).AsTask();

        await create.Should().ThrowAsync<AuditInvariantException>();
        await capability.Should().ThrowAsync<AuditInvariantException>();
        await delete.Should().ThrowAsync<AuditInvariantException>();
        (await h.Db.Bindings.CountAsync()).Should().Be(1, "nothing was changed unaudited");
    }

    private static async Task<Connection> CreateConnectionAsync(ResourceContextOwnershipTestHarness harness, ResourceContextId? resourceContextId) =>
        await harness.Connections.CreateConnectionAsync(new CreateConnectionRequest(
            new ConnectorDefinitionId("test.connector"),
            "Sample Connection",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            ResourceContextId: resourceContextId));
}
