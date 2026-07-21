using Hap.Synth;
using Xunit;

namespace Hap.Synth.Tests;

/// <summary>
/// Verifies the deterministic synthetic-directory generator against HAP-2's
/// acceptance criteria: byte-identical determinism, population shape, every
/// engineered edge case (a)-(j) asserted by name, seed users, and referential
/// integrity. All assertions run against the canonical seed unless noted.
/// </summary>
public sealed class DirectoryGeneratorTests
{
    private static readonly GeneratedDirectory Canonical =
        DirectoryGenerator.Generate(Distributions.CanonicalSeed);

    private static DirectorySnapshot Snapshot => Canonical.Snapshot;
    private static IReadOnlyList<PersonRecord> People => Canonical.Snapshot.Persons;

    private static IReadOnlyList<PersonRecord> ActiveReportsOf(string managerRef) =>
        People.Where(p => p.IsActive && p.ManagerExternalRef == managerRef).ToList();

    private static IReadOnlyList<PersonRecord> AllReportsOf(string managerRef) =>
        People.Where(p => p.ManagerExternalRef == managerRef).ToList();

    private static Dictionary<string, int> CountByBu() =>
        People.GroupBy(p => p.BuCode).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static PersonRecord ByRef(string externalRef) =>
        People.Single(p => p.ExternalRef == externalRef);

    // --- Determinism (criterion 2) -------------------------------------------

