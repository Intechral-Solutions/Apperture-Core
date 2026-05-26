using System.Net;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.PlatformSettings;
using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.Management.Api.Controllers;
using Apperture.Management.Api.Models;
using Apperture.Shared.Kernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Apperture.UnitTests.PlatformSettings;

public sealed class Epic114PlatformSettingOptionProviderTests
{
    [Fact]
    public async Task Options_endpoint_returns_provider_options_for_provider_backed_setting()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;

        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha", Description = "First option" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(It.IsAny<PlatformSettingDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var definitionRegistry = new SingleDefinitionRegistry(def);
        var optionProviderRegistry = new SingleOptionProviderRegistry(provider.Object);

        var principalContext = new Mock<IPrincipalContext>();
        var actor = new Mock<IActor>();
        actor.Setup(x => x.Id).Returns(new ActorId("actor-1"));
        actor.Setup(x => x.Kind).Returns(ActorKind.Human);
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);

        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            authorization.Object,
            definitionRegistry,
            optionProviderRegistry);

        var result = await controller.Options(def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        ok.Value.Should().BeAssignableTo<IReadOnlyList<PlatformSettingAllowedValueDto>>();

        var dtos = (IReadOnlyList<PlatformSettingAllowedValueDto>)ok.Value!;
        dtos.Should().HaveCount(2);
        dtos[0].Value.Should().Be("alpha");
        dtos[0].Label.Should().Be("Alpha");
    }

    [Fact]
    public async Task Options_endpoint_returns_bad_request_when_setting_is_not_provider_backed()
    {
        var def = PlatformSettingTestDefinitions.MultiSelectSample with
        {
            // Explicitly clear provider metadata (test-only)
            OptionProviderKey = null
        };

        var principalContext = new Mock<IPrincipalContext>();
        var actor = new Mock<IActor>();
        actor.Setup(x => x.Id).Returns(new ActorId("actor-1"));
        actor.Setup(x => x.Kind).Returns(ActorKind.Human);
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor.Object);

        var authorization = new Mock<IAuthorizationEvaluationService>();
        authorization.Setup(x => x.EvaluateAsync(It.IsAny<AuthorizationEvaluationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new ConfigPlatformSettingsController(
            new Mock<IPlatformSettingsService>().Object,
            principalContext.Object,
            authorization.Object,
            new SingleDefinitionRegistry(def),
            new SingleOptionProviderRegistry(new Mock<IPlatformSettingOptionProvider>().Object));

        var result = await controller.Options(def.Key, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetAsync_provider_backed_select_accepts_valid_value()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(def, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());
        resolver.Setup(x => x.GetResolvedAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(def, "alpha"));

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);
        repo.Setup(x => x.UpsertAsync(It.IsAny<PlatformSettingValue>(), null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var principalContext = new Mock<IPrincipalContext>();
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IActor?)null);

        var audit = new Mock<IAuditService>();

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            new SingleOptionProviderRegistry(provider.Object),
            principalContext.Object,
            audit.Object);

        await sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "alpha" },
            null,
            CancellationToken.None);
    }

    [Fact]
    public async Task SetAsync_provider_backed_select_rejects_invalid_value()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(def, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var principalContext = new Mock<IPrincipalContext>();
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IActor?)null);

        var audit = new Mock<IAuditService>();

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            new SingleOptionProviderRegistry(provider.Object),
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ScalarValue = "delta" },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not an allowed option*");
    }

    [Fact]
    public async Task SetAsync_provider_backed_multi_select_rejects_invalid_option()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedMultiSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(def, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var principalContext = new Mock<IPrincipalContext>();
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IActor?)null);

        var audit = new Mock<IAuditService>();

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            new SingleOptionProviderRegistry(provider.Object),
            principalContext.Object,
            audit.Object);

        var act = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "delta"] },
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not allowed options*");
    }

    [Fact]
    public async Task SetAsync_provider_backed_multi_select_enforces_max_items_and_rejects_duplicates()
    {
        var def = PlatformSettingTestDefinitions.ProviderBackedMultiSelectSample;
        var providerOptions = new[]
        {
            new PlatformSettingAllowedValue { Value = "alpha", Label = "Alpha" },
            new PlatformSettingAllowedValue { Value = "beta", Label = "Beta" },
            new PlatformSettingAllowedValue { Value = "gamma", Label = "Gamma" }
        };

        var provider = new Mock<IPlatformSettingOptionProvider>();
        provider.SetupGet(p => p.ProviderKey).Returns(PlatformSettingTestDefinitions.ProviderKey);
        provider.Setup(p => p.GetOptionsAsync(def, It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerOptions);

        var resolver = new Mock<IPlatformSettingsResolver>();
        resolver.Setup(x => x.ListResolvedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformSettingResolvedValue>());

        var repo = new Mock<IPlatformSettingValueRepository>();
        repo.Setup(x => x.GetByKeyAsync(def.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformSettingValue?)null);

        var principalContext = new Mock<IPrincipalContext>();
        principalContext.Setup(x => x.GetCurrentActorAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IActor?)null);

        var audit = new Mock<IAuditService>();

        var sut = new PlatformSettingsService(
            new SingleDefinitionRegistry(def),
            repo.Object,
            resolver.Object,
            new SingleOptionProviderRegistry(provider.Object),
            principalContext.Object,
            audit.Object);

        var actTooMany = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "beta", "gamma"] },
            null,
            CancellationToken.None);

        await actTooMany.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At most 2 value(s) are allowed.*");

        var actDuplicates = () => sut.SetAsync(
            def.Key,
            new PlatformSettingUpdatePayload { ArrayValues = ["alpha", "alpha"] },
            null,
            CancellationToken.None);

        await actDuplicates.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Duplicate values are not allowed.*");
    }

    private static PlatformSettingResolvedValue Resolved(PlatformSettingDefinition def, object currentValue) => new()
    {
        Key = def.Key,
        DisplayName = def.DisplayName,
        Description = def.Description,
        Category = def.Category,
        ValueType = def.ValueType,
        InputKind = def.InputKind,
        CurrentValue = currentValue,
        Source = PlatformSettingValueSource.Database
    };

    private sealed class SingleDefinitionRegistry(PlatformSettingDefinition definition) : IPlatformSettingDefinitionRegistry
    {
        public IReadOnlyList<PlatformSettingDefinition> GetAll() => [definition];

        public PlatformSettingDefinition? TryGet(string key) =>
            string.Equals(key, definition.Key, StringComparison.Ordinal) ? definition : null;
    }

    private sealed class SingleOptionProviderRegistry(IPlatformSettingOptionProvider provider) : IPlatformSettingOptionProviderRegistry
    {
        public bool TryGet(string providerKey, out IPlatformSettingOptionProvider providerOut)
        {
            if (string.Equals(providerKey, provider.ProviderKey, StringComparison.Ordinal))
            {
                providerOut = provider;
                return true;
            }

            providerOut = null!;
            return false;
        }
    }
}

