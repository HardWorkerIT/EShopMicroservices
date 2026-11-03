# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Product.Microservice/Product.Microservice.csproj", "Product.Microservice/"]
RUN dotnet restore "Product.Microservice/Product.Microservice.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Product.Microservice"
RUN dotnet build "Product.Microservice.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "Product.Microservice.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Product.Microservice.dll"]

