using Apperture.Core.Application.Services;
using FluentAssertions;
using Xunit;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// The historical actor rules (IAM-9 design gate §17): literals stay, user ids stay, current and retired usernames and
/// uniquely linked Entra object ids resolve, and a value that means different users (or nothing) is never guessed.
/// </summary>
public sealed class HistoricalActorClassifierTests
{
    private const string Pat = "0a0000000000000000000001";
    private const string Erin = "0a0000000000000000000002";
    private const string UserA = "3f9a1c0e7b2d4a6f8e1c5b7d";
    private const string Bob = "0a0000000000000000000004";
    private const string Cara = "0a0000000000000000000005";
    private const string Dan = "0a0000000000000000000006";
    private const string Eve = "0a0000000000000000000007";
    private const string Finn = "0a0000000000000000000008";
    private const string Gus = "0a0000000000000000000009";
    private const string Ivy = "0a000000000000000000000a";
    private const string Jon = "0a000000000000000000000b";

    private const string ErinOid = "11111111-1111-4111-8111-111111111111";
    private const string SharedOid = "22222222-2222-4222-8222-222222222222"; // Cara and Dan, two tenants
    private const string EveOid = "33333333-3333-4333-8333-333333333333"; // Eve, two tenants
    private const string GusName = "44444444-4444-4444-8444-444444444444"; // Gus's grandfathered username and his own object id
    private const string IvyJon = "55555555-5555-4555-8555-555555555555"; // Ivy's grandfathered username, Jon's object id

    private static readonly HistoricalActorClassifier Classifier = new(
        userIds: [Pat, Erin, UserA, Bob, Cara, Dan, Eve, Finn, Gus, Ivy, Jon],
        usernameLedger:
        [
            ("pat.lee", Pat),
            ("pat.old", Pat), // retired
            ("erin.e", Erin),
            ("ann", UserA),
            ("bob.b", Bob),
            (UserA, Bob), // Bob's retired, grandfathered 24-hex username: User A's id
            (Finn, Finn), // Finn's grandfathered username equals his own id
            (GusName, Gus),
            (IvyJon, Ivy),
        ],
        entraLinks:
        [
            (ErinOid, Erin),
            (SharedOid, Cara),
            (SharedOid, Dan),
            (EveOid, Eve),
            (EveOid, Eve),
            (GusName, Gus),
            (IvyJon, Jon),
        ]);

    [Theory]
    [InlineData("system")]
    [InlineData("anonymous")]
    [InlineData("recovery")]
    [InlineData("emergency-cli")]
    [InlineData("system-worker")]
    [InlineData("system:scheduler")]
    [InlineData("system:a-future-subsystem")]
    public void Non_human_literals_from_ActorLiterals_are_left_unchanged(string value) =>
        Classifier.Classify(value).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Literal));

    [Fact]
    public void A_user_id_with_no_other_meaning_is_already_normalized() =>
        Classifier.Classify(Pat).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.AlreadyUserId));

    [Theory]
    [InlineData("pat.lee")] // current username
    [InlineData("pat.old")] // retired username: the ledger keeps it bound to Pat
    [InlineData("Pat.Lee")] // usernames compare case-insensitively, like the ledger key
    public void Current_and_retired_usernames_resolve_to_their_permanent_owner(string value) =>
        Classifier.Classify(value).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Resolved, Pat));

    [Fact]
    public void An_object_id_with_exactly_one_entra_link_resolves_to_its_owner() =>
        Classifier.Classify(ErinOid).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Resolved, Erin));

    [Fact]
    public void Object_ids_compare_case_insensitively() =>
        Classifier.Classify(ErinOid.ToUpperInvariant()).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Resolved, Erin));

    [Theory]
    [InlineData("ghost.user")]
    [InlineData("99999999-9999-4999-8999-999999999999")] // an object id nobody is linked to
    [InlineData("pat.lee ")] // byte-exact: a trailing-space variant is not the username
    [InlineData("11111111-1111-4111-8111-111111111111 ")] // nor is a trailing-space object id
    public void A_value_with_no_safe_match_is_unresolved(string value) =>
        Classifier.Classify(value).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Unresolved));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_empty_and_blank_values_are_unresolved(string? value) =>
        Classifier.Classify(value).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Unresolved));

    [Fact]
    public void A_user_id_that_is_also_another_users_username_is_ambiguous_never_either_user()
    {
        // The 24-hex collision found in the IAM-9.2 rollback review: User A's id is Bob's grandfathered username.
        var result = Classifier.Classify(UserA);

        result.Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Ambiguous));
        result.UserId.Should().BeNull();
    }

    [Fact]
    public void A_username_is_not_rewritten_to_a_user_id_that_would_itself_be_ambiguous()
    {
        // "ann" names only User A, but A's id is also Bob's retired username: the rewritten value could not be read back.
        Classifier.Classify("ann").Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Ambiguous));
    }

    [Fact]
    public void A_user_id_that_is_also_the_same_users_username_is_already_normalized() =>
        Classifier.Classify(Finn).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.AlreadyUserId));

    [Fact]
    public void An_object_id_linked_to_different_users_is_ambiguous() =>
        Classifier.Classify(SharedOid).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Ambiguous));

    [Fact]
    public void An_object_id_with_several_links_of_one_user_resolves_nothing_because_the_gate_requires_exactly_one_link() =>
        Classifier.Classify(EveOid).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Unresolved));

    [Fact]
    public void A_username_and_an_object_id_of_the_same_user_resolve_to_that_user() =>
        Classifier.Classify(GusName).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Resolved, Gus));

    [Fact]
    public void A_username_of_one_user_that_is_another_users_object_id_is_ambiguous() =>
        Classifier.Classify(IvyJon).Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Ambiguous));

    [Fact]
    public void A_literal_wins_even_when_a_grandfathered_username_has_the_same_spelling()
    {
        var withSystemUser = new HistoricalActorClassifier([Pat], [("system", Pat)], []);

        withSystemUser.Classify("system").Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Literal));
    }

    [Fact]
    public void A_username_whose_owner_is_ambiguous_in_the_ledger_snapshot_is_ambiguous()
    {
        // The ledger key is case-insensitive in the database; a snapshot that still disagrees is never guessed.
        var conflicting = new HistoricalActorClassifier([Pat, Bob], [("pat.lee", Pat), ("PAT.LEE", Bob)], []);

        conflicting.Classify("pat.lee").Should().Be(new HistoricalActorClassification(HistoricalActorCategory.Ambiguous));
    }
}
