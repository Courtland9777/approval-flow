FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ApprovalFlow.slnx
RUN dotnet publish src/ApprovalFlow.Api/ApprovalFlow.Api.csproj -c Release --no-restore -o /app/api
RUN dotnet publish src/ApprovalFlow.Worker/ApprovalFlow.Worker.csproj -c Release --no-restore -o /app/worker

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS api
WORKDIR /app
COPY --from=build /app/api .
ENTRYPOINT ["dotnet", "ApprovalFlow.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0.10 AS worker
WORKDIR /app
COPY --from=build /app/worker .
ENTRYPOINT ["dotnet", "ApprovalFlow.Worker.dll"]

FROM node:24.4.1-alpine AS web-build
WORKDIR /src
COPY src/ApprovalFlow.Web/package*.json ./
RUN npm ci
COPY src/ApprovalFlow.Web/ ./
RUN npm run build

FROM nginx:1.29.0-alpine AS web
COPY infrastructure/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=web-build /src/dist /usr/share/nginx/html
EXPOSE 80
