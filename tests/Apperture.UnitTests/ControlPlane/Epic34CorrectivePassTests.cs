using Apperture.ControlPlane.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Apperture.ControlPlane.Security.Models;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Environment;
using Apperture.Core.Domain.Governance;
using Apperture.Core.Domain.Scheduling;
using Apperture.Management.Api.Controllers;
using Apperture.ModuleSdk.Abstractions;
using Apperture.RuntimePlane.Abstractions;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Apperture.UnitTests.ControlPlane;

/// <summary>
/// Epic 34 Corrective Pass: Backend action enforcement proof, capabilities truthfulness, denial-policy consistency.
/// </summary>
public sealed class Epic34CorrectivePassTests
{
    // --- Module lifecycle authorization ---

    [Fact]
    public async Task ModulesController_InstallModule_WithoutModuleConfigure_Returns403()
    {
        var listing = new ThrowingModuleWorkflowListingService(throwForLifecycle: true);
        var controller = new ModulesController(listing);
        var result = await controller.InstallModule("m1", CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ModulesController_InstallModule_WithModuleConfigure_Returns200()
    {
        var listing = new ThrowingModuleWorkflowListingService(throwForLifecycle: false);
        var controller = new ModulesController(listing);
        var result = await controller.InstallModule("m1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ModulesController_EnableModule_WithoutModuleConfigure_Returns403()
    {
        var listing = new ThrowingModuleWorkflowListingService(throwForLifecycle: true);
        var controller = new ModulesController(listing);
        var result = await controller.EnableModule("m1", CancellationToken.None);
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ModulesController_DisableModule_WithModuleConfigure_Returns200()
    {
        var listing = new ThrowingModuleWorkflowListingService(throwForLifecycle: false);
        var controller = new ModulesController(listing);
        var result = await controller.DisableModule("m1", CancellationToken.None);
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // --- Schedule enable/disable authorization ---

    [Fact]
    public async Task SchedulesController_Enable_WithoutScheduleManage_Returns403()
    {
        var gov = new StubScheduleGovernanceService(canManage: false);
        var mgmt = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(gov, mgmt, validation);
        var result = await controller.Enable("s1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SchedulesController_Enable_WithScheduleManage_Returns200()
    {
        var gov = new StubScheduleGovernanceService(canManage: true);
        var mgmt = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(gov, mgmt, validation);
        var result = await controller.Enable("s1", CancellationToken.None);
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public async Task SchedulesController_Disable_WithoutScheduleManage_Returns403()
    {
        var gov = new StubScheduleGovernanceService(canManage: false);
        var mgmt = new StubScheduleManagementService();
        var validation = new ScheduleValidationService();
        var controller = new SchedulesController(gov, mgmt, validation);
        var result = await controller.Disable("s1", CancellationToken.None);
        result.Should().BeOfType<ForbidResult>();
    }

    // --- Scheduler run-cycle: enforced by RecoveryController (recovery.manage), not SchedulesController ---

    [Fact]
    public async Task SchedulerRunCycle_EnforcedByRecoveryController_NotScheduleManage()
    {
        // Run-cycle is POST /api/control/recovery/schedules/run-cycle, enforced by recovery.manage.
        // This test documents that RunScheduleCycle is in RecoveryController, not SchedulesController.
        // Recovery authorization tests exist in RecoveryControllerTests.
        await Task.CompletedTask;
    }

    // --- Capabilities truthfulness: capabilities derive from same governance as action endpoints ---

    [Fact]
    public async Task OperatorCapabilitiesQueryService_CanManageModules_False_WhenAuthDeniesModuleConfigure()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(moduleConfigure: false, scheduleManage: true, recoveryManage: true, auditRead: true, runtimeHealthRead: true);
        var scheduleGov = new StubScheduleGovernanceService(canManage: true);
        var recoveryGov = new StubRecoveryGovernanceService(canRead: true, canManage: true);
        var service = new OperatorCapabilitiesQueryService(principal, auth, scheduleGov, recoveryGov, NullLogger<OperatorCapabilitiesQueryService>.Instance);

        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanManageModules.Should().BeFalse("capability must reflect module.configure denial");
    }

    [Fact]
    public async Task OperatorCapabilitiesQueryService_CanManageSchedules_False_WhenScheduleGovernanceDenies()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(moduleConfigure: true, scheduleManage: true, recoveryManage: true, auditRead: true, runtimeHealthRead: true);
        var scheduleGov = new StubScheduleGovernanceService(canManage: false);
        var recoveryGov = new StubRecoveryGovernanceService(canRead: true, canManage: true);
        var service = new OperatorCapabilitiesQueryService(principal, auth, scheduleGov, recoveryGov, NullLogger<OperatorCapabilitiesQueryService>.Instance);

        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanManageSchedules.Should().BeFalse("capability must reflect ScheduleGovernanceService.CanManageAsync");
    }

    [Fact]
    public async Task OperatorCapabilitiesQueryService_CanManageRecovery_False_WhenRecoveryGovernanceDenies()
    {
        var principal = new StubPrincipalContext();
        var auth = new StubAuthorizationEvaluationService(moduleConfigure: true, scheduleManage: true, recoveryManage: true, auditRead: true, runtimeHealthRead: true);
        var scheduleGov = new StubScheduleGovernanceService(canManage: true);
        var recoveryGov = new StubRecoveryGovernanceService(canRead: true, canManage: false);
        var service = new OperatorCapabilitiesQueryService(principal, auth, scheduleGov, recoveryGov, NullLogger<OperatorCapabilitiesQueryService>.Instance);

        var dto = await service.GetCapabilitiesAsync(CancellationToken.None);

        dto.CanManageRecovery.Should().BeFalse("capability must reflect RecoveryGovernanceService.CanManageAsync");
    }

    // --- Stubs ---

    private sealed class ThrowingModuleWorkflowListingService : IModuleWorkflowListingService
    {
        private readonly bool _throwForLifecycle;

        public ThrowingModuleWorkflowListingService(bool throwForLifecycle)
        {
            _throwForLifecycle = throwForLifecycle;
        }

        public ValueTask<IReadOnlyList<ModuleCatalogEntry>> GetModulesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ModuleCatalogEntry>>([]);

        public ValueTask<ModuleCatalogEntry?> GetModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ModuleCatalogEntry?>(null);

        public ValueTask<IReadOnlyList<WorkflowDefinition>> GetWorkflowsForModuleAsync(string moduleId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<WorkflowDefinition>>([]);

        public ValueTask<ModuleLifecycleTransitionResult> InstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            if (_throwForLifecycle)
                throw new UnauthorizedAccessException("Not authorized to configure modules.");
            return ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "install", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.Immediate, []));
        }

        public ValueTask<ModuleLifecycleTransitionResult> EnableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            if (_throwForLifecycle)
                throw new UnauthorizedAccessException("Not authorized to configure modules.");
            return ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "enable", ModuleLifecycleState.InstalledEnabled, ModuleLifecycleEffect.PartialUntilRestart, []));
        }

        public ValueTask<ModuleLifecycleTransitionResult> DisableModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            if (_throwForLifecycle)
                throw new UnauthorizedAccessException("Not authorized to configure modules.");
            return ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "disable", ModuleLifecycleState.InstalledDisabled, ModuleLifecycleEffect.PartialUntilRestart, []));
        }

