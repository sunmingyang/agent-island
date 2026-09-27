# Sparkle auto-update

AgentIsland ships with [Sparkle 2](https://sparkle-project.org). On launch and
on a daily cadence the app fetches the appcast (an XML file attached to the
**latest** GitHub Release), compares versions, and prompts the user to
download + install if a newer build is listed. Updates are verified with an
EdDSA signature so a hijacked URL alone can't deliver malware.

The feed URL is:
```
https://github.com/sunmingyang/agent-island/releases/latest/download/appcast.xml
```
GitHub's `releases/latest/download/<asset>` endpoint always 302-redirects to
the asset on the most recent non-prerelease release.

## Key state

This fork signs its own updates. The **public** EdDSA key is pinned in
`build.sh` as `SU_PUBLIC_KEY` (public keys ship inside distributed apps so
Sparkle can verify update signatures — they are not secrets). The matching
**private** key lives in the maintainer's Keychain and, for CI, in the
`SPARKLE_ED_PRIVATE_KEY` GitHub Actions secret of this repository:

```
https://github.com/sunmingyang/agent-island/settings/secrets/actions
```

Never commit the private key. Lose it and existing installs can no longer
auto-update; you'd have to ship a new build with a fresh public key embedded,
which existing installs can't migrate to.

Installs built before 2.1.3 (including upstream 2.1.2 builds) still check the
retired upstream repository — they need one manual install before the
self-hosted feed works for them.

## Rotating the key

1. Vendor Sparkle if needed (idempotent): `./scripts/setup-sparkle.sh`
2. Generate a new keypair (private key lands in your Keychain, public key
   prints to stdout): `./Vendor/Sparkle/bin/generate_keys`
3. Replace `SU_PUBLIC_KEY` in `build.sh` with the printed public key.
4. Export the private key for CI and store it as the repository secret, then
   delete the file:
   ```sh
   ./Vendor/Sparkle/bin/generate_keys -x sparkle_ed_priv
   gh secret set SPARKLE_ED_PRIVATE_KEY --repo sunmingyang/agent-island < sparkle_ed_priv
   rm sparkle_ed_priv
   ```

## Cutting a release

1. Bump `VERSION` and add the matching `## [X.Y.Z]` section to `CHANGELOG.md`
   (the workflow refuses to publish empty release notes).
2. Commit + tag + push:
   ```sh
   git commit -am "chore(release): bump VERSION to X.Y.Z" && git tag vX.Y.Z
   git push origin main vX.Y.Z
   ```
3. The release workflow runs `release.sh` on a macOS runner, which:
   - Builds a universal DMG
   - Signs it with the EdDSA key from the secret
   - Generates `dist/appcast.xml` listing the new version
   - Uploads **both** as release assets

   The Windows Release workflow attaches the Windows zip to the same release
   once it appears.
4. Existing installs pick up the update on their next daily check (or via
   Settings → Updates → Check Now).

### Local dry-run

`./release.sh` works locally too — it falls back to the Keychain key when
`$SPARKLE_PRIVATE_KEY_PATH` is unset. The DMG and appcast land in `dist/`,
unpublished. Useful for testing the prompt flow before tagging.

## Disabling the feature for a build

Set `SU_FEED_URL=` (empty) before running `build.sh`. Sparkle will still load
but won't have a feed to poll.
