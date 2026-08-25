namespace InTest.Runtime;

/// <summary>
/// Runs every registered <see cref="IAssemblyFixture"/> and drains the <see cref="FixtureContext"/>
/// teardown they registered. <see cref="RunAsync"/> orders fixtures itself, via
/// <see cref="FixtureGraph.Order"/>, rather than trusting the caller to have ordered them first —
/// split that responsibility across both <c>TestHost</c> and here and either both order (harmless
/// duplication) or both assume the other already did (seeding silently runs in whatever order the
/// container resolved fixtures), and nothing in the existing test suite would catch the second:
/// <see cref="FixtureGraph"/> is tested in isolation, and every fixture elsewhere in this codebase
/// happens to be independent. Ordering here, on the path that actually runs fixtures, makes the
/// guarantee unbypassable.
/// </summary>
public static class FixtureRunner
{
    /// <summary>
    /// Runs <paramref name="fixtures"/> in <see cref="FixtureGraph"/> order against
    /// <paramref name="profile"/>. A fixture whose <see cref="IAssemblyFixture.AppliesTo"/> is
    /// non-empty and does not contain <paramref name="profile"/> is skipped, with a line naming
    /// the fixture and the profile reported via <paramref name="diagnostics"/>'s
    /// <see cref="IRunDiagnostics.Warn"/> — a fixture silently not running is otherwise
    /// indistinguishable, from the outside, from one that ran and did nothing, and that is
    /// precisely the "must reach the operator even on a passing run" intent
    /// <see cref="IRunDiagnostics.Warn"/> exists for (see <see cref="IRunDiagnostics"/>'s own
    /// doc). Skipping propagates: a fixture that <c>DependsOn</c> a skipped fixture is skipped
    /// too, transitively through any depth of chain, with its log line naming the dependency that
    /// caused it rather than restating the profile check. Running it anyway would be exactly the
    /// silent-wrong-state failure <c>AppliesTo</c> exists to prevent — it would seed against state
    /// its dependency never built — and a fixture that genuinely does not need that state should
    /// not declare the dependency. (This stays a <c>FixtureRunner</c> concern rather than moving
    /// into <see cref="FixtureGraph"/>: <see cref="FixtureGraph"/> is a pure ordering function
    /// that knows nothing about profiles, and only <see cref="RunAsync"/> has both the order and
    /// the profile to combine.)
    /// <para>
    /// A fixture that throws fails the whole run with a message naming which one (§13: an
    /// unhandled <c>AssemblyInitialize</c> exception otherwise fails every test with an error
    /// that does not say "setup broke"), stops every fixture after it — a later fixture may
    /// depend on state the failed one was building — and drains whatever cleanup is already
    /// registered, including the failed fixture's own, so a fixture that created rows before it
    /// threw does not leak them. The fixture's own exception survives as the thrown
    /// <see cref="FixtureLifecycleException"/>'s <see cref="Exception.InnerException"/>, so a bare
    /// <see cref="NullReferenceException"/> in a fixture still gives its real stack trace to
    /// whoever reads the CI log.
    /// </para>
    /// </summary>
    public static async Task RunAsync(
        IEnumerable<IAssemblyFixture> fixtures,
        FixtureContext context,
        string profile,
        IRunDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);

        var ordered = FixtureGraph.Order(fixtures as IReadOnlyList<IAssemblyFixture> ?? fixtures.ToList());

        // Types skipped so far this run, so a later fixture's DependsOn can be checked against
        // it. FixtureGraph.Order guarantees every dependency is processed before its dependent,
        // so by the time a fixture is reached here, every type it might depend on has already
        // been added (or not).
        var skippedTypes = new HashSet<Type>();

