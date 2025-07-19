using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();
string CachePath = Environment.GetEnvironmentVariable("CACHE_PATH") 
    ?? Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "cache");
Directory.CreateDirectory(CachePath);

// Endpoint GET - Proxy com cache para requisições GET
app.MapGet("/{**url}", async (string url, IHttpClientFactory clientFactory, HttpContext context) =>
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var targetUri))
        return Results.BadRequest("Invalid target URL");
    
    string hash = await CacheHelper.GenerateHash(url);
    
    var cachedResult = await CacheHelper.LoadFromCache(hash, CachePath);
    if (cachedResult.HasValue)
    {
        var (cachedData, metadata) = cachedResult.Value;
        Console.WriteLine($"Cache hit for GET {url} (cached at {metadata.CachedAt})");
        return Results.File(cachedData, metadata.ContentType ?? "application/octet-stream");
    }
    
    var client = clientFactory.CreateClient();
    var response = await HttpHelper.ExecuteWithRetry(client, async (c) => await c.GetAsync(targetUri));
    var data = await response.Content.ReadAsByteArrayAsync();
    
    await CacheHelper.SaveToCache(hash, url, context.Request.Headers, response, data, CachePath);
    
    return Results.File(data, response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
});

// Endpoint POST - Proxy com cache para requisições POST
app.MapPost("/{**url}", async (HttpRequest req, string url, IHttpClientFactory clientFactory) =>
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var targetUri))
        return Results.BadRequest("Invalid target URL");
    
    Console.WriteLine($"Processing POST request to {url}");
    
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    ms.Position = 0;
    
    var contentType = req.ContentType ?? "";
   
    string hash = await CacheHelper.GenerateHash(url, ms);
    
    var cachedResult = await CacheHelper.LoadFromCache(hash, CachePath);
    if (cachedResult.HasValue)
    {
        var (cachedData, metadata) = cachedResult.Value;
        Console.WriteLine($"Cache hit for POST {url} (cached at {metadata.CachedAt})");
        return Results.File(cachedData, metadata.ContentType ?? "application/json");
    }
    
    ms.Position = 0;
    var client = clientFactory.CreateClient();
    var content = new StreamContent(ms);
    
    foreach (var header in req.Headers)
        content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    
    var response = await HttpHelper.ExecuteWithRetry(client, async (c) => await c.PostAsync(targetUri, content));
    var responseData = await response.Content.ReadAsByteArrayAsync();
    
    if (contentType.Contains("text") || contentType.Contains("json") || contentType.Contains("x-www-form-urlencoded"))
    {
        var bodyText = Encoding.UTF8.GetString(ms.ToArray());
        Console.WriteLine($"POST body:\n{bodyText.Substring(0, Math.Min(bodyText.Length, 100))}... Response status: {response.StatusCode}");
    }
    
    string requestFile = Path.Combine(CachePath, hash + ".request");
    await File.WriteAllBytesAsync(requestFile, ms.ToArray());
    
    if (response.IsSuccessStatusCode)
    {
        await CacheHelper.SaveToCache(hash, url, req.Headers, response, responseData, CachePath);
    }
    
    Results.StatusCode((int)response.StatusCode);
    return Results.File(responseData, response.Content.Headers.ContentType?.ToString() ?? "application/json");
});

app.Run();

/// <summary>
/// Metadados do cache contendo informações sobre a requisição e resposta
/// </summary>
/// <param name="CachedAt">Data e hora quando foi armazenado no cache</param>
/// <param name="RequestHeaders">Headers da requisição original</param>
/// <param name="ResponseHeaders">Headers da resposta do servidor</param>
/// <param name="StatusCode">Código de status HTTP da resposta</param>
/// <param name="ContentType">Tipo de conteúdo da resposta</param>
/// <param name="Url">URL original da requisição</param>
public record CacheMetadata(
    DateTime CachedAt,
    Dictionary<string, string[]> RequestHeaders,
    Dictionary<string, string[]> ResponseHeaders,
    int StatusCode,
    string? ContentType,
    string Url
);

