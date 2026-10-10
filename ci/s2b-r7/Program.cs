using System.Text.Json;
using NivareQ.Step2Pathfinder.Services;

// Isolated WebView2 wire-contract tests. Not a live WinUI/WebView2 UI test.
static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); }
static void Deny(Action action, string label)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
    { return; }
    throw new Exception("FAIL: unexpectedly permitted " + label);
}
var count=0;
void Ok(bool value,string label) { Check(value,label);count++; }
var root=Path.Combine(Path.GetTempPath(), "nivareq-s2b-r7-" + Guid.NewGuid().ToString("N"));
try
{
    using var store=NotesStoreV1.CreateStaging(root);
    var notebook=store.CreateNotebook("Web bridge test");
    var note=store.CreateNote(notebook,"Wire test note");
    var page=store.ListPages(note)[0].Id;
    var router=new NotesStagingPageRouterV1(store);
    var host=new NotesWebMessageBridgeV1(router);
    const string origin=NotesWebMessageBridgeV1.TrustedPage;
    Deny(()=>host.Bootstrap("https://scratchpad.pathfinder.invalid/index.html"),"S1 origin bootstrap"); count++;
    using var hello=JsonDocument.Parse(host.Bootstrap(origin));
    var token=hello.RootElement.GetProperty("documentToken").GetString()!;
    Ok(token.Length == 48 && hello.RootElement.GetProperty("channel").GetString() == NotesWebMessageBridgeV1.Channel,
       "native document capability minted");
    string Packet(string action, object payload,string requestId="req_1",string? lease=null)
      => JsonSerializer.Serialize(new{channel=NotesWebMessageBridgeV1.Channel,
          documentToken=lease??token,requestId,type=action,payload});
    JsonDocument Send(string action,object payload,string id="req_1")
      => JsonDocument.Parse(host.Handle(origin,Packet(action,payload,id)));
    Deny(()=>host.Handle(origin,Packet("notes-open",new{noteId=note,pageId=page},lease=new string('0',48))),
       "forged lease"); count++;
    Deny(()=>host.Handle("https://evil.invalid/index.html",Packet("notes-open",new{noteId=note,pageId=page})),
       "foreign page origin"); count++;
    using var opened=Send("notes-open",new{noteId=note,pageId=page});
    Ok(opened.RootElement.GetProperty("ok").GetBoolean(),"open staging page acknowledged");
    var openedResult=opened.RootElement.GetProperty("result");
    var session=openedResult.GetProperty("sessionId").GetString()!;
    Ok(openedResult.GetProperty("persistedRevision").GetInt64()==0,
       "open owns original saved revision");
    using(var refused=Send("notes-open",new{noteId=note,pageId=page},"req_conflict"))
        Ok(!refused.RootElement.GetProperty("ok").GetBoolean(),"competing open refused");
    var draft=new{guide="ruled",spacing=32,cells=new[]{new{text="Durable page from isolated WebView2 contract",strokesJson="[]"}},images=Array.Empty<object>()};
    var packet=new{protocol=NotesStagingPageRouterV1.Protocol,sessionId=session,sequence=1,
       noteId=note,pageId=page,expectedRevision=0,page=draft};
    using var written=Send("notes-write",packet,"req_write");
    Ok(written.RootElement.GetProperty("ok").GetBoolean() &&
       written.RootElement.GetProperty("result").GetProperty("persistedRevision").GetInt64()==1,
       "commit ACK revision is native CAS result");
    using(var duplicate=Send("notes-write",packet,"req_retry"))
       Ok(duplicate.RootElement.GetProperty("result").GetProperty("persistedRevision").GetInt64()==1,
          "same-packet retransmit idempotent");
    using(var stale=Send("notes-write",new{protocol=NotesStagingPageRouterV1.Protocol,sessionId=session,
       sequence=2,noteId=note,pageId=page,expectedRevision=0,page=draft},"req_stale"))
       Ok(!stale.RootElement.GetProperty("ok").GetBoolean(),"stale write rejected");
    using(var wrongSession=Send("notes-probe",new{protocol=NotesStagingPageRouterV1.Protocol,
       sessionId=Guid.NewGuid().ToString("N"),noteId=note,pageId=page},"req_notowner"))
       Ok(!wrongSession.RootElement.GetProperty("ok").GetBoolean(),"foreign page session rejected");
    using(var probe=Send("notes-probe",new{protocol=NotesStagingPageRouterV1.Protocol,
       sessionId=session,noteId=note,pageId=page},"req_probe"))
       Ok(probe.RootElement.GetProperty("result").GetProperty("persistedRevision").GetInt64()==1 &&
          probe.RootElement.GetProperty("result").GetProperty("page").GetProperty("cells")[0]
             .GetProperty("text").GetString()=="Durable page from isolated WebView2 contract",
          "read-only probe returns persisted page for ambiguous ACK reconciliation");
    using(var invalidRelease=Send("notes-release",new{sessionId=session,lastAcknowledgedSequence=1,
       lastAcknowledgedRevision=0},"req_releasebad"))
       Ok(!invalidRelease.RootElement.GetProperty("ok").GetBoolean(),"failed release retains session");
    using(var validRelease=Send("notes-release",new{sessionId=session,lastAcknowledgedSequence=1,
       lastAcknowledgedRevision=1},"req_release"))
       Ok(validRelease.RootElement.GetProperty("ok").GetBoolean() &&
          validRelease.RootElement.GetProperty("result").GetProperty("released").GetBoolean(),
          "durably ACKed release granted");
    using(var afterRelease=Send("notes-probe",new{protocol=NotesStagingPageRouterV1.Protocol,
       sessionId=session,noteId=note,pageId=page},"req_closed"))
       Ok(!afterRelease.RootElement.GetProperty("ok").GetBoolean(),"released page session cannot be reused");
    host.Revoke();
    Deny(()=>host.Handle(origin,Packet("notes-open",new{noteId=note,pageId=page})),"revoked document"); count++;
    Ok(store.ReadPage(note,page).Cells[0].Text=="Durable page from isolated WebView2 contract",
       "document revocation cannot rewrite persisted note");
    Console.WriteLine($"PASS S2-B R7 native WebView2 wire contract: {count} checks");
}
finally
{
    if(Directory.Exists(root)) Directory.Delete(root,true);
}
