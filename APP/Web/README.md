# TransLink Lite Web

Standalone Blazor WebAssembly/.NET 10 observer client for ephemeral realtime transcript and translation delivery. It communicates only through the public HTTP/WebSocket API and has no backend project references. Tokens and subtitle content remain in memory only.

```sh
dotnet run --project TransLink.Lite.Web --launch-profile http
```

Development configuration targets `http://localhost:5221`. Production must use the same API origin or an explicitly configured secure API URL and origin allowlist.
