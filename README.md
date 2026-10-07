# Eden Consent Badge

Version 2 of the Eden Consent Badge, published by The Eden Apis.

People install it from [theedenapis.com/badge](https://theedenapis.com/badge). Creator Companion reads the package listing that this repository publishes with GitHub Pages.

The badge itself is `Packages/com.theedenapis.consentbadge`. Version `2.0.0` in `package.json` matches the version stored by the badge settings script.

## Create the GitHub repository

Create an empty public repository named `eden-consent-badge` on the `lolmaxz` account. Do not add a README there. From this folder:

```powershell
git add .
git commit -m "Add the Eden Consent Badge 2.0.0 package."
git remote add origin git@github.com:lolmaxz/eden-consent-badge.git
git push -u origin main
```

If the repository name is different, use that name in the remote, then change `listingUrl` in the website file `src/lib/pages/Badge.svelte` and the `/badge/index.json` rewrite in `vercel.json`.

## Let GitHub publish releases

In the GitHub repository:

1. Settings, Secrets and variables, Actions, Variables. Add a repository variable named `PACKAGE_NAME` with the value `com.theedenapis.consentbadge`.
2. Settings, Pages. Set Build and deployment to GitHub Actions.
3. Actions, Build Release, Run workflow.

That creates the `2.0.0` release and publishes the listing at:

https://lolmaxz.github.io/eden-consent-badge/index.json

Leave old releases in place. Deleting one breaks projects that are still on that version.

## What stays out of the package

`Assets/EdenApis` is the user's settings folder. It is created in their project and is not part of this package, so an update does not replace it.

The first Creator Companion install removes an older copy found at `Assets/EdenBadge` or `Assets/EdenBadgeV2`.
