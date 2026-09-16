using System.Net;
using System.Text;
using System.Text.Json;
using CozyTranslator.Core;

var tests = new List<(string, Func<Task>)>();
void Test(string name, Action run) => tests.Add((name, () => { run(); return Task.CompletedTask; }));
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void True(bool value, string message) { if (!value) throw new Exception(message); }
void Throws(Action run) { try { run(); } catch (QueryException) { return; } throw new Exception("Expected QueryException"); }
QueryRequest Query(ProviderKind kind = ProviderKind.OpenAI, string url = "https://api.deepseek.com", QueryMode mode = QueryMode.Translation) =>
    new("The result is robust.", mode, TranslationDirection.EnglishToChinese, 2, QueryPlanner.DefaultPrompt, new(kind, url, "test-secret", "model-one"));

foreach (var word in new[] { "robust", "  ‘robust.’  ", "state-of-the-art", "don't", "DON’T", "AI" })
    Test($"Auto recognizes word: {word}", () => Equal(QueryMode.Dictionary, QueryPlanner.Resolve(word, QueryMode.Auto)));
foreach (var text in new[] { "hello world", "你好", "研究结果是稳定的", "Hello, world!", "version2", "U.S.", "two\nwords" })
    Test($"Auto translates phrase: {text}", () => Equal(QueryMode.Translation, QueryPlanner.Resolve(text, QueryMode.Auto)));
Test("Explicit translation overrides word detection", () => Equal(QueryMode.Translation, QueryPlanner.Resolve("hello", QueryMode.Translation)));
Test("Explicit dictionary overrides phrase detection", () => Equal(QueryMode.Dictionary, QueryPlanner.Resolve("take off", QueryMode.Dictionary)));
Test("Boundary punctuation is removed", () => Equal("robust", QueryPlanner.NormalizeWord("  ‘robust.’  ")));
Test("Prompt carries direction and tone", () => { var p = QueryPlanner.BuildSystem(Query() with { Style = 0 }); True(p.Contains("学术") && p.Contains("中文"), "Missing tone/direction"); });
Test("Translation preserves Markdown and LaTeX without wrapping the output", () => { var p = QueryPlanner.BuildSystem(Query() with { SystemPrompt = "custom" }); True(p.Contains("Markdown") && p.Contains("LaTeX") && p.Contains("代码围栏") && p.StartsWith("custom"), "Missing research format contract"); });
Test("Five styles have distinct instructions", () => Equal(5, Enumerable.Range(0,5).Select(n=>QueryPlanner.BuildSystem(Query() with {Style=n})).Distinct().Count()));
Test("Dictionary prompt enforces JSON independently of translation prompt", () => { var p=QueryPlanner.BuildSystem(Query(mode:QueryMode.Dictionary) with {SystemPrompt="BAD_PROMPT"}); True(p.Contains("phonetics") && !p.Contains("BAD_PROMPT"), "Wrong dictionary contract"); });
Test("Dictionary parses multiple meanings", () => { var e=DictionaryParser.Parse("""{"word":"robust","found":true,"phonetics":[{"accent":"US","ipa":"roʊˈbʌst"}],"meanings":[{"partOfSpeech":"adj.","definitions":["稳健的","强健的"]}]}"""); Equal(2,e.Meanings[0].Definitions.Length); });
Test("Unknown dictionary word is a valid empty state", () => True(!DictionaryParser.Parse("""{"word":"xyzxyz","found":false,"phonetics":[],"meanings":[]}""").Found,"Wrong found state"));
Test("Incomplete dictionary is rejected", () => Throws(()=>DictionaryParser.Parse("""{"word":"robust","found":true}""")));
Test("Malformed dictionary is rejected", () => Throws(()=>DictionaryParser.Parse("not json")));

foreach(var suffix in new[]{"", "/", "/v1", "/v1/"})
Test("OpenAI base path preserved: "+suffix, () => { using var r=ProtocolRequest.Create(Query(url:"https://example.com"+suffix), true); Equal("https://example.com"+suffix.TrimEnd('/')+"/chat/completions",r.RequestUri!.ToString()); Equal("test-secret",r.Headers.Authorization!.Parameter); });
Test("Claude uses native headers/system", () => { using var r=ProtocolRequest.Create(Query(ProviderKind.Claude,"https://api.anthropic.com/v1"),true); Equal("https://api.anthropic.com/v1/messages",r.RequestUri!.ToString()); True(r.Headers.Contains("x-api-key") && r.Headers.Contains("anthropic-version"),"Missing auth"); using var d=JsonDocument.Parse(r.Content!.ReadAsStringAsync().Result); True(d.RootElement.TryGetProperty("system",out _) && d.RootElement.GetProperty("max_tokens").GetInt32()>0,"Missing Claude fields"); });
Test("Gemini uses native streaming endpoint and header", () => { using var r=ProtocolRequest.Create(Query(ProviderKind.Gemini,"https://generativelanguage.googleapis.com/v1beta"),true); Equal("https://generativelanguage.googleapis.com/v1beta/models/model-one:streamGenerateContent?alt=sse",r.RequestUri!.ToString()); True(r.Headers.Contains("x-goog-api-key"),"Missing key header"); True(!r.RequestUri.ToString().Contains("test-secret"),"Secret in URL"); });
Test("Dictionary uses nonstream Gemini generation", () => { using var r=ProtocolRequest.Create(Query(ProviderKind.Gemini,"https://generativelanguage.googleapis.com/v1beta",QueryMode.Dictionary),false); True(r.RequestUri!.ToString().EndsWith(":generateContent"),"Wrong dictionary endpoint"); });
Test("Empty key rejected before network", () => Throws(()=>ProtocolRequest.Create(Query() with {Provider=new(ApiKey:"")},true)));
Test("Empty source rejected before network", () => Throws(()=>ProtocolRequest.Create(Query() with {Text="  "},true)));
Test("Non HTTPS endpoint rejected", () => Throws(()=>ProtocolRequest.Create(Query(url:"http://example.com"),true)));

