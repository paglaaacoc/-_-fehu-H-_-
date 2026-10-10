using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NivareQ.Step2Pathfinder.Services;

// S2-B R7 STAGING ONLY. An isolated, host-callable message interpreter; NOT
// registered with MainWindow, Scratchpad, cockpit, any active data provider, or
// restore/backup. Native UI must verify event.Source before calling Handle.
// Construct only with an INACTIVE NotesStoreV1 staging router. The real live
// activation requires S2-D's full backup and global writer/restore gate.
public sealed class NotesWebMessageBridgeV1
{
    public const string Channel = "nivareq.notes.web.v1";
    public const string TrustedPage = "https://notes.pathfinder.invalid/index.html";
    private const int MaxWireBytes = 32 * 1024 * 1024; // one page message, not a library cap
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 48
    };
    private readonly NotesStagingPageRouterV1 _router;
    private readonly object _gate = new();
    private readonly string _documentToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private readonly HashSet<string> _ownedSessions = new(StringComparer.Ordinal);
    private bool _revoked;

    public NotesWebMessageBridgeV1(NotesStagingPageRouterV1 inactiveRouter)
        => _router = inactiveRouter ?? throw new ArgumentNullException(nameof(inactiveRouter));

    // The host sends bootstrap only to the exact isolated Notes document after
    // verified navigation. Never post this token to the S1 or cockpit WebViews.
    public string Bootstrap(string eventSource)
    {
        lock (_gate)
        {
            ValidateSource(eventSource);
            if (_revoked) throw new InvalidOperationException("Notes document lease revoked.");
            return JsonSerializer.Serialize(new
            {
                channel = Channel, type = "notes-bootstrap", documentToken = _documentToken
            });
        }
    }

    // Revoke after a WebView navigation or disposal. Fail closed: existing
    // staging sessions stay held, rather than pretending uncertain page writes
    // and release requests have succeeded. A live shell must prevent close if
    // coordinator still reports pending/uncertain data.
    public bool HasOwnedSessions { get { lock (_gate) return _ownedSessions.Count != 0; } }

    public void Revoke() { lock (_gate) _revoked = true; }

    public string Handle(string eventSource, string messageJson)
    {
        lock (_gate)
        {
            ValidateSource(eventSource);
            if (_revoked) throw new InvalidOperationException("Notes document lease revoked.");
            if (messageJson is null || Encoding.UTF8.GetByteCount(messageJson) > MaxWireBytes)
                throw new InvalidDataException("Notes page message is missing or oversized.");
            using var json = JsonDocument.Parse(messageJson, new JsonDocumentOptions { MaxDepth = 48 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                GetString(root, "channel") != Channel ||
                GetString(root, "documentToken") != _documentToken)
                throw new InvalidDataException("Notes message lacks a valid document lease.");
            var requestId = GetString(root, "requestId");
            if (requestId.Length is < 1 or > 100 || !requestId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                throw new InvalidDataException("Invalid Notes request identifier.");
            var action = GetString(root, "type");
            try
            {
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Missing Notes action payload.");
                object result = action switch
                {
                    "notes-open" => Open(payload),
                    "notes-write" => Write(payload),
                    "notes-probe" => Probe(payload),
                    "notes-release" => Release(payload),
                    _ => throw new InvalidDataException("Unsupported Notes action.")
                };
                return JsonSerializer.Serialize(new
                {
                    channel = Channel, type = "notes-response", documentToken = _documentToken,
                    requestId, action, ok = true, result
                }, JsonOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or
                ArgumentException or KeyNotFoundException or FileNotFoundException or IOException or JsonException)
            {
                // Reject explicitly; never forge a commit ACK or expose native paths.
                var code = ex is IOException ? "IO_ERROR" : "REJECTED";
                return JsonSerializer.Serialize(new
                {
                    channel = Channel, type = "notes-response", documentToken = _documentToken,
                    requestId, action, ok = false, errorCode = code,
                    errorMessage = "Notes operation was not acknowledged; preserve the local draft."
                });
            }
        }
    }

    private NotesPageSession Open(JsonElement data)
    {
        if (_ownedSessions.Count >= 2)
            throw new InvalidOperationException("At most two handover sessions are allowed per document.");
        var noteId = GetString(data, "noteId");
        var pageId = GetString(data, "pageId");
        var session = _router.OpenStagingPage(noteId, pageId);
        _ownedSessions.Add(session.SessionId);
        return session;
    }

    private NotesPageWriteAck Write(JsonElement data)
    {
        var packet = data.Deserialize<NotesPageWritePacket>(JsonOptions)
            ?? throw new InvalidDataException("Missing page write packet.");
        RequireOwnership(packet.SessionId);
        return _router.Write(packet);
    }

    private NotesPageProbe Probe(JsonElement data)
    {
        var packet = data.Deserialize<NotesPageProbeRequest>(JsonOptions)
            ?? throw new InvalidDataException("Missing page probe request.");
        RequireOwnership(packet.SessionId);
        return _router.Probe(packet);
    }

    private object Release(JsonElement data)
    {
        var sessionId = GetString(data, "sessionId");
        RequireOwnership(sessionId);
        // Router's committed-revision guard must succeed BEFORE dropping
        // ownership. A failed/uncertain release retains the session.
        _router.ReleaseStagingPage(sessionId,
            data.GetProperty("lastAcknowledgedSequence").GetInt64(),
            data.GetProperty("lastAcknowledgedRevision").GetInt64());
        _ownedSessions.Remove(sessionId);
        return new { released = true };
    }

    private void RequireOwnership(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_ownedSessions.Contains(sessionId))
            throw new InvalidDataException("Notes session does not belong to this document.");
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Notes message field missing or malformed: " + name);
        return property.GetString() ?? throw new InvalidDataException("Null Notes string.");
    }

    private static void ValidateSource(string source)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(source, TrustedPage))
            throw new InvalidDataException("Notes message rejected from non-Notes WebView source.");
    }
}
