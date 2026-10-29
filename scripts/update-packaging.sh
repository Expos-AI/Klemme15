#!/usr/bin/env bash
# Regenerate the Scoop manifest and winget manifests for a release.
# Usage: update-packaging.sh <tag e.g. v0.12.1> <path to the win-x64 .zip>
# Keeps the install recipes correct (version + sha256) on every release, with no manual editing.
set -euo pipefail

TAG="${1:?tag required, e.g. v0.12.1}"
ZIP="${2:?path to the win-x64 zip required}"
VERSION="${TAG#v}"
OWNER="Expos-AI"
REPO="Klemme15"
PKGID="Expos-AI.Klemme15"
URL="https://github.com/${OWNER}/${REPO}/releases/download/${TAG}/${REPO}-${TAG}-win-x64.zip"
HASH="$(sha256sum "$ZIP" | cut -d' ' -f1)"
HASH_UPPER="$(printf '%s' "$HASH" | tr '[:lower:]' '[:upper:]')"
DESC="Open-source BMW E-series diagnostics: scan modules, read and clear faults, live data, guided tests and service functions with a K+DCAN cable."

mkdir -p bucket "packaging/winget"

# ---- Scoop (bucket/klemme15.json). The repo doubles as a Scoop bucket. ----
cat > bucket/klemme15.json <<JSON
{
    "version": "${VERSION}",
    "description": "${DESC}",
    "homepage": "https://github.com/${OWNER}/${REPO}",
    "license": "GPL-3.0-or-later",
    "architecture": {
        "64bit": {
            "url": "${URL}",
            "hash": "${HASH}",
            "extract_dir": "${REPO}-${TAG}-win-x64"
        }
    },
    "bin": [
        "Klemme15.exe",
        [
            "cli\\\\klemme15.exe",
            "klemme15"
        ]
    ],
    "shortcuts": [
        [
            "Klemme15.exe",
            "Klemme15"
        ]
    ],
    "checkver": "github",
    "autoupdate": {
        "architecture": {
            "64bit": {
                "url": "https://github.com/${OWNER}/${REPO}/releases/download/v\$version/${REPO}-v\$version-win-x64.zip",
                "extract_dir": "${REPO}-v\$version-win-x64"
            }
        }
    }
}
JSON

# ---- winget (packaging/winget/*.yaml), manifest schema 1.6, zip + portable nested installer ----
WDIR="packaging/winget"
cat > "${WDIR}/${PKGID}.installer.yaml" <<YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: ${PKGID}
PackageVersion: ${VERSION}
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
  - RelativeFilePath: ${REPO}-${TAG}-win-x64\\Klemme15.exe
    PortableCommandAlias: klemme15-gui
  - RelativeFilePath: ${REPO}-${TAG}-win-x64\\cli\\klemme15.exe
    PortableCommandAlias: klemme15
Installers:
  - Architecture: x64
    InstallerUrl: ${URL}
    InstallerSha256: ${HASH_UPPER}
ManifestType: installer
ManifestVersion: 1.6.0
YAML

cat > "${WDIR}/${PKGID}.locale.en-US.yaml" <<YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: ${PKGID}
PackageVersion: ${VERSION}
PackageLocale: en-US
Publisher: ${OWNER}
PublisherUrl: https://github.com/${OWNER}
PackageName: Klemme15
PackageUrl: https://github.com/${OWNER}/${REPO}
License: GPL-3.0-or-later
LicenseUrl: https://github.com/${OWNER}/${REPO}/blob/main/LICENSE
ShortDescription: "${DESC}"
Moniker: klemme15
Tags:
  - bmw
  - diagnostics
  - obd
  - automotive
  - e90
ManifestType: defaultLocale
ManifestVersion: 1.6.0
YAML

cat > "${WDIR}/${PKGID}.yaml" <<YAML
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: ${PKGID}
PackageVersion: ${VERSION}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
YAML

echo "Packaging updated for ${TAG}: sha256 ${HASH}"
