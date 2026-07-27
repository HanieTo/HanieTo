# HanieTo

An ASP.NET Core Web API project.

## Structure

- `src/HanieTo.Api` — the Web API project (controllers, `Program.cs`, configuration)
- `tests/HanieTo.Api.Tests` — xUnit test project
- `HanieTo.sln` — solution file tying both projects together

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting started

```bash
# restore & build
dotnet build

# run the API
dotnet run --project src/HanieTo.Api

# run tests
dotnet test
```

Once running, try the sample endpoint (check the console output for the actual port):

```bash
curl http://localhost:5299/weatherforecast
```

## IDE

- **VS Code** + [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension
- **JetBrains Rider**
- **Visual Studio** (Windows)