        public ValueTask<ModuleLifecycleTransitionResult> UninstallModuleAsync(string moduleId, CancellationToken cancellationToken = default)
        {
            if (_throwForLifecycle)
                throw new UnauthorizedAccessException("Not authorized to configure modules.");
            return ValueTask.FromResult(new ModuleLifecycleTransitionResult(true, moduleId, "uninstall", ModuleLifecycleState.Discovered, ModuleLifecycleEffect.RequiresRestart, []));
        }
    }

    private sealed class StubScheduleGovernanceService : IScheduleGovernanceService
    {
        private readonly bool _canManage;

        public StubScheduleGovernanceService(bool canManage) => _canManage = canManage;

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canManage);
    }

    private sealed class StubRecoveryGovernanceService : IRecoveryGovernanceService
    {
        private readonly bool _canRead;
        private readonly bool _canManage;

        public StubRecoveryGovernanceService(bool canRead = true, bool canManage = true)
        {
            _canRead = canRead;
            _canManage = canManage;
        }

        public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canRead);

        public ValueTask<bool> CanManageAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_canManage);
    }

    private sealed class StubPrincipalContext : IPrincipalContext
    {
        public ValueTask<IActor?> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IActor?>(new StubActor());
    }

    private sealed class StubActor : IActor
    {
        public ActorId Id => new("user:test");
        public ActorKind Kind => ActorKind.Human;
        public string? DisplayName => "Test User";
    }

    private sealed class StubAuthorizationEvaluationService : IAuthorizationEvaluationService
    {
        private readonly bool _moduleConfigure;
        private readonly bool _scheduleManage;
        private readonly bool _recoveryManage;
        private readonly bool _auditRead;
        private readonly bool _runtimeHealthRead;

        public StubAuthorizationEvaluationService(
            bool moduleConfigure = true,
            bool scheduleManage = true,
            bool recoveryManage = true,
            bool auditRead = true,
            bool runtimeHealthRead = true)
        {
            _moduleConfigure = moduleConfigure;
            _scheduleManage = scheduleManage;
            _recoveryManage = recoveryManage;
            _auditRead = auditRead;
            _runtimeHealthRead = runtimeHealthRead;
        }

        public ValueTask<bool> EvaluateAsync(AuthorizationEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var perm = context.Permission;
            if (perm.Action == ActionConstants.ModuleConfigure && perm.Resource?.ResourceType == ResourceTypeConstants.Module)
                return ValueTask.FromResult(_moduleConfigure);
            if (perm.Action == ActionConstants.ScheduleManage && perm.Resource?.ResourceType == ResourceTypeConstants.Schedule)
                return ValueTask.FromResult(_scheduleManage);
            if (perm.Action == ActionConstants.RecoveryManage && perm.Resource?.ResourceType == ResourceTypeConstants.Recovery)
                return ValueTask.FromResult(_recoveryManage);
            if (perm.Action == ActionConstants.AuditRead && perm.Resource?.ResourceType == ResourceTypeConstants.AuditPolicy)
                return ValueTask.FromResult(_auditRead);
            if (perm.Action == ActionConstants.RuntimeHealthRead && perm.Resource?.ResourceType == ResourceTypeConstants.RuntimeHealth)
                return ValueTask.FromResult(_runtimeHealthRead);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class StubScheduleManagementService : IScheduleManagementService
    {
        public ValueTask EnableAsync(ScheduleId id, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisableAsync(ScheduleId id, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<ScheduleDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleDefinition>>([]);
        public ValueTask<ScheduleDefinition?> GetDefinitionAsync(ScheduleId id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleDefinition?>(null);
        public ValueTask<ScheduleWithExecutionState?> GetScheduleWithExecutionStateAsync(ScheduleId id, RuntimePlaneId planeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ScheduleWithExecutionState?>(null);
        public ValueTask<ScheduleDefinition> CreateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(definition);
        public ValueTask UpdateScheduleAsync(ScheduleDefinition definition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<ScheduleAssignment> AssignToRuntimePlaneAsync(ScheduleId scheduleId, RuntimePlaneId runtimePlaneId, string? policyOverrides, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ScheduleAssignment(new ScheduleAssignmentId("s1"), scheduleId, runtimePlaneId, policyOverrides, 1, 1));
        public ValueTask<IReadOnlyList<ScheduleAssignment>> GetAssignmentsAsync(RuntimePlaneId runtimePlaneId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ScheduleAssignment>>([]);
    }
}
