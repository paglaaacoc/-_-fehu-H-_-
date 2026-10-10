using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace NivareQ.Step2Pathfinder.Services;

// S2-B R8: a typed WinUI3 WebView2 event adapter, not a launched/active editor.
// The caller must own a dedicated Notes-only WebView2 with trusted local host
// mapping. DO NOT ATTACH to a cockpit, source preview or S1 Scratchpad WebView.
public sealed class NotesWebView2LifecycleAdapterV1 : IDisposable
{
    public const string StagingAttachmentGuard = "NivareQ-S2B-R8-Isolated-Notes-Only";
    private readonly WebView2 _web;
    private readonly NotesWebDocumentLifecycleV1 _policy;
    private CoreWebView2? _core;
    private bool _attached;
    private bool _disposed;

    public NotesWebView2LifecycleAdapterV1(WebView2 dedicatedNotesView,
        NotesStagingPageRouterV1 inactiveRouter, string isolationGuard)
    {
        if (isolationGuard != StagingAttachmentGuard)
            throw new InvalidOperationException("An isolated staging-only Notes WebView2 is required.");
        _web = dedicatedNotesView ?? throw new ArgumentNullException(nameof(dedicatedNotesView));
        _policy = new NotesWebDocumentLifecycleV1(inactiveRouter);
    }

    public NotesWebDocumentLifecycleV1.Phase State => _policy.State;
    public string LastReason => _policy.LastReason;
    public bool CanCloseSafely => _policy.CanCloseSafely;

    // No Navigate() call here. The future isolated host must set its virtual
    // host-to-local-assets mapping first; call ArmInitialNavigation, THEN
    // Navigate(TrustedPage) on this dedicated WebView2 instance.
    public async Task AttachAsync()
    {
        if (_attached || _disposed) throw new InvalidOperationException("Notes WebView2 adapter already attached/disposed.");
        await _web.EnsureCoreWebView2Async();
        _core = _web.CoreWebView2 ?? throw new InvalidOperationException("Dedicated WebView2 core unavailable.");
        _core.Settings.IsWebMessageEnabled = true;
        _core.NavigationStarting += OnNavigationStarting;
        _core.NavigationCompleted += OnNavigationCompleted;
        _core.SourceChanged += OnSourceChanged;
        _core.ProcessFailed += OnProcessFailed;
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.NewWindowRequested += OnNewWindowRequested;
        _core.FrameNavigationStarting += OnFrameNavigationStarting;
        _attached = true;
    }

    public void ArmInitialNavigation()
    {
        if (!_attached || _disposed) throw new InvalidOperationException("Isolated Notes adapter not attached.");
        _policy.ArmInitialNavigation();
    }

    // Explicitly reject refresh until the renderer has sent notes-drained
    // AFTER native save/release ACK and the policy sealed the lease.
    public void ArmReloadAfterDrain()
    {
        if (!_attached || _disposed) throw new InvalidOperationException("Isolated Notes adapter not attached.");
        _policy.ArmReloadAfterDrain();
    }

    private void OnNavigationStarting(CoreWebView2 _, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!_policy.NavigationStarting(e.Uri, e.NavigationId)) e.Cancel = true;
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var boot = _policy.NavigationCompleted(sender.Source, e.NavigationId, e.IsSuccess);
        if (boot is not null)
        {
            try { sender.PostWebMessageAsJson(boot); }
            catch (Exception ex) { _policy.Quarantine("Bootstrap could not reach Notes renderer: " + ex.GetType().Name); }
        }
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs _)
        => _policy.SourceChanged(sender.Source);

    private void OnProcessFailed(CoreWebView2 _, CoreWebView2ProcessFailedEventArgs e)
        => _policy.ProcessFailed(e.ProcessFailedKind.ToString());

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var response = _policy.WebMessageReceived(e.Source, sender.Source, e.WebMessageAsJson);
        if (response is null) return;
        try { sender.PostWebMessageAsJson(response); }
        catch (Exception ex) { _policy.Quarantine("Notes ACK delivery failed: " + ex.GetType().Name); }
    }

    private static void OnNewWindowRequested(CoreWebView2 _, CoreWebView2NewWindowRequestedEventArgs e)
        => e.Handled = true;

    private static void OnFrameNavigationStarting(CoreWebView2 _, CoreWebView2NavigationStartingEventArgs e)
        => e.Cancel = true;

    // Fail closed. A caller must keep its Notes tab/control open until the
    // document reports a drained lease; never interpret Dispose() as a save.
    public bool TryClose() => !_disposed && _policy.TryClose();

    public void Dispose()
    {
        if (_disposed) return;
        if (_policy.State != NotesWebDocumentLifecycleV1.Phase.Closed)
            _policy.Quarantine("Isolated Notes WebView2 disposed; pending draft must be treated as uncertain.");
        if (_core is not null)
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.SourceChanged -= OnSourceChanged;
            _core.ProcessFailed -= OnProcessFailed;
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.FrameNavigationStarting -= OnFrameNavigationStarting;
        }
        _disposed = true;
    }
}
