FROM mcr.microsoft.com/dotnet/aspnet:10.0 
WORKDIR /app
COPY . .
RUN mkdir -p /app/data && chown -R "$APP_UID" /app /app/data 2>/dev/null || true && chmod -R 777 /app/data
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "{{ENTRY_DLL}}"]