    [Fact]
    public void Determinism_TwoRunsAreByteIdentical()
    {
        string first = SnapshotSerializer.SerializeSnapshot(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed).Snapshot);
        string second = SnapshotSerializer.SerializeSnapshot(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed).Snapshot);

        Assert.Equal(first, second);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(first),
            System.Text.Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void Determinism_SeedUsersAreByteIdenticalAcrossRuns()
    {
        string first = SnapshotSerializer.SerializeSeedUsers(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed).SeedUsers);
        string second = SnapshotSerializer.SerializeSeedUsers(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed).SeedUsers);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Determinism_DifferentSeedProducesDifferentOutput()
    {
        string canonical = SnapshotSerializer.SerializeSnapshot(Snapshot);
        string other = SnapshotSerializer.SerializeSnapshot(
            DirectoryGenerator.Generate(Distributions.CanonicalSeed + 1).Snapshot);

        Assert.NotEqual(canonical, other);
    }

    [Fact]
    public void RoundTrip_SnapshotSurvivesSerialisation()
    {
        string json = SnapshotSerializer.SerializeSnapshot(Snapshot);
        DirectorySnapshot restored = SnapshotSerializer.DeserializeSnapshot(json);

        Assert.Equal(Snapshot.Persons.Count, restored.Persons.Count);
        Assert.Equal(Snapshot.Bus.Count, restored.Bus.Count);
        Assert.Equal(Snapshot.Metadata.Seed, restored.Metadata.Seed);
    }

    // --- Metadata (criterion 1) ----------------------------------------------

    [Fact]
    public void Metadata_RecordsSeedAndGeneratorVersion()
    {
        Assert.Equal(Distributions.CanonicalSeed, Snapshot.Metadata.Seed);
        Assert.Equal(Distributions.GeneratorVersion, Snapshot.Metadata.GeneratorVersion);
        Assert.Equal(People.Count, Snapshot.Metadata.PersonCount);
        Assert.Equal(Snapshot.Bus.Count, Snapshot.Metadata.BuCount);
    }

    // --- Population shape (criterion 3) ---------------------------------------

    [Fact]
    public void Population_TwentyThreeBusSixGroupsThreePortfolios()
    {
        Assert.Equal(23, Snapshot.Bus.Count);
        Assert.Equal(6, Snapshot.Bus.Select(b => b.Group).Distinct().Count());
        Assert.Equal(3, Snapshot.Bus.Select(b => b.Portfolio).Distinct().Count());
    }

    [Fact]
    public void Population_TotalPeopleAtLeastTenThousand()
    {
        Assert.True(People.Count >= 10_000, $"expected >= 10,000 people, got {People.Count}");
    }

    [Fact]
    public void Population_AtLeastTwoThousandManagersWithActiveReport()
    {
        // SC-008's ">= 2,000 teams" is operationalised here as ">= 2,000 distinct
        // people who are the manager of at least one ACTIVE person" — i.e. a team is
        // a manager with a live direct report. Leavers/on-leave reports still count
        // their manager only if that manager also has another active report; a manager
        // whose reports have all left is not counted (their team has no live members).
        int managers = People
            .Where(p => p.IsActive && p.ManagerExternalRef is not null)
            .Select(p => p.ManagerExternalRef!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.True(managers >= 2_000, $"expected >= 2,000 managers, got {managers}");
    }

    [Fact]
    public void Population_OrdinaryBusEachHaveThreeHundredToEightHundredPeople()
    {
        // Q-009: the 300-800 band applies to ordinary (non-engineered) BUs only;
        // the three engineered-small BUs are deliberate exceptions.
        var engineered = new HashSet<string>(StringComparer.Ordinal)
        {
            Distributions.SubFourBuCode,
            Distributions.SingleTeamBuCode,
            Distributions.OrgOfSevenBuCode,
        };

        foreach (var (buCode, count) in CountByBu())
        {
            if (engineered.Contains(buCode))
            {
                continue;
            }

            Assert.True(count is >= 300 and <= 800,
                $"ordinary BU {buCode} has {count} people; expected 300-800");
        }
    }

    [Fact]
    public void Population_EveryBuInSnapshotIsPopulated()
    {
        var counts = CountByBu();
        foreach (var bu in Snapshot.Bus)
        {
            Assert.True(counts.ContainsKey(bu.Code) && counts[bu.Code] > 0,
                $"BU {bu.Code} has no people");
        }
    }

    // --- Engineered edge cases (criterion 4), asserted by name ----------------

    [Fact]
    public void EdgeCase_A_TeamOfExactlyThreePeople()
    {
        // A team of 3 people = the engineered team-3 manager + exactly 2 active reports.
        var reports = ActiveReportsOf(Distributions.Team3ManagerRef);
        Assert.Equal(2, reports.Count);
    }

    [Fact]
    public void EdgeCase_B_BusinessUnitWithFewerThanFourPeople()
    {
        int count = CountByBu()[Distributions.SubFourBuCode];
        Assert.True(count < 4, $"{Distributions.SubFourBuCode} expected < 4 people, got {count}");
    }

    [Fact]
    public void EdgeCase_C_BusinessUnitContainingASingleTeam()
    {
        string bu = Distributions.SingleTeamBuCode;
        var buPeople = People.Where(p => p.BuCode == bu).ToList();

        // Exactly one person in the BU manages anyone in the BU (a single team).
        var managersInBu = buPeople
            .Where(m => buPeople.Any(r => r.ManagerExternalRef == m.ExternalRef))
            .Select(m => m.ExternalRef)
            .ToList();

        // Derive the BU Lead ref from the configured single-team BU code (no hard-coded index).
        int singleTeamBuIndex = int.Parse(Distributions.SingleTeamBuCode.Substring(2));

        Assert.Single(managersInBu);
        Assert.Equal(Distributions.BuLeadRef(singleTeamBuIndex), managersInBu[0]);

        // Everyone except that single manager reports to it.
        foreach (var p in buPeople.Where(p => p.ExternalRef != managersInBu[0]))
        {
            Assert.Equal(managersInBu[0], p.ManagerExternalRef);
        }
    }

    [Fact]
    public void EdgeCase_D_TeamOfFourInsideOrgOfSeven()
    {
        int buCount = CountByBu()[Distributions.OrgOfSevenBuCode];
        Assert.Equal(7, buCount);

        // Team of 4 people = the team-4 manager + exactly 3 reports.
        var reports = AllReportsOf(Distributions.Team4ManagerRef);
        Assert.Equal(3, reports.Count);

        // Complement within the 7-person org is 3 (7 - 4).
        Assert.Equal(3, buCount - (reports.Count + 1));
    }

    [Fact]
    public void EdgeCase_E_PersonWithNullManager()
    {
        var person = ByRef(Distributions.NullManagerRef);
        Assert.Null(person.ManagerExternalRef);
        Assert.True(person.IsActive);
        // The engineered gap is distinct from the legitimate root exec.
        Assert.NotEqual(Distributions.ExecRef, person.ExternalRef);
    }

    [Fact]
    public void EdgeCase_F_ContractorsAtLeastFivePercentSpreadAcrossBus()
    {
        var contractors = People.Where(p => p.EmployeeType == "Contractor").ToList();
        double fraction = (double)contractors.Count / People.Count;
        Assert.True(fraction >= 0.05, $"contractors {fraction:P1} of population; expected >= 5%");

        int busWithContractors = contractors.Select(c => c.BuCode).Distinct().Count();
        Assert.True(busWithContractors >= 5,
            $"contractors present in only {busWithContractors} BUs; expected spread across >= 5");
    }

    [Fact]
    public void EdgeCase_G_InactiveLeaverExists()
    {
        Assert.Contains(People, p => !p.IsActive);
        Assert.False(ByRef(Distributions.LeaverRef).IsActive);
    }

    [Fact]
    public void EdgeCase_H_OnLeavePersonExists()
    {
        Assert.Contains(People, p => p.OnLeave);
        var onLeave = ByRef(Distributions.OnLeaveRef);
        Assert.True(onLeave.OnLeave);
        Assert.True(onLeave.IsActive, "on-leave people remain active (FR-069)");
    }

    [Fact]
    public void EdgeCase_I_ManagerWithReportInDifferentBu()
    {
        var report = ByRef(Distributions.CrossBuReportRef);
        var manager = ByRef(report.ManagerExternalRef!);
        Assert.NotEqual(manager.BuCode, report.BuCode);
        Assert.Equal(Distributions.CrossBuReportBuCode, report.BuCode);
    }

    [Fact]
    public void EdgeCase_J_ContractorManagingEmployees()
    {
        var contractorManager = ByRef(Distributions.ContractorManagerRef);
        Assert.Equal("Contractor", contractorManager.EmployeeType);

        var employeeReports = AllReportsOf(Distributions.ContractorManagerRef)
            .Where(r => r.EmployeeType == "Employee")
            .ToList();
        Assert.NotEmpty(employeeReports);
    }

    // --- Seed users (criterion 5) --------------------------------------------

    [Fact]
    public void SeedUsers_OnePerRoleWithStableResolvableRefs()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Individual"] = Distributions.SeedIndividualRef,
            ["Manager"] = Distributions.SeedManagerRef,
            ["BU Lead"] = Distributions.BuLeadRef(1),
            ["Group Leader"] = Distributions.GroupLeaderRef(1),
            ["Portfolio Leader"] = Distributions.PortfolioLeaderRef(1),
            ["HIG Executive"] = Distributions.ExecRef,
            ["Platform Admin"] = Distributions.AdminRef,
        };

        var users = Canonical.SeedUsers.Users;
        Assert.Equal(7, users.Count);
        Assert.Equal(expected.Keys.OrderBy(k => k), users.Select(u => u.Role).OrderBy(k => k));

        var refs = new HashSet<string>(People.Select(p => p.ExternalRef), StringComparer.Ordinal);
        foreach (var user in users)
        {
            Assert.Equal(expected[user.Role], user.ExternalRef);
            Assert.Contains(user.ExternalRef, refs); // resolves to a real person
        }

        // The seeded Manager genuinely manages someone (has >= 1 active report).
        Assert.NotEmpty(ActiveReportsOf(Distributions.SeedManagerRef));
    }

    // --- Referential integrity & uniqueness ----------------------------------

    [Fact]
    public void Integrity_EveryManagerRefResolvesToAPerson()
    {
        var refs = new HashSet<string>(People.Select(p => p.ExternalRef), StringComparer.Ordinal);
        foreach (var p in People.Where(p => p.ManagerExternalRef is not null))
        {
            Assert.Contains(p.ManagerExternalRef!, refs);
        }
    }

    [Fact]
    public void Integrity_ExternalRefsAreUnique()
    {
        Assert.Equal(People.Count, People.Select(p => p.ExternalRef).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Integrity_EmailsAreUnique()
    {
        Assert.Equal(People.Count, People.Select(p => p.Email).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Integrity_EveryPersonHasAKnownBuCode()
    {
        var buCodes = new HashSet<string>(Snapshot.Bus.Select(b => b.Code), StringComparer.Ordinal);
        foreach (var p in People)
        {
            Assert.Contains(p.BuCode, buCodes);
        }
    }

    [Fact]
    public void Integrity_ContractorStatusUsesKnownEmployeeTypes()
    {
        foreach (var p in People)
        {
            Assert.True(p.EmployeeType is "Employee" or "Contractor",
                $"unexpected employee_type '{p.EmployeeType}'");
        }
    }
}
