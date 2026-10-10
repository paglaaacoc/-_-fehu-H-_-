using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NivareQ.Step2Pathfinder.Services;

// Real Chromium/WebView2 events; staging-only, never a MainWindow attachment.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SmokeHost());
        if (SmokeHost.ResultCode != 0) Environment.ExitCode = SmokeHost.ResultCode;
    }
}

internal sealed class SmokeHost : Form
{
    public static int ResultCode { get; private set; } = 1;
    private const string Trusted = NotesWebMessageBridgeV1.TrustedPage;
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Pathfinder-R9-webview-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, TaskCompletionSource<JsonElement>> _responses = new();
    private TaskCompletionSource<string> _boot = NewSignal<string>();
    private TaskCompletionSource<bool>? _cancelSignal;
    private TaskCompletionSource<string> _actualRendererCrash = NewSignal<string>();
    private int _realCrashEvents;
    private string? _lastCrashKind;
    private NotesStoreV1? _store;
    private NotesWebDocumentLifecycleV1? _policy;
    private CoreWebView2? _core;
    private int _checks, _denied, _completed, _startEvents, _messages;
    private string _note = "", _page = "";

    private static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Assert(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException("SMOKE ASSERTION FAILED: " + label);
        _checks++;
        Console.WriteLine("PASS R9 REAL EVENT: " + label);
    }

    public SmokeHost()
    {
        Text = "Pathfinder R10 disposable REAL renderer crash (NOT PRODUCT)";
        Width = 620; Height = 420;
        Controls.Add(_view);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Left = -1600; Top = -1600;
        Shown += (_, _) => _ = RunAsync();
    }

