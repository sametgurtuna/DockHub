# winget manifests

Templates for the [Windows Package Manager](https://github.com/microsoft/winget-pkgs) package `SametGurtuna.DockHub`.
`{VERSION}`, `{URL}` and `{SHA256}` are filled in by [`.github/workflows/winget.yml`](../../.github/workflows/winget.yml).

When a release is published, the workflow:

1. submits these manifests as a new package if `SametGurtuna.DockHub` is not in winget-pkgs yet, or
2. runs `wingetcreate update` for later versions.

It needs a repository secret named `WINGET_TOKEN`: a classic personal access token with the `public_repo` scope, of an
account that has a fork of `microsoft/winget-pkgs`. Without the secret the workflow only prints a notice. It can also be
run by hand (*Actions › Winget › Run workflow*) with a release tag.

The manifests say `License: Proprietary` because the repository has no license file yet; change it when one is added.
