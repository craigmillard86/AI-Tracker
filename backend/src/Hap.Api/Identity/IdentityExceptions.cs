namespace Hap.Api.Identity;

/// <summary>The sign-in key does not match any seeded user. Maps to 400 at the API.</summary>
public sealed class UnknownSeedUserException : Exception
{
    public UnknownSeedUserException(string message) : base(message)
    {
    }
}

/// <summary>The seeded user exists but the directory has not been synced yet (HAP-3's
/// <c>POST /api/admin/sync</c> has never run), so no <c>Person</c> row exists to sign in as.
/// Maps to 409 at the API.</summary>
public sealed class PersonNotSyncedException : Exception
{
    public PersonNotSyncedException(string message) : base(message)
    {
    }
}

/// <summary>The person resolved from the directory is deactivated (FR-024 leaver handling).
/// Maps to 400 at the API — a leaver cannot start a new session, though sessions already
/// established before deactivation are not force-revoked by this check.</summary>
public sealed class InactiveUserException : Exception
{
    public InactiveUserException(string message) : base(message)
    {
    }
}
