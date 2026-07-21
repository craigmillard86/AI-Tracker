namespace Hap.Synth;

/// <summary>
/// Deterministic synthetic-directory generator (research D8, FR-020). Given a
/// seed it produces a <see cref="GeneratedDirectory"/> whose <c>persons</c>/<c>bus</c>
/// arrays conform to the contract's <c>DirectorySnapshot</c> shape, plus a
/// seed-users file. All randomness flows from a single <see cref="DeterministicRandom"/>
/// stream drawn in a fixed order, so the same seed yields byte-identical output.
/// Every distribution parameter and engineered edge case comes from
/// <see cref="Distributions"/> — this class holds no magic numbers.
/// </summary>
public sealed class DirectoryGenerator
{
    private readonly long _seed;
    private readonly DeterministicRandom _rng;
    private readonly List<PersonBuilder> _people = new();
    private readonly List<BuRecord> _bus = new();

    // group index (1-based) and portfolio index (1-based) for each BU (1-based).
    private readonly Dictionary<int, int> _groupOfBu = new();
    private readonly Dictionary<int, int> _portfolioOfGroup = new();

    // per-BU running sequence for ordinary member refs.
    private readonly Dictionary<string, int> _seqByBu = new();

    private DirectoryGenerator(long seed)
    {
        _seed = seed;
        _rng = new DeterministicRandom(seed);
    }

    public static GeneratedDirectory Generate(long seed) =>
        new DirectoryGenerator(seed).Build();

    private GeneratedDirectory Build()
    {
        BuildOrgSkeleton();
        BuildOrgTopLeaders();
        BuildBusinessUnits();
        InjectEngineeredFixtures();

        var persons = _people.Select(p => p.ToRecord()).ToList();
        var snapshot = new DirectorySnapshot
        {
            Metadata = new SnapshotMetadata
            {
                Seed = _seed,
                GeneratorVersion = Distributions.GeneratorVersion,
                PersonCount = persons.Count,
                BuCount = _bus.Count,
            },
            Bus = _bus,
            Persons = persons,
        };

        return new GeneratedDirectory
        {
            Snapshot = snapshot,
            SeedUsers = BuildSeedUsers(),
        };
    }

    // --- org skeleton: 23 BUs across 6 groups / 3 portfolios -----------------
    private void BuildOrgSkeleton()
    {
        // portfolio-of-group: groups 1,2 -> P1; 3,4 -> P2; 5,6 -> P3
        for (int g = 1; g <= Distributions.GroupCount; g++)
        {
            _portfolioOfGroup[g] = ((g - 1) / (Distributions.GroupCount / Distributions.PortfolioCount)) + 1;
        }

        // assign BUs to groups per the reviewed BusPerGroup split.
        int bu = 1;
        for (int g = 1; g <= Distributions.GroupCount; g++)
        {
            for (int i = 0; i < Distributions.BusPerGroup[g - 1]; i++)
            {
                _groupOfBu[bu] = g;
                bu++;
            }
        }

        for (int b = 1; b <= Distributions.BusinessUnitCount; b++)
        {
            int g = _groupOfBu[b];
            int p = _portfolioOfGroup[g];
            _bus.Add(new BuRecord
            {
                Code = Distributions.BuCode(b),
                Name = $"{Distributions.BuCode(b)} — {NamePools.BusinessUnitNames[b - 1]}",
                Group = NamePools.GroupNames[g - 1],
                Portfolio = NamePools.PortfolioNames[p - 1],
            });
        }
    }

    private int FirstBuOfGroup(int group) =>
        Enumerable.Range(1, Distributions.BusinessUnitCount).First(b => _groupOfBu[b] == group);

    private int FirstBuOfPortfolio(int portfolio)
    {
        int firstGroup = Enumerable.Range(1, Distributions.GroupCount)
            .First(g => _portfolioOfGroup[g] == portfolio);
        return FirstBuOfGroup(firstGroup);
    }

