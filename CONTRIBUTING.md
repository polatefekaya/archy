# Contributing to Archy

Open an issue before broad cross-cutting work. Keep a change inside its vertical slice, add behavior-focused tests, preserve Native AOT compatibility, and document configuration/coverage consequences.

Before proposing a change, run `zsh scripts/test.sh`, `npm run build` from `ui/archy-web`, and `dotnet publish src/Archy/Archy.csproj --configuration Release --runtime osx-arm64 --self-contained true --no-restore`.
