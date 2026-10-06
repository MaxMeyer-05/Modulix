FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Modulix.Scanner.csproj", "./"]
RUN dotnet restore "Modulix.Scanner.csproj"

COPY . .
RUN dotnet publish "Modulix.Scanner.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Modulix.Scanner.dll"]