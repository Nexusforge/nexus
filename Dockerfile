FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR app
COPY app .

ENTRYPOINT ["./Nexus"]