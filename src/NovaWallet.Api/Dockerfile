FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/NovaWallet.Api/NovaWallet.Api.csproj", "src/NovaWallet.Api/"]
RUN dotnet restore "src/NovaWallet.Api/NovaWallet.Api.csproj"

COPY . .
WORKDIR "/src/src/NovaWallet.Api"
RUN dotnet publish "NovaWallet.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
