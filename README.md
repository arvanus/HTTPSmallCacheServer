# HTTPSmallCacheServer

HTTPSmallCacheServer is a lightweight HTTP server with caching capabilities. It is designed for simple use cases where serving static files and caching responses can improve performance.

## Features

- Serves static files over HTTP
- Local disk caching
- Simple configuration and setup

## Getting Started

1. **Clone the repository:**
    ```bash
    git clone https://github.com/arvanus/HTTPSmallCacheServer.git
    ```

2. **Build and run:**
    ```
    dotnet run
    ```

3. **run:**
```shell
curl http://localhost:5000/https://catfact.ninja/fact
```

## Configuration

You can configure the server by editing the `Program.cs` file. Common options include:

- Port number
- Directory to serve files from

## License

This project is licensed under the MIT License.