        foreach (var fixture in ordered)
        {
            // Checked before entering the try below, so a cancellation between fixtures
            // propagates as a raw OperationCanceledException, undrained. That is a deliberate,
            // not incidental, choice: v1-b decision 4 already says cleanup is not guaranteed on
            // cancellation, crash, or agent timeout — the out-of-band sweeper is the answer for
            // what that leaves behind — so RunAsync does not pretend otherwise by draining here.
            // A fixture that throws OperationCanceledException from inside its own
            // InitializeAsync is different: RunAsync cannot distinguish that from any other bug
            // in the fixture's code, so the catch below still drains and wraps it — see the
            // cancellation-specific wording there, though, which does not blame the fixture.
            cancellationToken.ThrowIfCancellationRequested();

            var type = fixture.GetType();

            // Empty (or null — a consumer project without nullable reference types can leave
            // this uninitialized) means "every profile"; a non-empty AppliesTo restricts to the
            // profiles it names.
            if (fixture.AppliesTo is { Length: > 0 } appliesTo && !appliesTo.Contains(profile, StringComparer.Ordinal))
            {
                diagnostics.Warn(
                    $"Skipping fixture '{TypeName(type)}': its AppliesTo does not include profile '{profile}'.");
                skippedTypes.Add(type);
                continue;
            }

            // DependsOn is never null here — FixtureGraph.Order already rejected that. The first
            // skipped dependency, in DependsOn order, is named so the reader has one concrete
            // place to look; if it is itself a transitive skip, its own log line above names the
            // dependency that caused that one, so the chain is traceable line by line.
            var skippedDependency = fixture.DependsOn.FirstOrDefault(skippedTypes.Contains);
            if (skippedDependency is not null)
            {
                diagnostics.Warn(
                    $"Skipping fixture '{TypeName(type)}': its dependency '{TypeName(skippedDependency)}' " +
                    $"does not apply to profile '{profile}'.");
                skippedTypes.Add(type);
                continue;
            }

            try
            {
                await fixture.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Drain now, while we still know which fixture caused the failure, rather than
                // leaving it to whatever caller happens to invoke DrainAsync next. Captured as a
                // message rather than allowed to propagate on its own, so a drain failure adds to
                // the report instead of replacing — and hiding — the fixture failure that is the
                // actual reason the run is failing.
                var drainFailure = await TryDrainAfterFailureAsync(context).ConfigureAwait(false);

                // A cancellation that lands mid-fixture (e.g. a real CI timeout) is not a bug in
                // that fixture's code, so it gets wording that says so instead of sending the
                // reader after "the underlying error" — there is not one.
                var message = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                    ? $"Fixture '{TypeName(type)}' did not finish InitializeAsync because the run was " +
                      $"cancelled: {ex.Message}."
                    : $"Fixture '{TypeName(type)}' failed during InitializeAsync: {ex.Message}. Fix the " +
                      "underlying error in that fixture; later fixtures were not run because they may " +
                      "depend on the state this one was building.";

                if (drainFailure is not null)
                {
                    message += " Draining cleanup already registered before the failure also failed: " +
                        drainFailure;
                }

                // ex — the fixture's own exception — is the cause of this failure; a drain
                // failure on top of it is folded into the message text above only, so it does
                // not compete with ex for the one InnerException slot on the exception that
                // actually explains why the run failed.
                throw new FixtureLifecycleException(message, ex);
            }
        }
    }

    /// <summary>
    /// Drains every cleanup action registered on <paramref name="context"/> so far, in reverse
    /// registration order — the order that undoes a chain of dependent seeding correctly,
    /// last-created-first. One action throwing does not stop the rest: every failure is
    /// collected and aggregated into a single <see cref="FixtureLifecycleException"/>, so a
    /// mid-list failure cannot strand the actions after it, which is exactly the leak this method
    /// exists to prevent. That exception's <see cref="Exception.InnerException"/> is the failing
    /// action's own exception when there was exactly one, or an <see cref="AggregateException"/>
    /// wrapping all of them when there was more than one, so a reader gets each one's real stack
    /// trace rather than only its <see cref="Exception.Message"/> folded into ours. Draining
    /// <em>takes</em> the actions — removing them from <paramref name="context"/> as it reads
    /// them, rather than merely reading them — so draining the same <paramref name="context"/> a
    /// second time — <see cref="RunAsync"/> already drains once after a fixture failure, and
    /// <c>TestHost.CleanupAsync</c> (Task 5) drains again unconditionally during
    /// <c>AssemblyCleanup</c> — finds nothing left and runs nothing, with no separate "already
    /// drained" state for this method to track or get wrong.
    /// </summary>
    public static async Task DrainAsync(FixtureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var actions = context.TakeCleanupActions();
        var causes = new List<Exception>();
        var causeMessages = new List<string>();

        for (var i = actions.Count - 1; i >= 0; i--)
        {
            try
            {
                await actions[i]().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                causes.Add(ex);
                // Read here, inside this action's own catch, rather than later when the
                // aggregate message below is built: a cause whose own Message getter throws
                // must not let that second exception escape this method unwrapped — every
                // failure DrainAsync reports must arrive as FixtureLifecycleException, which is
                // this method's own contract and the one TestHost.CleanupAsync's narrow catch
                // (Task 5) relies on holding.
                causeMessages.Add(SafeMessage(ex));
            }
        }

        if (causes.Count > 0)
        {
            // One cause slot on the exception we throw: the single failure, unwrapped, when
            // there is only one — the common case, and the one where an adopter benefits most
            // from seeing its real stack trace directly. An AggregateException when there is
            // more than one, so none of them is silently preferred over the others.
            Exception cause = causes.Count == 1 ? causes[0] : new AggregateException(causes);

            // "Don't assume a sibling succeeded" is a non-sequitur when there is only one
            // failure — there are no siblings — so the remediation text differs by count rather
            // than always talking about siblings that may not exist.
            var remediation = causes.Count > 1
                ? "Each OnCleanup action must not assume the others succeeded — fix each so it " +
                  "runs correctly even when a sibling action fails."
                : "Fix the teardown so it succeeds, or make it tolerant of the resource already " +
                  "being gone.";

            throw new FixtureLifecycleException(
                $"{causes.Count} of {actions.Count} cleanup action(s) threw while draining: " +
                string.Join(" | ", causeMessages) +
                ". " + remediation,
                cause);
        }
    }

    /// <summary>
    /// <see cref="Exception.Message"/>, tolerating a cause whose own getter throws. Unlikely,
    /// but if it happened while <see cref="DrainAsync"/> was building its aggregate message
    /// rather than here, that second exception would escape unwrapped, breaking this method's
    /// promise to only ever throw <see cref="FixtureLifecycleException"/>.
    /// </summary>
    private static string SafeMessage(Exception exception)
    {
        try
        {
            return exception.Message;
        }
        catch (Exception)
        {
            return $"<{TypeName(exception.GetType())} threw while reading its own Message>";
        }
    }

    /// <summary>
    /// Drains after a fixture failure and reports what went wrong as a string instead of
    /// throwing, so <see cref="RunAsync"/> can fold a drain failure into the fixture's own
    /// failure message rather than let it propagate and replace the original cause.
    /// </summary>
    private static async Task<string?> TryDrainAfterFailureAsync(FixtureContext context)
    {
        try
        {
            await DrainAsync(context).ConfigureAwait(false);
            return null;
        }
        catch (FixtureLifecycleException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// <see cref="Type.FullName"/>, falling back to <see cref="Type.Name"/> — matches
    /// <c>FixtureGraph</c>'s reasoning: a bare <see cref="Type.Name"/> would render two
    /// same-named fixtures in different namespaces as indistinguishable in a skip or failure line.
    /// </summary>
    private static string TypeName(Type type) => type.FullName ?? type.Name;
}
