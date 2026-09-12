#!/usr/bin/env bash
# Install Unity's SmartMerge (UnityYAMLMerge) as the git merge driver for this
# repository, so concurrent edits to .unity scenes / .prefab files resolve
# semantically instead of producing raw YAML conflicts.
#
# Run once per clone, from anywhere inside the repo:
#     ./scripts/install-unityyamlmerge.sh
#
# This writes only to .git/config (repo-local), because the merge binary path
# is machine-specific. Teammates who skip this are unaffected: git silently
# falls back to the normal 3-way text merge.
#
# Reference: https://docs.unity3d.com/Manual/SmartMerge.html

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

# --- Locate UnityYAMLMerge -------------------------------------------------
candidate=""

# 1. Explicit override
if [[ -n "${UNITY_YAMLMERGE:-}" && -x "${UNITY_YAMLMERGE}" ]]; then
  candidate="${UNITY_YAMLMERGE}"
fi

# 2. Derive from this project's editor version, then fall back to any install.
if [[ -z "$candidate" && -f "ProjectSettings/ProjectVersion.txt" ]]; then
  version="$(awk '/m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)"
  for hub in "/Applications/Unity/Hub/Editor" "$HOME/Applications/Unity/Hub/Editor"; do
    p="$hub/$version/Unity.app/Contents/Helpers/UnityYAMLMerge"
    [[ -x "$p" ]] && candidate="$p" && break
  done
fi

if [[ -z "$candidate" ]]; then
  while IFS= read -r p; do
    [[ -x "$p" ]] && candidate="$p" && break
  done < <(find /Applications/Unity/Hub/Editor -maxdepth 5 -name UnityYAMLMerge -type f 2>/dev/null | sort -r)
fi

if [[ -z "$candidate" ]]; then
  cat >&2 <<'EOF'
ERROR: could not find UnityYAMLMerge.

Install the matching Unity editor via Unity Hub, or point at the binary
explicitly (note: it lives in Contents/Helpers/, NOT Contents/Resources/):

    UNITY_YAMLMERGE="/path/to/Unity.app/Contents/Helpers/UnityYAMLMerge" \
        ./scripts/install-unityyamlmerge.sh
EOF
  exit 1
fi

# --- Configure the driver (repo-local) -------------------------------------
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "\"$candidate\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary

# --- Git LFS (repo-local filters) ------------------------------------------
if command -v git-lfs >/dev/null 2>&1; then
  git lfs install --local >/dev/null
  echo "Git LFS: filters installed for this repo."
else
  echo "Git LFS: not installed — run 'brew install git-lfs' to use LFS-tracked assets." >&2
fi

echo "Unity SmartMerge: registered -> $candidate"
echo
echo "Verify with:  git config --get merge.unityyamlmerge.driver"
