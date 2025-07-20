# HTTPSmallCacheServer

## Overview
HTTPSmallCacheServer is a simple caching proxy server built using ASP.NET Core. It provides endpoints for handling GET and POST requests while caching responses to improve performance and reduce redundant network calls.

## Features
- Caching of GET and POST requests
- Automatic handling of request and response headers
- Retry mechanism for handling rate limits

## Prerequisites
- .NET SDK (version 9.0 or later)
- Docker (for building and running the Docker image)

## Setup Instructions

### Clone the Repository
```bash
git clone https://github.com/arvanus/HTTPSmallCacheServer.git
cd HTTPSmallCacheServer
```

### Build the Application
To build the application locally, run the following command:
```bash
dotnet build
```

### Run the Application
You can run the application using the following command:
```bash
dotnet run --urls=http://*:5000/
```
The application will start on `http://localhost:5000`.

## Docker Instructions

### Build the Docker Image
To build the Docker image, use the following command:
```bash
docker build -t httpsmallcacheserver .
```

### Run the Docker Container
To run the Docker container, execute:
```bash
docker run -d -p 5000:5000 httpsmallcacheserver
```
The application will be accessible at `http://localhost:5000`.

## Run published image:

You can use Docker Compose to run the published image. Create a `docker-compose.yml` file with the following content:

```yaml
services:
    httpsmallcacheserver:
        image:  ghcr.io/arvanus/httpsmallcacheserver
        volumes:
        - ./cache:/app/cache
        ports:
        - "5000:5000"
```

Then start the service with:

```bash
docker-compose up -d
```

The application will be available at `http://localhost:5000`.


## Usage
You can use any HTTP client to send requests to the server. For example, using `curl`:
```bash
curl http://localhost:5000/https://catfact.ninja/fact
```

## Contributing
Contributions are welcome! Please open an issue or submit a pull request for any enhancements or bug fixes.

## License
This project is licensed under the MIT License.