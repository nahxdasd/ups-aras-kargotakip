FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

# Install Chrome and ChromeDriver in a single layer to reduce image size
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    gnupg \
    unzip \
    xvfb \
    libxi6 \
    libgconf-2-4 \
    default-jdk \
    curl \
    ca-certificates \
    fonts-liberation \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libatspi2.0-0 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libgbm1 \
    libgtk-3-0 \
    libnspr4 \
    libnss3 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxkbcommon0 \
    libxrandr2 \
    xdg-utils \
    && mkdir -p /etc/apt/keyrings \
    && wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /etc/apt/keyrings/google.gpg \
    && echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/google.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google.list \
    && apt-get update \
    && apt-get install -y google-chrome-stable \
    && CHROME_VERSION=$(google-chrome --version | awk '{print $3}' | cut -d'.' -f1) \
    && echo "Installed Chrome version: $CHROME_VERSION" \
    && wget -q "https://edgedl.me.gvt1.com/edgedl/chrome/chrome-for-testing/$(curl -s https://googlechromelabs.github.io/chrome-for-testing/LATEST_RELEASE_$CHROME_VERSION)/linux64/chromedriver-linux64.zip" -O /tmp/chromedriver.zip \
    && unzip -o /tmp/chromedriver.zip -d /tmp/ \
    && mv /tmp/chromedriver-linux64/chromedriver /usr/local/bin/ \
    && chmod +x /usr/local/bin/chromedriver \
    && rm -rf /tmp/chromedriver.zip /tmp/chromedriver-linux64 \
    && echo "export PATH=$PATH:/usr/local/bin" >> /etc/profile \
    && echo "export PATH=$PATH:/usr/local/bin" >> ~/.bashrc \
    && chromedriver --version \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy only the project file first to leverage Docker cache
COPY ["KargoTakip.csproj", "./"]
RUN dotnet restore "KargoTakip.csproj"

# Copy the rest of the source code
COPY . .
RUN dotnet build "KargoTakip.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "KargoTakip.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KargoTakip.dll"]