    private async Task RunAsync()
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(95));
        using var registration = watchdog.Token.Register(() =>
        {
            if (IsHandleCreated && !IsDisposed)
                try { BeginInvoke((Action)(() => Fail(new TimeoutException("Real WebView2 smoke timed out.")))); }
                catch (InvalidOperationException) { }
        });
        try
        {
            Directory.CreateDirectory(_root);
            var staticDir = Path.Combine(_root, "site");
            Directory.CreateDirectory(staticDir);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "index.html"),
                Path.Combine(staticDir, "index.html"));
            _store = NotesStoreV1.CreateStaging(Path.Combine(_root, "notes"));
            var notebook = _store.CreateNotebook("R9 Chromium isolation smoke");
            _note = _store.CreateNote(notebook, "Not live Pathfinder content");
            _page = _store.ListPages(_note)[0].Id;
            _policy = new NotesWebDocumentLifecycleV1(new NotesStagingPageRouterV1(_store));

            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            Assert(!string.IsNullOrWhiteSpace(version), "actual Edge WebView2 Runtime " + version);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_root, "webview-userdata"));
            await _view.EnsureCoreWebView2Async(env);
            _core = _view.CoreWebView2 ?? throw new InvalidOperationException("Chromium WebView2 core unavailable");
            _core.SetVirtualHostNameToFolderMapping("notes.pathfinder.invalid", staticDir,
                CoreWebView2HostResourceAccessKind.DenyCors);
            _core.Settings.IsWebMessageEnabled = true;
            _core.NavigationStarting += OnStarting;
            _core.NavigationCompleted += OnCompleted;
            _core.SourceChanged += (_,_) => _policy!.SourceChanged(_core!.Source);
            _core.ProcessFailed += OnActualProcessFailed;
            _core.WebMessageReceived += OnMessage;
            _core.NewWindowRequested += (_, e) => e.Handled = true;
            Assert(_policy.State == NotesWebDocumentLifecycleV1.Phase.New, "isolated document unloaded");

            _policy.ArmInitialNavigation();
            _core.Navigate(Trusted);
            var firstLease = await _boot.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(_policy.State == NotesWebDocumentLifecycleV1.Phase.Ready && firstLease.Length == 48 && _startEvents > 0,
                "real WebView2 navigation and bootstrap completed");
            _cancelSignal = NewSignal<bool>();
            await _core.ExecuteScriptAsync("location.reload()");
            await _cancelSignal.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert(_denied >= 1 && _policy.State == NotesWebDocumentLifecycleV1.Phase.Ready,
                "actual Chromium unarmed refresh canceled");

            var opened = await SendAsync("notes-open", new{noteId=_note,pageId=_page}, "open1");
            Assert(opened.GetProperty("ok").GetBoolean(), "real JS native-open ACK");
            var session = opened.GetProperty("result").GetProperty("sessionId").GetString()!;
            Assert(_policy.HasNativeWriter, "real JS acquired native page writer");
            var draft = new {guide="ruled",spacing=32,cells=new[]{new{text="R9 actual Chromium durable write",strokesJson="[]"}},images=Array.Empty<object>()};
            var saved = await SendAsync("notes-write",new {
                protocol=NotesStagingPageRouterV1.Protocol,sessionId=session,sequence=1,
                noteId=_note,pageId=_page,expectedRevision=0,page=draft
            },"write1");
            Assert(saved.GetProperty("ok").GetBoolean() &&
                saved.GetProperty("result").GetProperty("persistedRevision").GetInt64()==1,
                "WebView2 write ACK reflects committed SQLite revision");
            var rejected = await SendAsync("notes-release",new {
                sessionId=session,lastAcknowledgedSequence=1,lastAcknowledgedRevision=0
            },"releasebad");
            Assert(!rejected.GetProperty("ok").GetBoolean() && _policy.HasNativeWriter,
                "wrong revision release rejected");
            var released=await SendAsync("notes-release",new {
                sessionId=session,lastAcknowledgedSequence=1,lastAcknowledgedRevision=1
            },"release1");
            Assert(released.GetProperty("ok").GetBoolean() && !_policy.HasNativeWriter,
                "native writer released only after ACK");
            var drained=await SendAsync("notes-drained",new{editorFrozen=true},"drain1");
            Assert(drained.GetProperty("ok").GetBoolean() && _policy.State==NotesWebDocumentLifecycleV1.Phase.Sealed,
                "real renderer/native drain ACK seals document");
            _policy.ArmReloadAfterDrain();
            _boot = NewSignal<string>();
            _core.Navigate(Trusted);
            var secondLease=await _boot.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert(firstLease!=secondLease && _policy.State==NotesWebDocumentLifecycleV1.Phase.Ready,
                "authorized Chromium reload rotates document token");
            var reopened=await SendAsync("notes-open",new{noteId=_note,pageId=_page},"open2");
            Assert(reopened.GetProperty("ok").GetBoolean() &&
                reopened.GetProperty("result").GetProperty("persistedRevision").GetInt64()==1 &&
                reopened.GetProperty("result").GetProperty("page").GetProperty("cells")[0]
                   .GetProperty("text").GetString()=="R9 actual Chromium durable write",
                "reloaded Chromium document reads actual durable page");

            // R10: a truly crashed, ephemeral WebView2 renderer, NOT a simulated policy callback.
            // The reopened page still owns a native writer; auto-recovery MUST fail closed.
            Assert(_policy.HasNativeWriter && _policy.State==NotesWebDocumentLifecycleV1.Phase.Ready,
                "real Chromium document owns native page before renderer crash");
            var durableText=_store.ReadPage(_note,_page).Cells[0].Text;
            Assert(durableText=="R9 actual Chromium durable write",
                "confirmed SQLite content exists before forced renderer crash");
            Console.WriteLine("R10 CRASH INJECTION: invoking Chromium Page.crash in isolated user-data folder");
            // Page.crash terminates the renderer. The command's Task normally faults when
            // its target disappears; a fault is NOT evidence of ProcessFailed by itself.
            var crashCommand=_core.CallDevToolsProtocolMethodAsync("Page.crash","{}");
            _=crashCommand.ContinueWith(t =>
            {
                if(t.IsFaulted) Console.WriteLine("R10 CDP transport interrupted after crash: "+t.Exception?.GetBaseException().GetType().Name);
                else if(t.IsCompletedSuccessfully) Console.WriteLine("R10 CDP command returned: "+t.Result);
            },TaskScheduler.Default);
            var actualKind=await _actualRendererCrash.Task.WaitAsync(TimeSpan.FromSeconds(22));
            Assert(actualKind==CoreWebView2ProcessFailedKind.RenderProcessExited.ToString() &&
                   _realCrashEvents>=1,
                "ACTUAL WebView2 ProcessFailed RenderProcessExited received");
            Assert(_policy.State==NotesWebDocumentLifecycleV1.Phase.Quarantined,
                "real renderer crash immediately quarantines Notes document");
            Assert(_policy.HasNativeWriter && !_policy.TryClose(),
                "renderer crash retains native owner; unsafe close refused");
            Assert(!_policy.NavigationStarting(Trusted,811234),
                "renderer-crashed document cannot be silently navigated or reopened");
            Assert(_policy.WebMessageReceived(Trusted,Trusted,
               JsonSerializer.Serialize(new{channel=NotesWebMessageBridgeV1.Channel,
                   documentToken=secondLease,type="notes-release",requestId="stale_after_crash",
                   payload=new {sessionId=reopened.GetProperty("result").GetProperty("sessionId").GetString(),
                     lastAcknowledgedSequence=0,lastAcknowledgedRevision=1}})) is null,
                "late browser message after real crash receives no release ACK");
            Assert(_store.ReadPage(_note,_page).Cells[0].Text==durableText,
                "real crash preserves previously committed SQLite page");
            Assert(_messages>=9 && _completed>=2,
                "real Chromium test observed browser messages and document navigation");
            Console.WriteLine($"PASS R10 ACTUAL RENDERER CRASH: {_checks} checks; crashEvents={_realCrashEvents}; lastFailure={_lastCrashKind}; started={_startEvents} completed={_completed} denied={_denied} webMessages={_messages}");
            ResultCode=0;
        }
        catch(Exception ex) { Fail(ex); }
        finally
        {
            watchdog.Cancel();
            try { _store?.Dispose(); } catch(Exception e){Console.WriteLine("CLEANUP: "+e.Message);}
            Close();
        }
    }
    private void Fail(Exception error)
    {
        if (IsDisposed) return;
        Console.Error.WriteLine("FAIL R9 ACTUAL WEBVIEW2: " + error);
        ResultCode=1;
        try { Close(); } catch { }
    }
    private void OnActualProcessFailed(object? sender,CoreWebView2ProcessFailedEventArgs e)
    {
        _realCrashEvents++;
        _lastCrashKind=e.ProcessFailedKind.ToString();
        Console.WriteLine("R10 ACTUAL ProcessFailed: kind="+_lastCrashKind+" reason="+e.Reason);
        _policy!.ProcessFailed(_lastCrashKind);
        if(e.ProcessFailedKind==CoreWebView2ProcessFailedKind.RenderProcessExited)
            _actualRendererCrash.TrySetResult(_lastCrashKind);
    }

    private void OnStarting(object? sender,CoreWebView2NavigationStartingEventArgs e)
    {
        _startEvents++;
        if(_policy!.NavigationStarting(e.Uri,e.NavigationId))return;
        e.Cancel=true; _denied++; _cancelSignal?.TrySetResult(true);
    }
    private void OnCompleted(object? sender,CoreWebView2NavigationCompletedEventArgs e)
    {
        _completed++;
        var bootstrap=_policy!.NavigationCompleted(_core!.Source,e.NavigationId,e.IsSuccess);
        if(bootstrap!=null)_core!.PostWebMessageAsJson(bootstrap);
    }
    private void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
    {
        _messages++;
        using var msg=JsonDocument.Parse(e.WebMessageAsJson);
        var value=msg.RootElement;
        if(value.TryGetProperty("type",out var type))
        {
            var tag=type.GetString();
            if(tag=="smoke-bootstrap-observed" && e.Source==Trusted)
            {
                _boot.TrySetResult(value.GetProperty("documentToken").GetString()!);
                return;
            }
            if(tag=="smoke-native-response-observed" && e.Source==Trusted)
            {
                var id=value.GetProperty("requestId").GetString()!;
                if(_responses.Remove(id,out var waiter))waiter.TrySetResult(value.GetProperty("response").Clone());
                return;
            }
        }
        var reply=_policy!.WebMessageReceived(e.Source,_core!.Source,e.WebMessageAsJson);
        if(reply!=null)_core!.PostWebMessageAsJson(reply);
    }
    private async Task<JsonElement> SendAsync(string action,object payload,string id)
    {
        var reply=NewSignal<JsonElement>();
        _responses.Add(id,reply);
        try
        {
            var script="window.smokeSend("+JsonSerializer.Serialize(action)+","+
                JsonSerializer.Serialize(payload)+","+JsonSerializer.Serialize(id)+")";
            var executed=await _core!.ExecuteScriptAsync(script);
            if(executed!="true")throw new InvalidOperationException("JS did not submit smoke action: "+executed);
            return await reply.Task.WaitAsync(TimeSpan.FromSeconds(12));
        }
        finally { _responses.Remove(id); }
    }
}
