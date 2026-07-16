# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                        # build
dotnet run --urls=http://*:5000/    # run locally on port 5000
```

There is no test project. `dotnet test` has nothing to run.

Docker / compose:

```bash
docker build -t httpsmallcacheserver .
docker run -d -p 5000:5000 httpsmallcacheserver
docker compose up -d                # builds locally, maps host 5141 -> container 5000
```

On push to `main`, `.github/workflows/docker-build.yml` builds and pushes `ghcr.io/<owner>/httpsmallcacheserver:latest`.

## Architecture

Caching HTTP proxy built as a single-file ASP.NET Core Minimal API. Effectively all logic lives in `Program.cs`.

**Request flow:** The proxy takes the *target URL as the path*, e.g. `GET http://localhost:5000/https://catfact.ninja/fact` proxies to `https://catfact.ninja/fact`. Both endpoints use the catch-all route `/{**url}`; the captured `url` must parse as an absolute URI or the request 400s.

- `GET /{**url}` → cache key = SHA256("GET " + url). On hit, returns the cached file; on miss, fetches, caches, returns.
- `POST /{**url}` → cache key = SHA256("POST " + url + request body), so different bodies cache separately. Only successful responses are cached; the raw request body is always written to `<hash>.request` regardless.

Both handlers forward the client's request headers to the target (skipping `host`) by building an isolated `HttpRequestMessage` per outbound call — request headers go on `request.Headers`, content headers (`Content-Type`, …) fall back to `request.Content.Headers`. The method prefix in the hash keeps a GET and an empty-body POST on the same URL from colliding.

**Cache storage** (`CacheHelper`): flat directory, three files per entry keyed by hex hash:
- `<hash>.cache` — raw response bytes
- `<hash>.metadata.json` — `CacheMetadata` record (timestamp, request/response headers, status, content-type, url)
- `<hash>.request` — POST request body (POST only)

Cache directory resolves from the `CACHE_PATH` env var, falling back to `<WebRootPath>/cache`. In Docker `CACHE_PATH=/app/cache` and that path is the volume mount point.

**Retry** (`HttpHelper.ExecuteWithRetry`): wraps outbound calls. Retries only on HTTP 429, honoring `Retry-After` (delta or date) else exponential backoff, clamped to 1–60s, max 3 retries. Also retries on transport exceptions.

**HttpClient config** (registered in `Program.cs`): the named `"default"` client enables `AutomaticDecompression` for GZip/Deflate/**Brotli**. Both GET and POST use `"default"` — a target (Mistral) returned Brotli-encoded responses the stock handler could not decode. An unnamed client is also registered but no longer used by the handlers.

## Gotchas

- **No cache expiration / invalidation.** Entries live forever until the directory is cleared manually. There is no TTL or eviction.
- Changing `GenerateHash` (e.g. the method prefix) changes every key, orphaning the entire existing cache directory — old entries are never read again.
- `LoggingHandler.cs` is a standalone debugging `DelegatingHandler` and is **not** wired into the app — construct a client with it manually to trace request/response bodies to the console.
- Diagnostic output goes to `Console.WriteLine` (cache hits, POST bodies truncated to 100 chars, retry waits, failures), not a logging framework.
