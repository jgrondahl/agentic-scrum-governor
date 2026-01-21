# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY AgenticScrumGovernor.sln ./
COPY src/GovernorCli/GovernorCli.csproj ./src/GovernorCli/

RUN dotnet restore ./src/GovernorCli/GovernorCli.csproj

# Copy the remaining source
COPY . ./
RUN dotnet publish ./src/GovernorCli/GovernorCli.csproj -c Release -o /out --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /work

# Copy published output
COPY --from=build /out /app

# Default entrypoint: governor CLI
ENTRYPOINT ["dotnet", "/app/GovernorCli.dll"]