    // --- org-top leaders: exec, portfolio, group, BU leads, admin ------------
    private void BuildOrgTopLeaders()
    {
        // HIG Executive (root; legitimate null manager).
        AddLeader(Distributions.ExecRef, Distributions.BuCode(1),
            "Group President (HIG Executive)", managerRef: null);

        // Portfolio leaders report to the exec; homed in the first BU of their portfolio.
        for (int p = 1; p <= Distributions.PortfolioCount; p++)
        {
            AddLeader(Distributions.PortfolioLeaderRef(p), Distributions.BuCode(FirstBuOfPortfolio(p)),
                "Portfolio Leader", managerRef: Distributions.ExecRef);
        }

        // Group leaders report to their portfolio leader; homed in the first BU of their group.
        for (int g = 1; g <= Distributions.GroupCount; g++)
        {
            int portfolio = _portfolioOfGroup[g];
            AddLeader(Distributions.GroupLeaderRef(g), Distributions.BuCode(FirstBuOfGroup(g)),
                "Group Leader", managerRef: Distributions.PortfolioLeaderRef(portfolio));
        }

        // BU leads (EVPs) report to their group leader; homed in their own BU.
        for (int b = 1; b <= Distributions.BusinessUnitCount; b++)
        {
            int group = _groupOfBu[b];
            AddLeader(Distributions.BuLeadRef(b), Distributions.BuCode(b),
                "EVP, Business Unit", managerRef: Distributions.GroupLeaderRef(group));
        }

        // Platform Admin: a real person carrying the admin grant (FR-056). Homed in BU01.
        AddLeader(Distributions.AdminRef, Distributions.BuCode(1),
            "Platform Administrator", managerRef: Distributions.BuLeadRef(1));
    }

    // --- per-BU internals ----------------------------------------------------
    private void BuildBusinessUnits()
    {
        for (int b = 1; b <= Distributions.BusinessUnitCount; b++)
        {
            string code = Distributions.BuCode(b);
            _seqByBu[code] = 1;

            if (code == Distributions.SubFourBuCode)
            {
                BuildSubFourBu(b);
            }
            else if (code == Distributions.SingleTeamBuCode)
            {
                BuildSingleTeamBu(b);
            }
            else if (code == Distributions.OrgOfSevenBuCode)
            {
                BuildOrgOfSevenBu(b);
            }
            else
            {
                BuildOrdinaryBu(b);
            }
        }
    }

    private void BuildOrdinaryBu(int buIndex)
    {
        string code = Distributions.BuCode(buIndex);
        string buLead = Distributions.BuLeadRef(buIndex);
        int target = _rng.Next(Distributions.OrdinaryBuMinPeople, Distributions.OrdinaryBuMaxPeople);

        // BU Lead already created in org-top and counts toward the BU total.
        int remaining = target - 1;

        while (remaining > 0)
        {
            if (remaining < Distributions.MinPeopleForDirector)
            {
                // Too few left to justify a director; attach directly under the BU Lead.
                while (remaining > 0)
                {
                    AddMember(code, buLead);
                    remaining--;
                }
                break;
            }

            string director = AddManagerPerson(code, buLead, "Director");
            remaining--;

            int teams = _rng.Next(Distributions.TeamsPerDirectorMin, Distributions.TeamsPerDirectorMax);
            for (int t = 0; t < teams && remaining > 0; t++)
            {
                if (remaining < 2)
                {
                    // Not enough for a lead + a report; attach leftover under the director.
                    while (remaining > 0)
                    {
                        AddMember(code, director);
                        remaining--;
                    }
                    break;
                }

                string teamLead = AddManagerPerson(code, director, "Engineering Manager");
                remaining--;

                int reports = _rng.Next(Distributions.ReportsPerTeamMin, Distributions.ReportsPerTeamMax);
                reports = Math.Min(reports, remaining);
                for (int m = 0; m < reports; m++)
                {
                    AddMember(code, teamLead);
                    remaining--;
                }
            }
        }
    }

    // edge (b): a BU with fewer than 4 people total (BU Lead + 2 members = 3).
    private void BuildSubFourBu(int buIndex)
    {
        string code = Distributions.BuCode(buIndex);
        string buLead = Distributions.BuLeadRef(buIndex);
        for (int i = 0; i < Distributions.SubFourBuMembers; i++)
        {
            AddMember(code, buLead);
        }
    }

    // edge (c): a BU whose whole org is a single team (BU Lead directly manages all members).
    private void BuildSingleTeamBu(int buIndex)
    {
        string code = Distributions.BuCode(buIndex);
        string buLead = Distributions.BuLeadRef(buIndex);
        for (int i = 0; i < Distributions.SingleTeamBuMembers; i++)
        {
            AddMember(code, buLead);
        }
    }

