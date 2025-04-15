This is a modified fork of the open source WebSocketSharp library:

https://github.com/sta/websocket-sharp

If you need access to the modified source, email support@virtualmaker.net.

Modifications:
- Prefixed all namespaces with 'Proxima' to avoid collisions.
- Previously, if HttpServer was initialized as 'secure', it would fail for HTTP requests. Now, it will handle HTTP requests by redirecting to HTTPS.
- Updated targeted .NET version to 4.7.1.
- Added default parameter to SslStrema constructor, to avoid failure on older Unity versions.
- Added SendAsTextAsync to avoid some memory allocation and send MemoryStream directly as string.