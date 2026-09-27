# winget manifests

Templates for the [Windows Package Manager](https://github.com/microsoft/winget-pkgs) package `SametGurtuna.DockHub`.
`{VERSION}`, `{URL}` and `{SHA256}` are filled in by [`.github/workflows/winget.yml`](../../.github/workflows/winget.yml).

When a release is published, the workflow:

1. submits these manifests as a new package if `SametGurtuna.DockHub` is not in winget-pkgs yet, or
2. runs `wingetcreate update` for later versions.

It needs a repository secret named `WINGET_TOKEN`. Without it the workflow only prints a notice.

## Setting up `WINGET_TOKEN`

1. With the GitHub account that should open the pull requests, fork
   [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) (the *Fork* button).
2. Create a **classic** personal access token for that account: *Settings › Developer settings › Personal access tokens ›
   Tokens (classic) › Generate new token*, with only the `public_repo` scope. Pick an expiry you will remember to renew.
3. In this repository, *Settings › Secrets and variables › Actions › New repository secret*: name `WINGET_TOKEN`, value
   the token.

## Releasing

1. The release workflow builds a **draft** release from a `v*` tag.
2. Publishing the draft (not as a pre-release) starts the winget workflow. It can also be run by hand
   (*Actions › Winget › Run workflow*) with a release tag, for example to retry.
3. The workflow opens a pull request in `microsoft/winget-pkgs`. Its bots install DockHub silently in a clean VM; the
   first version of a package is also reviewed by a moderator, which can take a few days. Later versions are usually
   merged faster.
4. After the merge, `winget install SametGurtuna.DockHub` (or `winget install dockhub`) installs the new version,
   usually within a few hours.

If a check fails, the bot comments on the pull request with the reason. Fix the cause (for a first submission, often
these templates) and run the workflow again with the same tag.

The manifests say `License: Proprietary` because the repository has no license file yet; change it when one is added.