/// <summary>
/// Helper para operações de cache
/// </summary>
public static class CacheHelper
{
    /// <summary>
    /// Gera um hash SHA256 baseado na URL e opcionalmente no corpo da requisição.
    /// Este hash é usado como chave única para identificar requisições no cache.
    /// </summary>
    /// <param name="url">A URL da requisição</param>
    /// <param name="body">O corpo da requisição (opcional, usado principalmente em POST)</param>
    /// <returns>String hexadecimal representando o hash SHA256</returns>
    public static async Task<string> GenerateHash(string url, Stream? body = null)
    {
        using var sha = SHA256.Create();
        
        // Adiciona a URL ao hash
        sha.TransformBlock(Encoding.UTF8.GetBytes(url), 0, url.Length, null, 0);
        
        // Se há corpo da requisição, adiciona ao hash
        if (body != null)
        {
            using var ms = new MemoryStream();
            await body.CopyToAsync(ms);
            ms.Position = 0;
            sha.TransformBlock(ms.ToArray(), 0, (int)ms.Length, null, 0);
            
            // Reseta a posição do stream original para reutilização
            body.Position = 0;
        }
        
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>
    /// Salva dados no cache junto com metadados em arquivo JSON separado
    /// </summary>
    /// <param name="hash">Hash identificador único do cache</param>
    /// <param name="url">URL da requisição</param>
    /// <param name="requestHeaders">Headers da requisição</param>
    /// <param name="response">Resposta HTTP recebida</param>
    /// <param name="data">Dados binários da resposta</param>
    /// <param name="cachePath">Caminho do diretório de cache</param>
    public static async Task SaveToCache(string hash, string url, IHeaderDictionary requestHeaders, 
        HttpResponseMessage response, byte[] data, string cachePath)
    {
        string cacheFile = Path.Combine(cachePath, hash + ".cache");
        string metadataFile = Path.Combine(cachePath, hash + ".metadata.json");
        
        // Salva o conteúdo binário
        await File.WriteAllBytesAsync(cacheFile, data);
        
        // Prepara metadados
        var metadata = new CacheMetadata(
            CachedAt: DateTime.UtcNow,
            RequestHeaders: requestHeaders.ToDictionary(
                h => h.Key, 
                h => h.Value.Where(v => v != null).Select(v => v ?? string.Empty).ToArray()
            ),
            ResponseHeaders: response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(h => h.Key, h => h.Value.ToArray()),
            StatusCode: (int)response.StatusCode,
            ContentType: response.Content.Headers.ContentType?.ToString(),
            Url: url
        );
        
        // Salva metadados como JSON
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(metadataFile, JsonSerializer.Serialize(metadata, jsonOptions));
    }

    /// <summary>
    /// Carrega dados do cache e aplica headers da resposta original
    /// </summary>
    /// <param name="hash">Hash identificador único do cache</param>
    /// <param name="cachePath">Caminho do diretório de cache</param>
    /// <returns>Resultado com dados e metadados, ou null se não encontrado</returns>
    public static async Task<(byte[] Data, CacheMetadata Metadata)?> LoadFromCache(string hash, string cachePath)
    {
        string cacheFile = Path.Combine(cachePath, hash + ".cache");
        string metadataFile = Path.Combine(cachePath, hash + ".metadata.json");
        
        // Verifica se o arquivo de dados existe
        if (!File.Exists(cacheFile))
            return null;
        
        try
        {
            // Carrega dados
            var data = await File.ReadAllBytesAsync(cacheFile);
            
            CacheMetadata metadata;
            
            // Se metadados não existem, cria padrão
            if (!File.Exists(metadataFile))
            {
                Console.WriteLine($"Metadata file not found for {hash}, creating default metadata");
                
                metadata = new CacheMetadata(
                    CachedAt: DateTime.UtcNow,
                    RequestHeaders: new Dictionary<string, string[]>(),
                    ResponseHeaders: new Dictionary<string, string[]>
                    {
                        ["Content-Type"] = new[] { "application/json; charset=utf-8" }
                    },
                    StatusCode: 200,
                    ContentType: "application/json; charset=utf-8",
                    Url: "unknown"
                );
                
                // Salva o metadata padrão para futuras consultas
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(metadataFile, JsonSerializer.Serialize(metadata, jsonOptions));
            }
            else
            {
                // Carrega metadados existentes
                var metadataJson = await File.ReadAllTextAsync(metadataFile);
                var deserializedMetadata = JsonSerializer.Deserialize<CacheMetadata>(metadataJson);
                
                if (deserializedMetadata == null)
                {
                    Console.WriteLine($"Failed to deserialize metadata for {hash}, using default");
                    
                    metadata = new CacheMetadata(
                        CachedAt: DateTime.UtcNow,
                        RequestHeaders: new Dictionary<string, string[]>(),
                        ResponseHeaders: new Dictionary<string, string[]>
                        {
                            ["Content-Type"] = new[] { "application/json; charset=utf-8" }
                        },
                        StatusCode: 200,
                        ContentType: "application/json; charset=utf-8",
                        Url: "unknown"
                    );
                }
                else
                {
                    metadata = deserializedMetadata;
                }
            }
            
            return (data, metadata);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading cache {hash}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Helper para operações HTTP com retry
/// </summary>
public static class HttpHelper
{
    /// <summary>
    /// Executa uma requisição HTTP com retry automático em caso de rate limit.
    /// Implementa backoff exponencial para aguardar antes de tentar novamente.
    /// </summary>
    /// <param name="client">Cliente HTTP a ser usado</param>
    /// <param name="requestFunc">Função que executa a requisição HTTP</param>
    /// <param name="maxRetries">Número máximo de tentativas (padrão: 3)</param>
    /// <returns>Resposta HTTP da requisição bem-sucedida</returns>
    public static async Task<HttpResponseMessage> ExecuteWithRetry(
        HttpClient client, 
        Func<HttpClient, Task<HttpResponseMessage>> requestFunc,
        int maxRetries = 3)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await requestFunc(client);
                
                // Se não é rate limit, retorna a resposta (sucesso ou erro)
                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return response;
                }
                
                // Se é rate limit e ainda há tentativas restantes
                if (attempt < maxRetries)
                {
                    // Verifica se existe header Retry-After
                    var retryAfter = response.Headers.RetryAfter;
                    int waitSeconds;
                    
                    if (retryAfter?.Delta.HasValue == true)
                    {
                        // Usa o tempo especificado no header Retry-After
                        waitSeconds = (int)retryAfter.Delta.Value.TotalSeconds;
                    }
                    else if (retryAfter?.Date.HasValue == true)
                    {
                        // Calcula diferença até a data especificada
                        waitSeconds = (int)(retryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds;
                    }
                    else
                    {
                        // Backoff exponencial: 2^attempt segundos (2, 4, 8...)
                        waitSeconds = (int)Math.Pow(2, attempt + 1);
                    }
                    
                    // Garante um mínimo de 1 segundo e máximo de 60 segundos
                    waitSeconds = Math.Max(1, Math.Min(waitSeconds, 60));
                    
                    Console.WriteLine($"Rate limit detected. Waiting {waitSeconds} seconds before retry {attempt + 1}/{maxRetries}...");
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                    
                    response.Dispose(); // Libera recursos da resposta com erro
                }
                else
                {
                    // Última tentativa, retorna o erro de rate limit
                    Console.WriteLine($"Rate limit exceeded after {maxRetries} retries.");
                    return response;
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                // Em caso de exceção (timeout, network error, etc.), tenta novamente
                Console.WriteLine($"Request failed on attempt {attempt + 1}: {ex.Message}");
                
                if (attempt < maxRetries)
                {
                    int waitSeconds = (int)Math.Pow(2, attempt + 1);
                    Console.WriteLine($"Waiting {waitSeconds} seconds before retry...");
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                }
            }
        }
        
        // Se chegou aqui, todas as tentativas falharam com exceção
        throw new HttpRequestException($"Request failed after {maxRetries + 1} attempts");
    }
}