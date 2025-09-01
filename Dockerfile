# 런타임 이미지
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000

# 빌드 이미지
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Marvin.Tmtmfh91.Web.Backend.csproj", "./"]
RUN dotnet restore "Marvin.Tmtmfh91.Web.Backend.csproj"
COPY . .
RUN dotnet publish "Marvin.Tmtmfh91.Web.Backend.csproj" -c Release -o /app/publish

# 최종 이미지
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Marvin.Tmtmfh91.Web.Backend.dll"]