using FluentAssertions;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.UnitTests.Licensing;

/// <summary>
/// The key a customer types once, on the phone, off an email (<c>LICENSING.md</c> §2).
/// </summary>
public sealed class LicenceKeyTests
{
    [Fact]
    public void A_minted_key_round_trips_through_its_canonical_form()
    {
        LicenceKey key = LicenceKey.NewKey();

        LicenceKey.Parse(key.Value).Should().Be(key);
        key.Value.Should().StartWith("VUMA-").And.HaveLength(4 + 4 + LicenceKey.BodyLength);
    }

    [Fact]
    public void Case_hyphens_and_spaces_do_not_matter()
    {
        LicenceKey key = LicenceKey.NewKey();

        LicenceKey.Parse(key.Value.ToLowerInvariant()).Should().Be(key);
        LicenceKey.Parse(key.Value.Replace("-", string.Empty, StringComparison.Ordinal)).Should().Be(key);
        LicenceKey.Parse($" {key.Value} ").Should().Be(key);
        LicenceKey.Parse(key.Body).Should().Be(key);
    }

    [Fact]
    public void The_three_characters_people_actually_confuse_are_folded()
    {
        // Crockford's whole point. O is not in the alphabet, so a key containing one can only have
        // come from somebody reading a zero — and a checksum failure would send them back to check a
        // key that was, as far as they can see, typed correctly.
        // Nineteen zeros weight to a checksum of zero, so this is a genuinely valid key.
        LicenceKey key = LicenceKey.Parse("VUMA-00000-00000-00000-00000");

        LicenceKey.Parse("VUMA-OOOOO-OOOOO-OOOOO-OOOOO").Should().Be(key);
    }

    [Fact]
    public void A_single_wrong_character_fails_the_checksum()
    {
        LicenceKey key = LicenceKey.NewKey();

        // Substitution — the commonest typing mistake — at every position, with every other character
        // in the alphabet. The odd positional weights make this exhaustive assertion possible: not one
        // of them may pass.
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        char[] body = key.Body.ToCharArray();

        for (int index = 0; index < body.Length; index++)
        {
            char original = body[index];

            foreach (char replacement in alphabet.Where(character => character != original))
            {
                body[index] = replacement;

                LicenceKey.TryParse(new string(body), out _)
                    .Should().BeFalse("'{0}' at position {1} is a typo", replacement, index);
            }

            body[index] = original;
        }
    }

    [Fact]
    public void A_transposition_fails_the_checksum()
    {
        // The second commonest mistake, and the reason the checksum is positionally weighted. An
        // unweighted sum would not notice two characters swapping places at all.
        //
        // Adjacent swaps are caught unless the two characters are exactly sixteen apart in the
        // alphabet, which is stated in LicenceKey's own remarks — so the pair is chosen to be a case
        // the checksum claims to catch rather than one it does not.
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        LicenceKey key = LicenceKey.NewKey();
        char[] body = key.Body.ToCharArray();

        for (int index = 0; index < body.Length - 2; index++)
        {
            int left = alphabet.IndexOf(body[index], StringComparison.Ordinal);
            int right = alphabet.IndexOf(body[index + 1], StringComparison.Ordinal);

            if (left == right || Math.Abs(left - right) == 16)
            {
                continue;
            }

            char[] swapped = (char[])body.Clone();
            (swapped[index], swapped[index + 1]) = (swapped[index + 1], swapped[index]);

            LicenceKey.TryParse(new string(swapped), out _).Should().BeFalse();
            return;
        }

        Assert.Fail("A random key had no transposable adjacent pair, which should not be possible.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("VUMA-123")]
    [InlineData("VUMA-AAAAA-AAAAA-AAAAA-AAAAAA")]
    [InlineData("ZNTH-AAAAA-AAAAA-AAAAA-AAAAA")]
    public void A_key_of_the_wrong_shape_is_refused(string candidate)
        => LicenceKey.TryParse(candidate, out _).Should().BeFalse();

    [Fact]
    public void The_stored_form_is_a_digest_and_never_the_key()
    {
        LicenceKey key = LicenceKey.NewKey();
        string digest = key.Digest();

        digest.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        digest.Should().NotContain(key.Body[..5]);

        // Stable, because it is the primary lookup for an activation.
        key.Digest().Should().Be(digest);
    }

    [Fact]
    public void Two_keys_do_not_share_a_digest()
    {
        LicenceKey.NewKey().Digest().Should().NotBe(LicenceKey.NewKey().Digest());
    }
}