    // edge (d): org of exactly 7 with a team of exactly 4 (manager + 3), complement of 3.
    private void BuildOrgOfSevenBu(int buIndex)
    {
        string code = Distributions.BuCode(buIndex);
        string buLead = Distributions.BuLeadRef(buIndex);

        // Team of 4 people: the engineered team-4 manager + 3 active reports
        // (named/active so the "team of 4" is unambiguous for suppression tests).
        AddNamedManager(Distributions.Team4ManagerRef, code, buLead, "Engineering Manager");
        for (int i = 0; i < 3; i++)
        {
            AddNamedMember($"{Distributions.Team4ManagerRef}-R{i + 1}", code,
                Distributions.Team4ManagerRef, "Software Engineer", "Employee", true, false);
        }

        // Complement of 3: BU Lead + 2 people reporting directly to the BU Lead.
        for (int i = 0; i < 2; i++)
        {
            AddMember(code, buLead);
        }
        // Total = BU Lead(1) + team-4 manager(1) + 3 + 2 = 7.
    }

    // --- engineered fixtures (seed users + remaining named edge cases) --------
    private void InjectEngineeredFixtures()
    {
        string bu1 = Distributions.PrimaryFixtureBuCode;
        string buLead1 = Distributions.BuLeadRef(1);

        // Seed Manager + Individual (roles for dev sign-in). Manager has >= 1 active report.
        AddNamedManager(Distributions.SeedManagerRef, bu1, buLead1, "Engineering Manager");
        AddNamedMember(Distributions.SeedIndividualRef, bu1, Distributions.SeedManagerRef,
            "Software Engineer", employeeType: "Employee", isActive: true, onLeave: false);

        // edge (a): a team of exactly 3 people — the team-3 manager + 2 reports.
        AddNamedManager(Distributions.Team3ManagerRef, bu1, buLead1, "Engineering Manager");
        AddNamedMember(Distributions.Team3ManagerRef + "-R1", bu1, Distributions.Team3ManagerRef,
            "Software Engineer", "Employee", true, false);
        AddNamedMember(Distributions.Team3ManagerRef + "-R2", bu1, Distributions.Team3ManagerRef,
            "QA Engineer", "Employee", true, false);

        // edge (e): an active person with a null manager (directory gap; not the root).
        AddNamedMember(Distributions.NullManagerRef, bu1, managerRef: null,
            "Business Analyst", "Employee", true, false);

        // edge (j): a contractor who manages employees.
        AddNamedManager(Distributions.ContractorManagerRef, bu1, buLead1,
            "Delivery Lead (Contract)", employeeType: "Contractor");
        AddNamedMember(Distributions.ContractorManagerRef + "-R1", bu1, Distributions.ContractorManagerRef,
            "Software Engineer", "Employee", true, false);
        AddNamedMember(Distributions.ContractorManagerRef + "-R2", bu1, Distributions.ContractorManagerRef,
            "Support Specialist", "Employee", true, false);

        // edge (g): an explicit inactive leaver.
        AddNamedMember(Distributions.LeaverRef, bu1, buLead1,
            "Software Engineer", "Employee", isActive: false, onLeave: false);

        // edge (h): an explicit on-leave (still active) person.
        AddNamedMember(Distributions.OnLeaveRef, bu1, buLead1,
            "Software Engineer", "Employee", isActive: true, onLeave: true);

        // edge (i): a report homed in BU02 but managed by BU01's BU Lead (cross-BU chain).
        AddNamedMember(Distributions.CrossBuReportRef, Distributions.CrossBuReportBuCode, buLead1,
            "Solutions Consultant", "Employee", true, false);
    }

    // --- seed users: one per role, stable refs -------------------------------
    private SeedUsersFile BuildSeedUsers()
    {
        var byRef = _people.ToDictionary(p => p.ExternalRef, StringComparer.Ordinal);

        SeedUser Make(string role, string externalRef)
        {
            var p = byRef[externalRef];
            return new SeedUser
            {
                Role = role,
                ExternalRef = p.ExternalRef,
                Name = p.Name,
                Email = p.Email,
                BuCode = p.BuCode,
            };
        }

        var users = new List<SeedUser>
        {
            Make("Individual", Distributions.SeedIndividualRef),
            Make("Manager", Distributions.SeedManagerRef),
            Make("BU Lead", Distributions.BuLeadRef(1)),
            Make("Group Leader", Distributions.GroupLeaderRef(1)),
            Make("Portfolio Leader", Distributions.PortfolioLeaderRef(1)),
            Make("HIG Executive", Distributions.ExecRef),
            Make("Platform Admin", Distributions.AdminRef),
        };

        return new SeedUsersFile
        {
            Seed = _seed,
            GeneratorVersion = Distributions.GeneratorVersion,
            Users = users,
        };
    }

