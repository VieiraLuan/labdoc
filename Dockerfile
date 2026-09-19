FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LabDoc.Api/LabDoc.Api.csproj src/LabDoc.Api/
RUN dotnet restore src/LabDoc.Api/LabDoc.Api.csproj
COPY . .
RUN dotnet publish src/LabDoc.Api/LabDoc.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "LabDoc.Api.dll"]
