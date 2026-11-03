#!/bin/bash

# Start SQL Server
/opt/mssql/bin/sqlservr &

# Wait for SQL Server to start
sleep 30

# Create database
for i in {1..50};
do
    /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -d master -Q "IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ProductDB') CREATE DATABASE ProductDB" 2>/dev/null
    if [ $? -eq 0 ]
    then
        echo "ProductDB created successfully"
        break
    else
        echo "Waiting for SQL Server to start... ($i/50)"
        sleep 2
    fi
done

# Keep container running
wait

