# Contributing

1. Read [AI_CONTEXT.md](AI_CONTEXT.md): project layout and the rules every change follows (build with 0 warnings, tests for every feature, localise every visible text in all six languages).
2. Build and test before opening a pull request:

   ```
   dotnet build MasterHub.slnx --nologo
   dotnet test MasterHub.slnx --nologo
   ```
3. Add a line to [CHANGELOG.md](CHANGELOG.md) under *Unreleased*.
4. Do not copy code, artwork or data from other projects; add any third-party asset to [NOTICE.md](NOTICE.md).
5. Security problems: see [SECURITY.md](SECURITY.md). Outbound connections must be listed in [PRIVACY.md](PRIVACY.md).
