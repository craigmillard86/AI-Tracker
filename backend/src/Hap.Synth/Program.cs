using Hap.Synth;

// CLI entrypoint for the deterministic synthetic-directory generator.
// Usage:
//   dotnet run --project backend/src/Hap.Synth -- \
//     --seed <long> --out <directory.json> --seed-users <seed-users.json>
// Invoked with the canonical seed by scripts/synth/generate.sh.

long seed = Distributions.CanonicalSeed;
string outPath = Path.Combine("backend", "src", "Hap.Synth", "output", "directory.json");
string seedUsersPath = Path.Combine("backend", "src", "Hap.Synth", "output", "seed-users.json");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed" when i + 1 < args.Length:
            if (!long.TryParse(args[++i], out seed))
            {
                Console.Error.WriteLine($"ERROR: --seed must be an integer, got '{args[i]}'.");
                return 1;
            }
            break;
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--seed-users" when i + 1 < args.Length:
            seedUsersPath = args[++i];
            break;
        default:
            Console.Error.WriteLine($"ERROR: unrecognised argument '{args[i]}'.");
            return 1;
    }
}

GeneratedDirectory generated = DirectoryGenerator.Generate(seed);

string snapshotJson = SnapshotSerializer.SerializeSnapshot(generated.Snapshot);
string seedUsersJson = SnapshotSerializer.SerializeSeedUsers(generated.SeedUsers);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(seedUsersPath))!);
File.WriteAllText(outPath, snapshotJson);
File.WriteAllText(seedUsersPath, seedUsersJson);

int personCount = generated.Snapshot.Persons.Count;
int managerCount = generated.Snapshot.Persons
    .Where(p => p.IsActive && p.ManagerExternalRef is not null)
    .Select(p => p.ManagerExternalRef!)
    .Distinct(StringComparer.Ordinal)
    .Count();
int contractorCount = generated.Snapshot.Persons.Count(p => p.EmployeeType == "Contractor");

Console.WriteLine($"Synthetic directory generated (seed {seed}, v{Distributions.GeneratorVersion}):");
Console.WriteLine($"  BUs:         {generated.Snapshot.Bus.Count}");
Console.WriteLine($"  Persons:     {personCount}");
Console.WriteLine($"  Managers:    {managerCount} (distinct, with >= 1 active report)");
Console.WriteLine($"  Contractors: {contractorCount} ({contractorCount * 100.0 / personCount:F1}%)");
Console.WriteLine($"  Snapshot:    {outPath}");
Console.WriteLine($"  Seed users:  {seedUsersPath}");

return 0;
