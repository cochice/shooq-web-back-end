# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 9.0 Web API project (`Marvin.Tmtmfh91.Web.BackEnd`) - a minimal ASP.NET Core application with OpenAPI support. The project follows standard .NET Web API conventions with a single WeatherForecast controller as a starter template.

## Common Commands

### Development
```bash
# Build the project
dotnet build

# Run the application (development mode)
dotnet run --project Marvin.Tmtmfh91.Web.BackEnd

# Run with specific launch profile
dotnet run --project Marvin.Tmtmfh91.Web.BackEnd --launch-profile https
```

### Testing and Quality
```bash
# Restore dependencies
dotnet restore

# Clean build artifacts
dotnet clean

# Build in release mode
dotnet build --configuration Release
```

## Project Structure

- **Program.cs**: Main application entry point with service configuration and middleware pipeline
- **Controllers/**: API controllers (currently contains WeatherForecastController)
- **Models**: Domain models (WeatherForecast.cs in root for now)
- **Properties/launchSettings.json**: Development launch profiles with different URLs and environments

## Application Configuration

### Launch Profiles
- **http**: Development server on http://localhost:5255
- **https**: Development server on https://localhost:7284 and http://localhost:5255  
- **WSL**: WSL2 configuration for Linux development

### Key Services Configured
- Controllers with API support
- OpenAPI/Swagger (development only)
- HTTPS redirection
- Authorization middleware

## Architecture Notes

This is a standard minimal API setup with:
- Controller-based routing (`[Route("[controller]")]`)
- Dependency injection for logging
- OpenAPI documentation in development
- Standard ASP.NET Core middleware pipeline

The project uses nullable reference types and implicit usings (C# 9.0+ features).