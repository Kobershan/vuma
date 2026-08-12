using FluentAssertions;
using VumaRetail.Domain.Licensing;

namespace VumaRetail.UnitTests.Licensing;

/// <summary>
/// The hardware binding, and the tolerance that keeps it from ruining a Monday
/// (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// Table-driven across the weighting, as the stage brief asks. The two rows that matter are the first
/// two: replacing a network card must not break a licence, and moving to a different box must.
/// </remarks>
public sealed class HardwareFingerprintTests
{
    private static readonly Dictionary<FingerprintComponent, string> Machine = new()
    {
        [FingerprintComponent.MotherboardUuid] = "4C4C4544-0037-5A10-8046-B7C04F395632",
        [FingerprintComponent.MachineGuid] = "d0f1b0a4-2f1a-4c76-9a1e-0a0b0c0d0e0f",
        [FingerprintComponent.PrimaryMacAddress] = "F0DEF1A2B3C4",
        [FingerprintComponent.SystemVolumeSerial] = "1A2B-3C4D",
        [FingerprintComponent.CpuSignature] = "X64:8:X64",
    };

    public static TheoryData<string, FingerprintComponent[], int, bool> Changes => new()
    {
        // description, components replaced, expected score, still the same machine
        { "nothing changed", [], 11, true },
        { "network card replaced", [FingerprintComponent.PrimaryMacAddress], 9, true },
        { "data disk replaced", [FingerprintComponent.SystemVolumeSerial], 9, true },
        {
            "network card and disk replaced",
            [FingerprintComponent.PrimaryMacAddress, FingerprintComponent.SystemVolumeSerial],
            7,
            true
        },
        {
            "motherboard replaced",
            [FingerprintComponent.MotherboardUuid],
            8,
            true
        },
        {
            "motherboard, disk and machine GUID replaced",
            [
                FingerprintComponent.MotherboardUuid,
                FingerprintComponent.SystemVolumeSerial,
                FingerprintComponent.MachineGuid,
            ],
            3,
            false
        },
        {
            "moved to a different box entirely",
            [
                FingerprintComponent.MotherboardUuid,
                FingerprintComponent.MachineGuid,
                FingerprintComponent.PrimaryMacAddress,
                FingerprintComponent.SystemVolumeSerial,
                FingerprintComponent.CpuSignature,
            ],
            0,
            false
        },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Scores_a_changed_machine_against_the_bound_one(
        string description,
        FingerprintComponent[] replaced,
        int expectedScore,
        bool sameMachine)
    {
        _ = description;

        string salt = HardwareFingerprint.NewSalt();
        HardwareFingerprint bound = HardwareFingerprint.Capture(salt, Machine);

        Dictionary<FingerprintComponent, string> after = new(Machine);

        foreach (FingerprintComponent component in replaced)
        {
            after[component] = $"replaced-{component}";
        }

        HardwareFingerprint candidate = HardwareFingerprint.Capture(salt, after);

        bound.Score(candidate).Should().Be(expectedScore);
        bound.Matches(candidate).Should().Be(sameMachine);
    }

    [Fact]
    public void The_threshold_is_seven_of_eleven()
    {
        HardwareFingerprint.MaxScore.Should().Be(11);
        HardwareFingerprint.MatchThreshold.Should().Be(7);

        // The claim LICENSING.md §3 actually makes: the two components a business replaces are worth
        // four points together, which leaves a machine bound.
        (HardwareFingerprint.WeightOf(FingerprintComponent.PrimaryMacAddress)
            + HardwareFingerprint.WeightOf(FingerprintComponent.SystemVolumeSerial))
            .Should().Be(HardwareFingerprint.MaxScore - HardwareFingerprint.MatchThreshold);
    }

    [Fact]
    public void A_component_the_machine_cannot_report_scores_nothing_rather_than_matching()
    {
        string salt = HardwareFingerprint.NewSalt();

        HardwareFingerprint bound = HardwareFingerprint.Capture(salt, Machine);

        // A virtual machine with no motherboard UUID. Absence must not read as agreement — otherwise
        // every VM in the world matches every other VM's licence on the components neither can report.
        Dictionary<FingerprintComponent, string> partial = new(Machine);
        partial.Remove(FingerprintComponent.MotherboardUuid);

        bound.Score(HardwareFingerprint.Capture(salt, partial)).Should().Be(8);
    }

    [Fact]
    public void Nothing_raw_survives_the_capture()
    {
        HardwareFingerprint fingerprint = HardwareFingerprint.Capture(
            HardwareFingerprint.NewSalt(),
            Machine);

        // The property the whole design exists for: a database of customers' MAC addresses is a
        // liability with no upside.
        foreach (string reading in Machine.Values)
        {
            fingerprint.ComponentHashes.Values.Should().NotContain(reading);
            fingerprint.Digest().Should().NotContain(reading);
        }
    }

    [Fact]
    public void The_same_component_on_two_machines_does_not_match_across_components()
    {
        string salt = HardwareFingerprint.NewSalt();

        HardwareFingerprint first = HardwareFingerprint.Capture(
            salt,
            new Dictionary<FingerprintComponent, string> { [FingerprintComponent.MachineGuid] = "same" });

        HardwareFingerprint second = HardwareFingerprint.Capture(
            salt,
            new Dictionary<FingerprintComponent, string> { [FingerprintComponent.CpuSignature] = "same" });

        first.Score(second).Should().Be(0);
    }

    [Fact]
    public void A_different_salt_produces_a_different_hash_for_the_same_machine()
    {
        HardwareFingerprint first = HardwareFingerprint.Capture(HardwareFingerprint.NewSalt(), Machine);
        HardwareFingerprint second = HardwareFingerprint.Capture(HardwareFingerprint.NewSalt(), Machine);

        // Which is why a candidate reading is always hashed under the *stored* salt. Two independently
        // salted captures of one machine score zero against each other, and that is correct.
        first.Score(second).Should().Be(0);
        first.Digest().Should().NotBe(second.Digest());
    }

    [Fact]
    public void The_digest_does_not_depend_on_enumeration_order()
    {
        string salt = HardwareFingerprint.NewSalt();

        Dictionary<FingerprintComponent, string> reversed = Machine
            .Reverse()
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        HardwareFingerprint.Capture(salt, Machine).Digest()
            .Should().Be(HardwareFingerprint.Capture(salt, reversed).Digest());
    }
}
