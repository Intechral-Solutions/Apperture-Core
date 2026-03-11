using Apperture.Core.Application.Services;
using Apperture.Core.Domain.Governance;
using Apperture.ControlPlane.Security;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

public sealed class ScopeContainmentEvaluatorTests
{
    private readonly IScopeContainmentEvaluator _evaluator = new ScopeContainmentEvaluator();

    [Fact]
    public void IsContained_returns_true_when_scopes_equal()
    {
        _evaluator.IsContained("instance", "instance").Should().BeTrue();
        _evaluator.IsContained("module:mod-1", "module:mod-1").Should().BeTrue();
    }

    [Fact]
    public void IsContained_returns_true_when_outer_is_instance()
    {
        _evaluator.IsContained("instance", "instance").Should().BeTrue();
        _evaluator.IsContained("module:mod-1", "instance").Should().BeTrue();
        _evaluator.IsContained("connection:conn-1", "instance").Should().BeTrue();
        _evaluator.IsContained("module:mod-1:slot:DbConnection", "instance").Should().BeTrue();
    }

    [Fact]
    public void IsContained_returns_true_when_inner_is_slot_within_module()
    {
        _evaluator.IsContained("module:mod-1:slot:DbConnection", "module:mod-1").Should().BeTrue();
    }

    [Fact]
    public void IsContained_returns_false_when_not_contained()
    {
        _evaluator.IsContained("instance", "module:mod-1").Should().BeFalse();
        _evaluator.IsContained("module:mod-1", "module:mod-2").Should().BeFalse();
        _evaluator.IsContained("module:mod-1:slot:X", "module:mod-2").Should().BeFalse();
    }

    [Fact]
    public void IsContained_returns_false_when_null_or_empty()
    {
        _evaluator.IsContained("", "instance").Should().BeFalse();
        _evaluator.IsContained("instance", "").Should().BeFalse();
    }

    [Fact]
    public void ScopeKeyConstants_produce_correct_formats()
    {
        ScopeKeyConstants.Instance.Should().Be("instance");
        ScopeKeyConstants.Module("mod-1").Should().Be("module:mod-1");
        ScopeKeyConstants.Connection("conn-1").Should().Be("connection:conn-1");
        ScopeKeyConstants.Operation("op-1").Should().Be("operation:op-1");
        ScopeKeyConstants.Slot("mod-1", "DbConnection").Should().Be("module:mod-1:slot:DbConnection");
    }
}
