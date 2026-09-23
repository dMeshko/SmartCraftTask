FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# The central package and build props come first: with ManagePackageVersionsCentrally the restore
# cannot resolve a single version without them, so copying only the csproj would fail.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/SmartCraftTask/SmartCraftTask.csproj", "src/SmartCraftTask/"]
RUN dotnet restore "src/SmartCraftTask/SmartCraftTask.csproj"
COPY . .
WORKDIR "/src/src/SmartCraftTask"
RUN dotnet build "./SmartCraftTask.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SmartCraftTask.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartCraftTask.dll"]
