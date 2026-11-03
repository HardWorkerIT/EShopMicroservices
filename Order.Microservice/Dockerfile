# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Order.Microservice/Order.Microservice.csproj", "Order.Microservice/"]
RUN dotnet restore "Order.Microservice/Order.Microservice.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src/Order.Microservice"
RUN dotnet build "Order.Microservice.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "Order.Microservice.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
EXPOSE 80
EXPOSE 443

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Order.Microservice.dll"]

