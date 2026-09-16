namespace CozyTranslator.Core;

public interface IQueryProvider
{
    IAsyncEnumerable<QueryUpdate> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default);
}
public enum QueryStatus { Idle, Loading, Streaming, Success, Cancelled, Error }
public sealed record QueryState(QueryStatus Status = QueryStatus.Idle, string Text = "", DictionaryEntry? Entry = null, string Error = "");
public sealed class QuerySession(IQueryProvider provider) : IDisposable
{
    public QueryState State { get; private set; } = new();
    public event Action<QueryState>? Changed;
    private CancellationTokenSource? active;
    private long generation;
    private void Set(QueryState state) { State=state; Changed?.Invoke(state); }
    public async Task StartAsync(QueryRequest request)
    {
        var current=++generation;
        active?.Cancel();
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(120));active=cts;
        Set(new(QueryStatus.Loading));
        try
        {
            await foreach(var item in provider.QueryAsync(request,cts.Token))
            {
                if(current!=generation) return;
                Set(State with {Text=State.Text+(item.Delta??""),Entry=item.Entry??State.Entry,Status=item.Completed?QueryStatus.Success:QueryStatus.Streaming});
            }
            if(current==generation && State.Status!=QueryStatus.Success) Set(State with {Status=QueryStatus.Error,Error="响应提前结束，请重试。"});
        }
        catch(OperationCanceledException) {if(current==generation)Set(State with {Status=QueryStatus.Error,Error="请求超时，请重试。"});}
        catch(QueryException ex) {if(current==generation)Set(State with {Status=QueryStatus.Error,Error=ex.Message});}
        catch(HttpRequestException) {if(current==generation)Set(State with {Status=QueryStatus.Error,Error="无法连接服务，请检查网络、系统代理与接口地址。"});}
        catch(Exception) {if(current==generation)Set(State with {Status=QueryStatus.Error,Error="处理响应失败，请检查接口配置后重试。"});}
        finally {if(ReferenceEquals(active,cts))active=null;}
    }
    public void Stop()
    {
        ++generation;active?.Cancel();active=null;
        if(State.Status is QueryStatus.Loading or QueryStatus.Streaming) Set(State with {Status=QueryStatus.Cancelled,Error="已停止 · 内容尚未完成"});
    }
    public void Reset() {Stop();Set(new());}
    public void Dispose() {++generation;active?.Cancel();active=null;}
}
