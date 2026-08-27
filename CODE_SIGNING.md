# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

SmoothFolder is preparing to use SignPath Foundation for official release
signing. Until onboarding is complete, previously published GitHub Release
artifacts may be unsigned. Once signing is enabled, only artifacts produced and
approved under this policy are eligible for an official SmoothFolder signature.

## Team roles

- Authors / committers: [Alejandro-GZ](https://github.com/Alejandro-GZ)
- Reviewers: [Alejandro-GZ](https://github.com/Alejandro-GZ)
- Approvers: [Alejandro-GZ](https://github.com/Alejandro-GZ)

Changes proposed by contributors who are not committers must be reviewed by a
project reviewer before merge. Every signing request for an official release
must be manually approved by an approver.

Project members with repository or SignPath access must use multi-factor
authentication.

## Privacy policy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.

SmoothFolder does not collect telemetry or analytics. Its configuration,
imported shortcut copies, settings, and bounded diagnostic logs are stored
locally under `%LOCALAPPDATA%\SmoothFolder`.

SmoothFolder can launch user-selected shortcuts, URLs, and third-party
applications. Any network activity performed by software or services launched at
the user's request is governed by those third parties and is not telemetry
collected by SmoothFolder.

## Release and signing requirements

Official signed releases must:

- be built from the source code and build scripts in this repository using the
  project's automated GitHub Actions release pipeline;
- correspond to a version tag in this repository;
- sign only SmoothFolder project binaries produced from SmoothFolder source;
- use product metadata identifying the application as `SmoothFolder` and keep
  version metadata consistent within a build;
- require manual approval for every signing request;
- preserve verifiable build provenance from repository source to signed
  artifact; and
- be verified after signing before the signed artifact is published as an
  official release.

A signing request must not be approved if the source revision, build provenance,
artifact identity, or requested release cannot be verified.
