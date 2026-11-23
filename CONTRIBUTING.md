# Contributing

Thanks for wanting to improve Sonic Screwdriver. Contributions are welcome: bug fixes, features,
documentation and tests.

## How

1. Fork the repository (or create a branch if you have write access).
2. Keep the change small and focused, one topic per pull request.
3. Add or update tests, then run `dotnet test`.
4. Open a pull request against `main`. The CI (tests and CodeQL) must be green before it is merged.

Tips:

- Build and test instructions are in the [README](README.md#build).
- The integration tests and `tools/e2e-test.ps1` need a real audio device (CI runners have none), so
  run them locally when you touch audio, keyboard hook or tray code.
- If you change what users see (features, shortcuts, settings), update the README and the
  [CHANGELOG](CHANGELOG.md).
- Dependency and GitHub Actions updates come from Dependabot, in grouped pull requests.

## License of your contribution

Sonic Screwdriver is free software under the [GPL-3.0](LICENSE) license.

By submitting a contribution (a pull request, a patch, or any code, documentation or asset), you agree
that:

1. You wrote it, or you have the right to submit it.
2. It is licensed under the project's license, [GPL-3.0](LICENSE). You keep the copyright on your contribution.

Opening a pull request means you accept these terms.
