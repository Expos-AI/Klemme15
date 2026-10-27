#!/usr/bin/env sh
# Fetch EdiabasLib (GPLv3, https://github.com/uholeschak/ediabaslib) into external/ediabaslib.
# Only needed for EDIABAS mode in the Windows build. Pinned to a tested commit.
set -e
COMMIT=3ef551e8ad425ee362868ff4bef1b55f15330039
DIR="$(cd "$(dirname "$0")/.." && pwd)/external/ediabaslib"
if [ ! -d "$DIR/.git" ]; then
  git init -q "$DIR"
  git -C "$DIR" remote add origin https://github.com/uholeschak/ediabaslib.git
fi
git -C "$DIR" fetch -q --depth 1 origin "$COMMIT"
git -C "$DIR" checkout -q FETCH_HEAD
echo "EdiabasLib ready at $DIR ($COMMIT)"
