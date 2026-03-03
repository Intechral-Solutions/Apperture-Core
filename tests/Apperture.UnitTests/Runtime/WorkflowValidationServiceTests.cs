using Apperture.Runtime.Abstractions;
using Apperture.Runtime.Engine;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Runtime;

public sealed class WorkflowValidationServiceTests
{
    [Fact]
    public void Validate_returns_false_when_no_steps()
    {
        var def = new WorkflowDefinition(new WorkflowDefinitionId("x"), "Test", "M1", Array.Empty<string>());
        new WorkflowValidationService().Validate(def).Should().BeFalse();
    }

    [Fact]
    public void Validate_returns_true_when_has_steps()
    {
        var def = new WorkflowDefinition(new WorkflowDefinitionId("x"), "Test", "M1", new[] { "Step1" });
        new WorkflowValidationService().Validate(def).Should().BeTrue();
    }
}