async Task<string> StreamResult(ProviderKind kind, string body)
{
    using var http=new HttpClient(new FixtureHandler(body)); var provider=new ProviderClient(http);var output="";bool done=false;
    await foreach(var u in provider.QueryAsync(Query(kind))) { output+=u.Delta;done|=u.Completed; }
    True(done,"Missing completed event");return output;
}
tests.Add(("OpenAI handles fragmented UTF8 SSE and ignores reasoning",async()=>Equal("你好",await StreamResult(ProviderKind.OpenAI,"data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"hidden\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"你\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"好\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"))));
tests.Add(("Claude native event stream yields only text",async()=>Equal("你好",await StreamResult(ProviderKind.Claude,"event: ping\ndata: {\"type\":\"ping\"}\n\nevent: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"你好\"}}\n\nevent: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\nevent: message_stop\ndata: {\"type\":\"message_stop\"}\n\n"))));
tests.Add(("Gemini omits thought parts",async()=>Equal("你好",await StreamResult(ProviderKind.Gemini,"data: {\"candidates\":[{\"content\":{\"parts\":[{\"thought\":true,\"text\":\"secret thought\"},{\"text\":\"你好\"}]},\"finishReason\":\"STOP\"}]}\n\n"))));
tests.Add(("Abrupt SSE end is an error",async()=>{try {await StreamResult(ProviderKind.OpenAI,"data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");}catch(QueryException){return;}throw new Exception("Truncated stream accepted");}));
tests.Add(("Authentication error is clear and secret-free",async()=>{using var http=new HttpClient(new FixtureHandler("test-secret",HttpStatusCode.Unauthorized));try {await foreach(var _ in new ProviderClient(http).QueryAsync(Query())) {}} catch(QueryException ex){True(ex.Message.Contains("Key")&&!ex.Message.Contains("test-secret"),"Unsafe error");return;}throw new Exception("401 accepted");}));
tests.Add(("Nonstream dictionary passes through real provider parser",async()=>{
    var entry="""{"word":"robust","found":true,"phonetics":[],"meanings":[{"partOfSpeech":"adj.","definitions":["稳健的"]}]}""";
    var body=JsonSerializer.Serialize(new {choices=new[]{new {message=new {content=entry},finish_reason="stop"}}});
    using var http=new HttpClient(new FixtureHandler(body));DictionaryEntry? actual=null;
    await foreach(var u in new ProviderClient(http).QueryAsync(Query(mode:QueryMode.Dictionary)))actual=u.Entry??actual;
    Equal("robust",actual?.Word);
}));
tests.Add(("New query wins when cancelled provider responds late",async()=>{
    var p=new ControlledProvider(); using var s=new QuerySession(p);
    var old=s.StartAsync(Query() with {Text="old"});var current=s.StartAsync(Query() with {Text="new"});
    p.New.TrySetResult();await current;p.Old.TrySetResult();await old;
    Equal("new",s.State.Text);Equal(QueryStatus.Success,s.State.Status);
}));
tests.Add(("Stop preserves current partial text",async()=>{
    var p=new ControlledProvider();using var s=new QuerySession(p);
    var pending=s.StartAsync(Query() with {Text="old"});s.Stop();p.Old.TrySetResult();await pending;
    Equal(QueryStatus.Cancelled,s.State.Status);True(s.State.Text=="partial","Lost partial result");
}));

var failed=0;
foreach(var (name,run) in tests) { try {await run();System.Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;System.Console.WriteLine("FAIL "+name+": "+ex.Message);} }
System.Console.WriteLine($"\n{tests.Count-failed}/{tests.Count} passed");
return failed==0?0:1;

sealed class FixtureHandler(string body, HttpStatusCode status=HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status) {Content=new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(body)))});
}
sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken=default) => base.ReadAsync(buffer[..Math.Min(3,buffer.Length)],cancellationToken);
}
sealed class ControlledProvider : CozyTranslator.Core.IQueryProvider
{
    public TaskCompletionSource Old {get;}=new();public TaskCompletionSource New {get;}=new();
    public async IAsyncEnumerable<QueryUpdate> QueryAsync(QueryRequest request,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken cancellationToken=default)
    {
        if(request.Text=="old") {yield return new(Delta:"partial");await Old.Task;} else await New.Task;
        yield return new(Delta:request.Text);yield return new(Completed:true);
    }
}
