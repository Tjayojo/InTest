namespace InTest.Cli.Spec;

/// <summary>
/// Thrown by <see cref="ClientLockfile"/> for every way a client generator's own lockfile can fail
/// to yield what <c>init --client-lockfile</c> asked it to recover: the file is missing or
/// unreadable, its JSON does not parse, or it is JSON but not a lockfile — a required field
/// (<c>descriptionLocation</c>) is absent, renamed, blank, or the wrong type. Mirrors
/// <see cref="SpecLoadException"/>, <see cref="Configuration.ConfigLoadException"/> and
/// <see cref="Clients.ClientCallMapFormatException"/> deliberately: one exception type per
/// adopter-facing file format, carrying a message written for the adopter rather than a raw
/// framework exception that never names the file at all. <c>InitCommand</c> catches this the same
/// way <c>GenerateCommand</c> already catches the other three — print <c>ex.Message</c> bare,
/// exit 2 — rather than letting it fall through to <c>Program</c>'s crash-floor phrasing.
/// </summary>
public sealed class ClientLockfileException(string message, Exception? inner = null) : Exception(message, inner);
