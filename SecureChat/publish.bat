@echo off
del latest.7z
del .\bin\Release /F /Q
dotnet publish "SecureChat.csproj" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishReadyToRun=false ^
  -p:DebugType=none
7z a latest.7z -r ".\bin\Release\net8.0-windows\win-x64\publish"
pause