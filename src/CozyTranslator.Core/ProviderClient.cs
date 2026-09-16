using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace CozyTranslator.Core;

public sealed class ProviderClient(HttpClient client) : IQueryProvider
{
    public async IAsyncEnumerable<QueryUpdate> QueryAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var dictionary=QueryPlanner.Resolve(request.Text,request.Mode)==QueryMode.Dictionary;
        using var message=ProtocolRequest.Create(request,!dictionary);
        using var response=await client.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(!response.IsSuccessStatusCode) throw new QueryException((int)response.StatusCode switch {
            401=>"API Key 无效或已过期，请在设置中检查。",403=>"该 API Key 没有访问权限。",402=>"账户余额不足，请检查服务商账户。",
            429=>"请求受限或配额不足，请稍后手动重试。",404=>"接口或模型不存在，请检查基础地址和模型名称。",
            >=500=>"模型服务暂时不可用，请稍后重试。",_=>"接口拒绝了请求，请检查所选协议、模型和地址。"});
        if(dictionary)
        {
            var json=await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc=Parse(json);
            CheckFailure(doc.RootElement,request.Provider.Kind);
            var text=ExtractText(doc.RootElement,request.Provider.Kind,false);
            yield return new(Entry:DictionaryParser.Parse(text),Completed:true);
            yield break;
        }
        await using var content=await response.Content.ReadAsStreamAsync(cancellationToken);
        var hasText=false;
        await foreach(var data in ReadEvents(content,cancellationToken))
        {
            if(data=="[DONE]")
            {
                if(!hasText)throw new QueryException("模型返回了空内容，请检查模型或重新查询。");
                yield return new(Completed:true);yield break;
            }
            using var doc=Parse(data);var root=doc.RootElement;
            CheckFailure(root,request.Provider.Kind);
            var text=ExtractText(root,request.Provider.Kind,true);
            if(text.Length>0) {hasText=true;yield return new(Delta:text);}
            if(IsComplete(root,request.Provider.Kind))
            {
                if(!hasText)throw new QueryException("模型返回了空内容，请重新查询。");
                yield return new(Completed:true);yield break;
            }
        }
        throw new QueryException("连接提前结束，当前内容可能不完整，请重新查询。");
    }

    private static JsonDocument Parse(string value)
    {
        try{return JsonDocument.Parse(value);}catch(JsonException){throw new QueryException("接口返回了无法解析的数据，请检查协议与地址。");}
    }
    private static string Value(JsonElement e,string key) => e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString()??"" : "";
    private static JsonElement? First(JsonElement e,string key) => e.TryGetProperty(key,out var a) && a.ValueKind==JsonValueKind.Array && a.GetArrayLength()>0 ? a[0]:null;

    private static string ExtractText(JsonElement root,ProviderKind kind,bool stream)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new QueryException("接口响应格式不正确。");
        if(kind==ProviderKind.OpenAI)
        {
            var choice=First(root,"choices");
            if(choice is {} c && c.TryGetProperty(stream?"delta":"message",out var m))return Value(m,"content");
        }
        else if(kind==ProviderKind.Claude)
        {
            if(stream)
            {
                if(Value(root,"type")=="content_block_delta" && root.TryGetProperty("delta",out var delta) && Value(delta,"type")=="text_delta")return Value(delta,"text");
                if(Value(root,"type")=="content_block_start" && root.TryGetProperty("content_block",out var block) && Value(block,"type")=="text")return Value(block,"text");
            }
            else if(root.TryGetProperty("content",out var blocks) && blocks.ValueKind==JsonValueKind.Array)
                return string.Concat(blocks.EnumerateArray().Where(b=>Value(b,"type")=="text").Select(b=>Value(b,"text")));
        }
        else
        {
            var candidate=First(root,"candidates");
            if(candidate is {} c && c.TryGetProperty("content",out var body) && body.TryGetProperty("parts",out var parts) && parts.ValueKind==JsonValueKind.Array)
                return string.Concat(parts.EnumerateArray().Where(p=>!p.TryGetProperty("thought",out var t)||t.ValueKind!=JsonValueKind.True).Select(p=>Value(p,"text")));
        }
        return "";
    }
    private static void CheckFailure(JsonElement root,ProviderKind kind)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw new QueryException("接口响应格式不正确。");
        if(root.TryGetProperty("error",out _) || Value(root,"type")=="error")throw new QueryException("服务在生成过程中返回错误，请手动重试。");
        string reason="";
        if(kind==ProviderKind.OpenAI && First(root,"choices") is {} choice)reason=Value(choice,"finish_reason");
        if(kind==ProviderKind.Claude)
        {
            reason=Value(root,"stop_reason");
            if(root.TryGetProperty("delta",out var delta))reason=Value(delta,"stop_reason");
        }
        if(kind==ProviderKind.Gemini)
        {
            if(First(root,"candidates") is {} c)reason=Value(c,"finishReason");
            if(root.TryGetProperty("promptFeedback",out var f) && Value(f,"blockReason").Length>0)throw new QueryException("服务未能处理此输入，请检查文本后重试。");
        }
        if(reason is "length" or "max_tokens" or "MAX_TOKENS")throw new QueryException("输出达到模型长度上限，内容未完成。请缩短原文后重试。");
        if(reason.Length>0 && reason is not ("stop" or "end_turn" or "stop_sequence" or "STOP"))throw new QueryException("模型未正常完成生成，请检查输入或模型后重试。");
    }
    private static bool IsComplete(JsonElement root,ProviderKind kind) => kind switch {
        ProviderKind.OpenAI => First(root,"choices") is {} c && Value(c,"finish_reason")=="stop",
        ProviderKind.Claude => Value(root,"type")=="message_stop",
        _ => First(root,"candidates") is {} c && Value(c,"finishReason")=="STOP"
    };
    private static async IAsyncEnumerable<string> ReadEvents(Stream stream,[EnumeratorCancellation]CancellationToken token)
    {
        using var reader=new StreamReader(stream,Encoding.UTF8,true,4096,leaveOpen:true);
        var data=new StringBuilder();
        while(await reader.ReadLineAsync(token) is {} line)
        {
            if(line.Length==0) {if(data.Length>0){yield return data.ToString();data.Clear();}continue;}
            if(line.StartsWith("data:",StringComparison.Ordinal))
            {
                if(data.Length>0)data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }
        if(data.Length>0)yield return data.ToString();
    }
}
