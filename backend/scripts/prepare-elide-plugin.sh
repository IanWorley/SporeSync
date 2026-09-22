#!/usr/bin/env bash
set -euo pipefail

readonly PLUGIN_COMMIT='a1bb1307203acb44fa0d622aad4870c69f1144e1'
readonly PLUGIN_ARCHIVE_SHA256='f38cde05fa0ee59fc4b7d8c592379f96a98de28029b1eba9fb0c7c307749402a'
readonly PLUGIN_ARCHIVE_URL="https://github.com/elide-dev/gradle/archive/$PLUGIN_COMMIT.tar.gz"
readonly PLUGIN_ARCHIVE_ROOT="gradle-$PLUGIN_COMMIT"
readonly DOWNLOAD_RETRIES=3
readonly SCRIPT_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly BACKEND_DIRECTORY="$(cd -- "$SCRIPT_DIRECTORY/.." && pwd)"
readonly DEV_DIRECTORY="$BACKEND_DIRECTORY/.dev"
readonly PLUGIN_ARCHIVE="$DEV_DIRECTORY/elide-gradle-$PLUGIN_COMMIT.tar.gz"
readonly PLUGIN_SOURCE_DIRECTORY="$DEV_DIRECTORY/elide-gradle"
readonly SOURCE_MARKER="$PLUGIN_SOURCE_DIRECTORY/.sporesync-plugin-commit"

archive_checksum() {
    shasum -a 256 "$1" | awk '{print $1}'
}

verify_archive() {
    [[ "$(archive_checksum "$PLUGIN_ARCHIVE")" == "$PLUGIN_ARCHIVE_SHA256" ]]
}

mkdir -p "$DEV_DIRECTORY"

if [[ -f "$PLUGIN_ARCHIVE" ]] && ! verify_archive; then
    echo "The cached Elide Gradle archive failed checksum verification: $PLUGIN_ARCHIVE" >&2
    exit 1
fi

if [[ ! -f "$PLUGIN_ARCHIVE" ]]; then
    temporary_archive="$(mktemp "$DEV_DIRECTORY/elide-gradle.archive.XXXXXX")"
    curl --fail --location --retry "$DOWNLOAD_RETRIES" --output "$temporary_archive" "$PLUGIN_ARCHIVE_URL"

    if [[ "$(archive_checksum "$temporary_archive")" != "$PLUGIN_ARCHIVE_SHA256" ]]; then
        echo "Downloaded Elide Gradle archive failed checksum verification." >&2
        exit 1
    fi

    mv "$temporary_archive" "$PLUGIN_ARCHIVE"
fi

if [[ -f "$SOURCE_MARKER" ]] && [[ "$(<"$SOURCE_MARKER")" == "$PLUGIN_COMMIT" ]]; then
    echo "Elide Gradle plugin source is ready at $PLUGIN_SOURCE_DIRECTORY"
    exit 0
fi

if [[ -e "$PLUGIN_SOURCE_DIRECTORY" ]]; then
    echo "Elide Gradle plugin source has an unexpected state at $PLUGIN_SOURCE_DIRECTORY." >&2
    echo "Move that directory aside, then rerun bash scripts/prepare-elide-plugin.sh." >&2
    exit 1
fi

temporary_source="$(mktemp -d "$DEV_DIRECTORY/elide-gradle.extract.XXXXXX")"
tar -xzf "$PLUGIN_ARCHIVE" -C "$temporary_source"

if [[ ! -f "$temporary_source/$PLUGIN_ARCHIVE_ROOT/settings.gradle.kts" ]]; then
    echo "Elide Gradle archive did not contain the expected source root." >&2
    exit 1
fi

printf '%s\n' "$PLUGIN_COMMIT" > "$temporary_source/$PLUGIN_ARCHIVE_ROOT/.sporesync-plugin-commit"
mv "$temporary_source/$PLUGIN_ARCHIVE_ROOT" "$PLUGIN_SOURCE_DIRECTORY"
rmdir "$temporary_source"

echo "Prepared Elide Gradle plugin source at $PLUGIN_SOURCE_DIRECTORY"
