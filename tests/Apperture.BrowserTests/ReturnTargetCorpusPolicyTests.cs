using Apperture.Management.Ui.Shared.Auth;
using FluentAssertions;
using Xunit;

namespace Apperture.BrowserTests;

/// <summary>
/// SEC-1A2a: the SEC-1.0 corpus IS the contract for the shared return-target policy. These are ordinary fast tests (no
/// browser, not gated by <c>APPERTURE_BROWSER_TESTS</c>) so that a change to <see cref="LocalReturnUrl"/> that would make a
/// hostile value valid, or a legitimate deep link invalid, fails in a plain <c>dotnet test</c>, long before the ~20 minute
/// browser run proves the same thing in real navigation. They do not replace the browser evidence: they keep it honest.
/// </summary>
public sealed class ReturnTargetCorpusPolicyTests
{
    [Theory]
    [MemberData(nameof(ReturnTargetCorpus.HostileIds), MemberType = typeof(ReturnTargetCorpus))]
    public void Every_hostile_corpus_value_is_refused(string id) =>
        LocalReturnUrl.IsValid(ReturnTargetCorpus.ById(id).Value).Should().BeFalse(ReturnTargetCorpus.ById(id).Note);

    [Theory]
    [MemberData(nameof(ReturnTargetCorpus.AuthSurfaceIds), MemberType = typeof(ReturnTargetCorpus))]
    public void Every_auth_surface_corpus_value_is_refused(string id) =>
        LocalReturnUrl.Evaluate(ReturnTargetCorpus.ById(id).Value).Should().Be(LocalReturnUrlVerdict.ReservedAuthPath);

    [Theory]
    [MemberData(nameof(ReturnTargetCorpus.LegitimateIds), MemberType = typeof(ReturnTargetCorpus))]
    public void Every_legitimate_corpus_value_is_accepted_unchanged(string id)
    {
        var value = ReturnTargetCorpus.ById(id).Value;

        LocalReturnUrl.Validate(value).Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(ReturnTargetCorpus.LegitimateIds), MemberType = typeof(ReturnTargetCorpus))]
    public void Every_legitimate_corpus_value_survives_the_sign_in_url_round_trip(string id)
    {
        var value = ReturnTargetCorpus.ById(id).Value;
        var login = LoginRouteHelper.BuildLoginUrl(value, LoginRouteHelper.ExpiredReason);

        LoginRouteHelper.ReadReturnTarget("http://localhost:9001" + login).Should().Be(value);
    }
}
