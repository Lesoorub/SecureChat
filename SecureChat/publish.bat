REM @echo off
del latest.7z
del .\bin\Release /F /Q
dotnet publish "SecureChat.csproj" --configuration Release
7z a latest.7z -r ".\bin\Release\net8.0\win-x64\publish\*"