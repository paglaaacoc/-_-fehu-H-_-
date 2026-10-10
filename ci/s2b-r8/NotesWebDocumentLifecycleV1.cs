using System.Text.Json;

namespace NivareQ.Step2Pathfinder.Services;

// S2-B R8: policy boundary for ONE otherwise isolated WebView2 document.
// No MainWindow hookup, no live Notes generation, no S1 provider, no backup pointer.
// WebView2 event handlers are adapted by NotesWebView2LifecycleAdapterV1.
public sealed class NotesWebDocumentLifecycleV1
{
    public enum Phase { New, Loading, Ready, Sealed, Quarantined, Closed }

    private readonly NotesStagingPageRouterV1 _router;
    private NotesWebMessageBridgeV1? _bridge;
    private ulong? _navigationId;
    private string? _leaseToken;
    private bool _armedInitial;
    private bool _armedReload;
    private Phase _phase = Phase.New;
    private string _lastReason = "Not initialized";

    public NotesWebDocumentLifecycleV1(NotesStagingPageRouterV1 inactiveRouter)
        => _router = inactiveRouter ?? throw new ArgumentNullException(nameof(inactiveRouter));

    public Phase State => _phase;
    public string LastReason => _lastReason;
    public bool HasNativeWriter => _bridge?.HasOwnedSessions ?? false;
    public bool CanCloseSafely => (_phase is Phase.New or Phase.Sealed) && !HasNativeWriter;

    // Only an explicit host navigation may load the isolated document. This is
    // never called automatically by a renderer or a WebView2 refresh shortcut.
    public void ArmInitialNavigation()
    {
        if (_phase != Phase.New || _armedInitial)
            throw new InvalidOperationException("Initial Notes document was already armed.");
        _armedInitial = true;
    }

    // The editor emits a *document-bound* drain confirmation only AFTER its
    // coordinator has flushed, received the release ACK and frozen all edits.
    // Native also requires zero owned writer sessions. No mere 'page saved' flag
    // or renderer-claimed ACK is sufficient.
    public void ArmReloadAfterDrain()
    {
        if (_phase != Phase.Sealed || HasNativeWriter)
            throw new InvalidOperationException("Notes reload requires a sealed, released document.");
        _armedReload = true;
    }

    // Return FALSE -> CoreWebView2NavigationStartingEventArgs.Cancel = true.
    public bool NavigationStarting(string uri, ulong navigationId)
    {
        if (!IsTrusted(uri) || _phase is Phase.Closed or Phase.Quarantined)
            return false;
        if (_navigationId.HasValue) return false; // no overlapping navigation
        var allowed = (_phase == Phase.New && _armedInitial) ||
                      (_phase == Phase.Sealed && _armedReload && !HasNativeWriter);
        if (!allowed) return false; // includes unannounced reload and redirects
        _armedInitial = false;
        _armedReload = false;
        _bridge?.Revoke();
        _bridge = null;
        _leaseToken = null;
        _navigationId = navigationId;
        _phase = Phase.Loading;
        _lastReason = "Notes document loading; no messages may be written";
        return true;
    }

    // Call only for main-frame NavigationCompleted. The host passes core.Source;
    // an unrelated canceled navigation must not knock out the running document.
    public string? NavigationCompleted(string currentSource, ulong navigationId, bool succeeded)
    {
        if (_navigationId != navigationId) return null;
        _navigationId = null;
        if (!succeeded || !IsTrusted(currentSource))
        {
            Quarantine("Isolated Notes document failed to load; do not restore unacknowledged drafts.");
            return null;
        }
        _bridge = new NotesWebMessageBridgeV1(_router);
        var bootstrap = _bridge.Bootstrap(currentSource);
        using var json = JsonDocument.Parse(bootstrap);
        _leaseToken = json.RootElement.GetProperty("documentToken").GetString();
        _phase = Phase.Ready;
        _lastReason = "Notes document ready (inactive staging generation only)";
        return bootstrap;
    }

    // Detect any source changes that escaped main-frame starting policy (e.g.,
    // same-document redirects or host error). Never grant a new document lease.
    public void SourceChanged(string currentSource)
    {
        if (_phase is Phase.Ready or Phase.Sealed && !IsTrusted(currentSource))
            Quarantine("Unexpected Notes source change; unacknowledged drafts are unsafe.");
    }

    public string? WebMessageReceived(string source, string currentSource, string json)
    {
        if (_phase != Phase.Ready || _bridge is null || !IsTrusted(source) || !IsTrusted(currentSource))
            return null;
        try
        {
            using var body = JsonDocument.Parse(json);
            var root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() == "notes-drained")
            {
                if (!HasMatchingLease(root) || HasNativeWriter ||
                    !root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("editorFrozen", out var frozen) ||
                    frozen.ValueKind != JsonValueKind.True ||
                    !TryGetId(root, out var requestId)) return null;
                var token = _leaseToken!;
                _bridge.Revoke(); // stop writes even while ACK travels to renderer
                _phase = Phase.Sealed;
                _lastReason = "Coordinator drained; native sessions released; navigation may be armed";
                return JsonSerializer.Serialize(new
                {
                    channel = NotesWebMessageBridgeV1.Channel,
                    type = "notes-response", documentToken = token,
                    requestId, action = "notes-drained", ok = true,
                    result = new { drained = true }
                });
            }
            return _bridge.Handle(source, json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            // Host should ignore invalid messages, NEVER claim an ACK.
            return null;
        }
    }

    // An unexpected renderer, browser, or GPU process failure always revokes the
    // document lease. No background retry, auto-reload or stale ACK is allowed.
    public void ProcessFailed(string failureKind)
        => Quarantine("WebView2 process failure (" + failureKind + "); draft may be uncertain.");

    public bool TryClose()
    {
        if (!CanCloseSafely) return false;
        _bridge?.Revoke();
        _phase = Phase.Closed;
        _lastReason = "Notes document closed after verified drain";
        return true;
    }

    public void Quarantine(string reason)
    {
        _bridge?.Revoke();
        _armedInitial = false;
        _armedReload = false;
        _navigationId = null;
        if (_phase != Phase.Closed) _phase = Phase.Quarantined;
        _lastReason = reason;
        // Deliberately retain native writer session(s): uncertain renderer data
        // MUST NOT be silently discarded or reassigned to another document.
    }

    private bool HasMatchingLease(JsonElement root)
        => _leaseToken is not null &&
           root.TryGetProperty("channel", out var channel) &&
           channel.ValueKind == JsonValueKind.String &&
           channel.GetString() == NotesWebMessageBridgeV1.Channel &&
           root.TryGetProperty("documentToken", out var token) &&
           token.ValueKind == JsonValueKind.String && token.GetString() == _leaseToken;

    private static bool TryGetId(JsonElement root, out string id)
    {
        id = "";
        if (!root.TryGetProperty("requestId", out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        id = property.GetString() ?? "";
        return id.Length is >= 1 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private static bool IsTrusted(string? url)
        => StringComparer.OrdinalIgnoreCase.Equals(url, NotesWebMessageBridgeV1.TrustedPage);
}
