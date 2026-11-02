FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Restaurant.Web/Restaurant.Web.csproj backend/Restaurant.Web/
COPY backend/restaurant-backend-app/dnapp/lambdas/api-handler/APIHandler.csproj backend/restaurant-backend-app/dnapp/lambdas/api-handler/
RUN dotnet restore backend/Restaurant.Web/Restaurant.Web.csproj
COPY backend/Restaurant.Web/ backend/Restaurant.Web/
COPY backend/restaurant-backend-app/dnapp/lambdas/api-handler/ backend/restaurant-backend-app/dnapp/lambdas/api-handler/
RUN dotnet publish backend/Restaurant.Web/Restaurant.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Restaurant.Web.dll"]