    // --- person construction helpers -----------------------------------------
    private string NextBuRef(string buCode)
    {
        int seq = _seqByBu[buCode];
        _seqByBu[buCode] = seq + 1;
        return $"HAP-{buCode}-{seq:D4}";
    }

    private void AddLeader(string externalRef, string buCode, string title, string? managerRef)
    {
        _people.Add(new PersonBuilder
        {
            ExternalRef = externalRef,
            BuCode = buCode,
            JobTitle = title,
            ManagerExternalRef = managerRef,
            EmployeeType = "Employee",
            IsActive = true,
            OnLeave = false,
        }.WithName(_rng));
    }

    private string AddManagerPerson(string buCode, string managerRef, string title)
    {
        string externalRef = NextBuRef(buCode);
        _people.Add(new PersonBuilder
        {
            ExternalRef = externalRef,
            BuCode = buCode,
            JobTitle = title,
            ManagerExternalRef = managerRef,
            EmployeeType = "Employee",
            IsActive = true,
            OnLeave = false,
        }.WithName(_rng));
        return externalRef;
    }

    private void AddNamedManager(string externalRef, string buCode, string managerRef,
        string title, string employeeType = "Employee")
    {
        _people.Add(new PersonBuilder
        {
            ExternalRef = externalRef,
            BuCode = buCode,
            JobTitle = title,
            ManagerExternalRef = managerRef,
            EmployeeType = employeeType,
            IsActive = true,
            OnLeave = false,
        }.WithName(_rng));
    }

    private void AddMember(string buCode, string managerRef)
    {
        string externalRef = NextBuRef(buCode);
        string employeeType = _rng.Chance(Distributions.ContractorRate) ? "Contractor" : "Employee";

        bool isActive = true;
        bool onLeave = false;
        if (_rng.Chance(Distributions.LeaverRate))
        {
            isActive = false;
        }
        else if (_rng.Chance(Distributions.OnLeaveRate))
        {
            onLeave = true;
        }

        _people.Add(new PersonBuilder
        {
            ExternalRef = externalRef,
            BuCode = buCode,
            JobTitle = _rng.Pick(NamePools.MemberTitles),
            ManagerExternalRef = managerRef,
            EmployeeType = employeeType,
            IsActive = isActive,
            OnLeave = onLeave,
        }.WithName(_rng));
    }

    private void AddNamedMember(string externalRef, string buCode, string? managerRef,
        string title, string employeeType, bool isActive, bool onLeave)
    {
        _people.Add(new PersonBuilder
        {
            ExternalRef = externalRef,
            BuCode = buCode,
            JobTitle = title,
            ManagerExternalRef = managerRef,
            EmployeeType = employeeType,
            IsActive = isActive,
            OnLeave = onLeave,
        }.WithName(_rng));
    }
}

/// <summary>Mutable internal builder; projected to the immutable
/// <see cref="PersonRecord"/> once the population is complete.</summary>
internal sealed class PersonBuilder
{
    public string ExternalRef { get; init; } = "";
    public string BuCode { get; init; } = "";
    public string JobTitle { get; init; } = "";
    public string? ManagerExternalRef { get; init; }
    public string EmployeeType { get; init; } = "Employee";
    public bool IsActive { get; init; } = true;
    public bool OnLeave { get; init; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";

    public PersonBuilder WithName(DeterministicRandom rng)
    {
        string first = rng.Pick(NamePools.FirstNames);
        string last = rng.Pick(NamePools.LastNames);
        Name = $"{first} {last}";
        // Email uniqueness is carried by the (unique) external_ref suffix.
        Email = $"{first}.{last}.{ExternalRef}@synth.hig.local".ToLowerInvariant();
        return this;
    }

    public PersonRecord ToRecord() => new()
    {
        ExternalRef = ExternalRef,
        Name = Name,
        Email = Email,
        JobTitle = JobTitle,
        ManagerExternalRef = ManagerExternalRef,
        BuCode = BuCode,
        EmployeeType = EmployeeType,
        IsActive = IsActive,
        OnLeave = OnLeave,
    };
}
