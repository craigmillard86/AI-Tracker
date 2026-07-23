using Xunit;

namespace Hap.Architecture.Tests;

/// <summary>
/// HAP-12 final re-QA (fresh instance, CLAUDE.md §9): a precision finding on
/// <see cref="SeamBoundaryTests.Raw_score_display_reads_consult_the_erasure_ledger"/>, not a defect in
/// today's shipped code (every current display-read call site genuinely checks the ledger — verified by
/// direct reading of <c>ManagerModerationService</c>/<c>SelfAssessmentService</c>/
/// <c>PersonalDataExportService</c>). Non-blocking; recorded as an advisory trip-wire, not fixed here.
///
/// <para><b>The finding:</b> <c>FindDisplayReadsWithoutErasureCheck</c> is FILE-scoped, not call-site-scoped:
/// it sets <c>checksLedger = true</c> if the <see cref="ErasureLedgerReference"/> pattern matches ANYWHERE
/// in the file, then that one flag gates every <see cref="DisplayReadMethods"/> match in the WHOLE file. So
/// a file that already references the ledger for one read (e.g. <c>ManagerModerationService.cs</c>, which
/// legitimately calls <c>_ledger.IsErasedAsync</c> three times today) would NOT be flagged if a future author
/// added a second, unrelated display-read call in the same file with no local ledger check next to it — the
/// guard proves "this file uses the ledger somewhere", not "every read in this file is guarded". This is the
/// same coarseness the pre-existing <c>RollupSnapshot</c> query-surface guard has (a precedent, not a new
/// pattern introduced here), and the guard's own doc comment only ever promises file-level referencing — so
/// this documents the guard's REAL precision rather than asserting it violates its own contract.</para>
/// </summary>
public sealed class SeamBoundaryHap12QaFinalTests
{
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public void Erasure_display_guard_is_file_scoped_a_second_unguarded_read_in_an_already_guarded_file_escapes_it()
    {
        // A file with ONE properly-guarded display read (satisfies the file-level ledger-reference check)
        // and a SECOND display read with no ledger check anywhere near it.
        var alreadyGuardedFileWithASecondUnguardedRead = ("backend/src/Hap.Api/Authorization/SomeFutureService.cs", new[]
        {
            "var guarded = await _store.GetSelfAsync(personId, cycleId, ct);",
            "if (await _ledger.IsErasedAsync(personId, guarded.Assessment.Id, ct)) return Disclosed();",
            "// ... elsewhere in the SAME file, added later, with no ledger check near it:",
            "var unguarded = await _store.GetAllForPersonAsync(otherPersonId, ct);",
            "return RawProject(unguarded);",
        });

        var offenders = SeamBoundaryTests.FindDisplayReadsWithoutErasureCheck(
            new[] { alreadyGuardedFileWithASecondUnguardedRead }, exemptFiles: new[] { "____no_such_file____" });

        // Documents the ACTUAL (weaker-than-call-site) behaviour: the second, unguarded read is NOT caught,
        // because the file already satisfies the whole-file "checksLedger" flag via the first read. If this
        // assertion ever starts failing (i.e. the guard becomes call-site-precise), that is a genuine
        // improvement — update this test rather than treating the new failure as a regression.
        Assert.Empty(offenders);

        // Contrast: the SAME unguarded read in a file with NO ledger reference at all IS still caught (the
        // guard's baseline case continues to work — this isn't a fully vacuous guard, just coarser than
        // "every call site" might be assumed to mean from its doc-comment prose alone).
        var noLedgerAnywhere = ("backend/src/Hap.Api/Authorization/AnotherFutureService.cs", new[]
        {
            "var unguarded = await _store.GetAllForPersonAsync(otherPersonId, ct);",
            "return RawProject(unguarded);",
        });
        Assert.NotEmpty(SeamBoundaryTests.FindDisplayReadsWithoutErasureCheck(
            new[] { noLedgerAnywhere }, exemptFiles: new[] { "____no_such_file____" }));
    }
}
