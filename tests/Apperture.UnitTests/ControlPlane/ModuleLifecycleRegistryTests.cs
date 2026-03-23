using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Operations;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Apperture.Core.Domain.Audit;

namespace Apperture.UnitTests.ControlPlane;

public sealed class ModuleLifecycleRegistryTests
{
    [Fact]
    public async Task ListModules_marks_discovered_modules_as_enabled_installations_for_compatibility()
    {
        await using var db = CreateDbContext();
        var service = new SqlModuleRegistryService([new TestModule("M1")], db, new TestEnvironmentContext(), new InMemoryAuditService());

        var modules = await service.ListModulesAsync();

        modules.Should().ContainSingle();
        modules[0].Descriptor.ModuleId.Should().Be("M1");
        modules[0].Enabled.Should().BeFalse();
        modules[0].State.Should().Be(ModuleLifecycleState.Discovered);
    }

    [Fact]
    public async Task DisableModule_blocks_when_persisted_references_exist()
    {
        await using var db = CreateDbContext();
        db.WorkflowInstances.Add(new WorkflowInstanceEntity
        {
            Id = "wf-1",
            DefinitionId = "M1.Workflow",
            Status = "running",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new SqlModuleRegistryService([new TestModule("M1")], db, new TestEnvironmentContext(), new InMemoryAuditService());
        await service.InstallModuleAsync("M1");
        await service.EnableModuleAsync("M1");

        var result = await service.DisableModuleAsync("M1");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "active_workflow_references");
    }

    [Fact]
    public async Task EnableModule_blocks_on_workflow_conflict_with_enabled_module()
    {
        await using var db = CreateDbContext();
        var service = new SqlModuleRegistryService(
            [new TestModule("M1"), new TestModule("M2", workflowId: "M1.Workflow")],
            db,
            new TestEnvironmentContext(),
            new InMemoryAuditService());

        await service.InstallModuleAsync("M1");
        await service.EnableModuleAsync("M1");
        await service.InstallModuleAsync("M2");
        var result = await service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "workflow_id_conflict");
    }

    [Fact]
    public async Task EnableModule_blocks_on_operation_conflict_with_enabled_module()
    {
        await using var db = CreateDbContext();
        var service = new SqlModuleRegistryService(
            [new TestModule("M1"), new TestModule("M2", operationId: "M1.Operation")],
            db,
            new TestEnvironmentContext(),
            new InMemoryAuditService());

        await service.InstallModuleAsync("M1");
        await service.EnableModuleAsync("M1");
        await service.InstallModuleAsync("M2");
        var result = await service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "operation_id_conflict");
    }

    [Fact]
    public async Task EnableModule_blocks_on_navigation_conflict_with_enabled_module()
    {
        await using var db = CreateDbContext();
        var service = new SqlModuleRegistryService(
            [new TestModule("M1", navRoute: "/common"), new TestModule("M2", navRoute: "/common")],
            db,
            new TestEnvironmentContext(),
            new InMemoryAuditService());

        await service.InstallModuleAsync("M1");
        await service.EnableModuleAsync("M1");
        await service.InstallModuleAsync("M2");
        var result = await service.EnableModuleAsync("M2");

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().Contain(x => x.Code == "navigation_route_conflict");
    }

    [Fact]
    public async Task ListModules_surfaces_missing_descriptor_for_persisted_enabled_module()
    {
        await using var db = CreateDbContext();
        db.ModuleInstallationStates.Add(new ModuleInstallationStateEntity
        {
            EnvironmentId = "test",
            ModuleId = "Missing.Module",
            Version = "1.0.0",
            Enabled = true,
            InstalledAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new SqlModuleRegistryService([], db, new TestEnvironmentContext(), new InMemoryAuditService());
        var modules = await service.ListModulesAsync();

        modules.Should().ContainSingle();
        modules[0].EffectivelyActive.Should().BeFalse();
        modules[0].ValidationIssues.Should().Contain(x => x.Code == "descriptor_missing");
    }

    [Fact]
    public async Task Enable_disable_uninstall_emit_audit_records_with_effect_semantics()
    {
        await using var db = CreateDbContext();
        var audit = new InMemoryAuditService();
        var service = new SqlModuleRegistryService([new TestModule("M1")], db, new TestEnvironmentContext(), audit);

        await service.InstallModuleAsync("M1");
        var enable = await service.EnableModuleAsync("M1");
        var disable = await service.DisableModuleAsync("M1");
        var uninstall = await service.UninstallModuleAsync("M1");

        enable.Effect.Should().Be(ModuleLifecycleEffect.PartialUntilRestart);
        disable.Effect.Should().Be(ModuleLifecycleEffect.PartialUntilRestart);
        uninstall.Effect.Should().Be(ModuleLifecycleEffect.RequiresRestart);
        audit.Records.Should().Contain(x => x.Action == "module.lifecycle.enable");
        audit.Records.Should().Contain(x => x.Action == "module.lifecycle.disable");
        audit.Records.Should().Contain(x => x.Action == "module.lifecycle.uninstall");
    }

    private static AppertureDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppertureDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppertureDbContext(options);
    }

    private sealed class TestEnvironmentContext : IEnvironmentContext
    {
        public string EnvironmentId => "test";
    }

    private sealed class TestModule : IModule, IOperationDefinitionProvider
    {
        private readonly string _moduleId;
        private readonly string _workflowId;
        private readonly string _operationId;
        private readonly string _navRoute;

        public TestModule(string moduleId, string? workflowId = null, string? operationId = null, string? navRoute = null)
        {
            _moduleId = moduleId;
            _workflowId = workflowId ?? $"{moduleId}.Workflow";
            _operationId = operationId ?? $"{moduleId}.Operation";
            _navRoute = navRoute ?? $"/{moduleId.ToLowerInvariant()}";
        }

        public ModuleManifest GetManifest() => new(_moduleId, _moduleId, "1.0.0", "test");

        public ValueTask<ModuleDescriptor> BuildDescriptorAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ModuleDescriptor(
                _moduleId,
                _moduleId,
                "1.0.0",
                "test",
                null,
                null,
                [
                    new WorkflowContributionDescriptor(_workflowId, "TestWorkflow"),
                    new OperationContributionDescriptor(_operationId, "TestOp"),
                    new NavigationContributionDescriptor("Runs", "Test", _navRoute),
                ]));

        public async IAsyncEnumerable<WorkflowDefinition> GetDefinitionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new WorkflowDefinition(new WorkflowDefinitionId(_workflowId), "Test", _moduleId, ["Step1"]);
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<OperationDefinition> GetOperationsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new OperationDefinition(new OperationId(_operationId), "TestOp", _moduleId, OperationCategory.Command, null, []);
            await Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditService : IAuditService
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditPage(Records, null));

        public ValueTask<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            var q = Records.AsEnumerable();
            if (query.FromUtc.HasValue)
                q = q.Where(r => r.Timestamp >= query.FromUtc.Value);
            if (query.ToUtc.HasValue)
                q = q.Where(r => r.Timestamp <= query.ToUtc.Value);
            return ValueTask.FromResult(q.Count());
        }

        public ValueTask<AuditRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<AuditRecord?>(Records.FirstOrDefault(r => r.Id.Value == id));
    }
}
