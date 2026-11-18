FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Mg.Platform.Api/Mg.Platform.Api.csproj", "src/Mg.Platform.Api/"]
RUN dotnet restore "src/Mg.Platform.Api/Mg.Platform.Api.csproj"
COPY . .
WORKDIR /src/src/Mg.Platform.Api
RUN dotnet publish "Mg.Platform.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Mg.Platform.Api.dll"]
