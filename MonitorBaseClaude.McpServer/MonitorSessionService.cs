using System.Text.Json;

namespace MonitorBaseClaude.McpServer;

public sealed class MonitorSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly MonitorServerSettings settings;

    public MonitorSessionService(MonitorServerSettings settings)
    {
        this.settings = settings;
    }

    public MonitorSessionState StartSession(string purpose = "monitor workflow")
    {
        string sessionId = $"monitor-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40];
        return CreateSessionState(sessionId, purpose);
    }

    public MonitorSessionState EnsureSession(string sessionId, string purpose)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        string path = GetSessionPath(sessionId);
        if (File.Exists(path))
        {
            MonitorSessionState existing = JsonSerializer.Deserialize<MonitorSessionState>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidOperationException($"Monitor session file could not be read: {path}");
            MonitorSessionState touched = existing with { LastAccessedAt = DateTimeOffset.UtcNow };
            Save(touched);
            return touched;
        }

        return CreateSessionState(sessionId, purpose);
    }

    private MonitorSessionState CreateSessionState(string sessionId, string purpose)
    {
        MonitorSessionState state = new(
            sessionId,
            purpose,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            settings.WatchedSolutionPath,
            Path.GetDirectoryName(settings.WatchedSolutionPath) ?? string.Empty,
            [
                new MonitorSessionEvent(
                    DateTimeOffset.UtcNow,
                    "session-started",
                    "Monitor session handle created.",
                    null)
            ],
            []);
        Save(state);
        return state;
    }

    public IReadOnlyList<MonitorSessionSummary> ListSessions()
    {
        string sessionRoot = GetSessionRoot();
        if (!Directory.Exists(sessionRoot))
        {
            return [];
        }

        return Directory
            .GetFiles(sessionRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(TryLoad)
            .Where(state => state is not null)
            .Cast<MonitorSessionState>()
            .OrderByDescending(state => state.LastAccessedAt)
            .Select(state => new MonitorSessionSummary(
                state.SessionId,
                state.Purpose,
                state.CreatedAt,
                state.LastAccessedAt,
                state.WatchedSolutionPath,
                state.Events.Count,
                state.Files?.Count ?? 0))
            .ToArray();
    }

    public MonitorSessionState GetSession(string sessionId)
    {
        MonitorSessionState state = LoadRequired(sessionId);
        state = state with { LastAccessedAt = DateTimeOffset.UtcNow };
        Save(state);
        return state;
    }

    public MonitorSessionState RecordEvent(string sessionId, string eventType, string summary, string? payloadJson = null)
    {
        MonitorSessionState state = LoadRequired(sessionId);
        List<MonitorSessionEvent> events = [.. state.Events];
        events.Add(new MonitorSessionEvent(DateTimeOffset.UtcNow, eventType, summary, payloadJson));
        MonitorSessionState updated = state with
        {
            LastAccessedAt = DateTimeOffset.UtcNow,
            Events = events
        };
        Save(updated);
        return updated;
    }

    public MonitorSessionFileCheckResult CheckFileHash(string sessionId, MonitorFileHashInfo current)
    {
        MonitorSessionState state = LoadRequired(sessionId);
        MonitorSessionFileState? existing = FindFileState(state, current.RelativeSourcePath);
        MonitorSessionState touched = state with { LastAccessedAt = DateTimeOffset.UtcNow };
        Save(touched);

        return new MonitorSessionFileCheckResult(
            sessionId,
            current.SourceFilePath,
            current.RelativeSourcePath,
            existing is not null,
            existing is not null && !existing.Sha256.Equals(current.Sha256, StringComparison.OrdinalIgnoreCase),
            current.Sha256,
            existing?.Sha256,
            current.Length,
            existing?.Length,
            current.LastWriteUtc,
            existing?.LastWriteUtc,
            existing?.LastFetchedAt,
            existing?.FetchCount ?? 0);
    }

    public MonitorSessionState RecordFileFetch(string sessionId, MonitorFileHashInfo current, string accessKind)
    {
        MonitorSessionState state = LoadRequired(sessionId);
        List<MonitorSessionFileState> files = [.. state.Files ?? []];
        int index = files.FindIndex(file => file.RelativeSourcePath.Equals(current.RelativeSourcePath, StringComparison.OrdinalIgnoreCase));
        MonitorSessionFileState updatedFile = new(
            current.SourceFilePath,
            current.RelativeSourcePath,
            current.Sha256,
            current.Length,
            current.LastWriteUtc,
            DateTimeOffset.UtcNow,
            accessKind,
            index >= 0 ? files[index].FetchCount + 1 : 1);

        if (index >= 0)
        {
            files[index] = updatedFile;
        }
        else
        {
            files.Add(updatedFile);
        }

        List<MonitorSessionEvent> events = [.. state.Events];
        events.Add(new MonitorSessionEvent(
            DateTimeOffset.UtcNow,
            "file-fetched",
            $"{current.RelativeSourcePath} fetched through {accessKind}.",
            JsonSerializer.Serialize(updatedFile, JsonOptions)));

        MonitorSessionState updated = state with
        {
            LastAccessedAt = DateTimeOffset.UtcNow,
            Events = events,
            Files = files.OrderBy(file => file.RelativeSourcePath, StringComparer.OrdinalIgnoreCase).ToArray()
        };
        Save(updated);
        return updated;
    }

    private MonitorSessionState LoadRequired(string sessionId)
    {
        string path = GetSessionPath(sessionId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Monitor session was not found.", path);
        }

        return JsonSerializer.Deserialize<MonitorSessionState>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Monitor session file could not be read: {path}");
    }

    private MonitorSessionState? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MonitorSessionState>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save(MonitorSessionState state)
    {
        Directory.CreateDirectory(GetSessionRoot());
        File.WriteAllText(GetSessionPath(state.SessionId), JsonSerializer.Serialize(state, JsonOptions));
    }

    private static MonitorSessionFileState? FindFileState(MonitorSessionState state, string relativeSourcePath)
    {
        return state.Files?.FirstOrDefault(file => file.RelativeSourcePath.Equals(relativeSourcePath, StringComparison.OrdinalIgnoreCase));
    }

    private string GetSessionRoot()
    {
        return Path.Combine(settings.UiRoot, "Working", "Sessions");
    }

    private string GetSessionPath(string sessionId)
    {
        string safeSessionId = string.Concat(sessionId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        return Path.Combine(GetSessionRoot(), safeSessionId + ".json");
    }
}

public sealed record MonitorSessionState(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessedAt,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    IReadOnlyList<MonitorSessionEvent> Events,
    IReadOnlyList<MonitorSessionFileState>? Files = null);

public sealed record MonitorSessionSummary(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessedAt,
    string WatchedSolutionPath,
    int EventCount,
    int FileCount);

public sealed record MonitorSessionEvent(
    DateTimeOffset Timestamp,
    string EventType,
    string Summary,
    string? PayloadJson);

public sealed record MonitorSessionFileState(
    string SourceFilePath,
    string RelativeSourcePath,
    string Sha256,
    long Length,
    DateTime LastWriteUtc,
    DateTimeOffset LastFetchedAt,
    string AccessKind,
    int FetchCount);

public sealed record MonitorSessionFileCheckResult(
    string SessionId,
    string SourceFilePath,
    string RelativeSourcePath,
    bool KnownInSession,
    bool ChangedSinceLastFetch,
    string CurrentSha256,
    string? SessionSha256,
    long CurrentLength,
    long? SessionLength,
    DateTime CurrentLastWriteUtc,
    DateTime? SessionLastWriteUtc,
    DateTimeOffset? LastFetchedAt,
    int FetchCount);
