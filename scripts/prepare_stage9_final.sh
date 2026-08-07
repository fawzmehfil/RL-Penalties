#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.0.74f1/Unity.app/Contents/MacOS/Unity"
PROJECT="$ROOT/unity"
RESULTS="$ROOT/results/stage9-final"

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

cd "$ROOT"
mkdir -p "$RESULTS"
test -x "$UNITY" || fail "Unity 6000.0.74f1 is not installed"
test -x .venv/bin/python || fail "Run scripts/setup_python.sh first"
test -f web/stage8-analysis/package-lock.json || fail "Stage 8 analysis source is missing"
pgrep -f "Unity.app/Contents/MacOS/Unity.*-project[Pp]ath $PROJECT" >/dev/null &&
  fail "Close the Unity editor for this project before running the handoff"

echo "Stage 9: validating licensed audio assets"
.venv/bin/python - <<'PY'
from hashlib import sha256
from pathlib import Path

expected = {
    "Freesound/goal_reaction_cc0.ogg": "c1792d12aa520a29ae6926cf29a0c94371104d9794331049f4ac0918d08537dd",
    "Freesound/miss_reaction_cc0.ogg": "14f01554fc4897df23189b0c787fc57d5e5e940ed194620f1fe4671c367ec379",
    "Freesound/save_reaction_cc0.ogg": "d75ef600681c83cbeb3673976aed4cd5e288e3c7b0dbc01f61afe6bda8b96143",
    "Freesound/stadium_ambience_cc0.ogg": "cfb7c910374a188ae380e52a3145bda42dd4d89d5f85facb2889881f6ecc40c9",
    "KenneyImpact/body_01.ogg": "7d3ba0bb5e60a11b5d3e558c141303dcf494256675fbf753c0d252d2cf0481e3",
    "KenneyImpact/body_02.ogg": "7642a4fd43e547afe4f7adfadb3dabb681c0ff512f52c1674bae30a726841faf",
    "KenneyImpact/bounce_01.ogg": "5069e3571a77d7f7aae9ef71d0364aa245fb7d64a7c8cc9956f221d03088c089",
    "KenneyImpact/bounce_02.ogg": "5c4a1f35fde7e14046931da7bc3d1b23736541b7190ba107e08a379c4ca43cd6",
    "KenneyImpact/frame_01.ogg": "a96f879fec0864a8938e0c745b6996a6c5679c16a234ce31a01cc995e8401003",
    "KenneyImpact/frame_02.ogg": "e8a9eaba7c4d27422e4eeb3e6c7100d5d7dc0f83e005efc98c960adcc5265337",
    "KenneyImpact/glove_01.ogg": "49e7ca88743fca974bb8676ea138b751cfd8f9033b5e7af8736c2a215d6edbc1",
    "KenneyImpact/glove_02.ogg": "4d0096364ba9e46119d2ff6df493fdc101bd2e1efae061da5e5f77a53b3fdcb6",
    "KenneyImpact/glove_03.ogg": "d130ae541951243d3f6ed963d0057450a445ffe117fbf9a084d34eb1635cb563",
    "KenneyImpact/net_01.ogg": "f0e982611e97512fee5f777986b67e8b435434b601f94992ec044f7e89fb5acb",
    "KenneyImpact/net_02.ogg": "c3cd1c073d186ae8fa35788ba94de581f1826e7427a7bb26490b2695fac18efa",
    "KenneyImpact/strike_01.ogg": "b33a8f14068aec24ec69ba85e5e87fdc41228975f6a1a3e44a6e7d6fc3d9f8d8",
    "KenneyImpact/strike_02.ogg": "f92f5cb6ba4ff2766497292ffd90865654317eeca976f5652e0708dbdcdc0dd9",
    "KenneyImpact/strike_03.ogg": "7993dd4c156b9979ad69f17be5ebe31850b16039a3857f8477141be54dfee1b3",
    "KenneyInterface/ui_back_01.ogg": "07db973f79f6ae0f2edc34561e7592e24d0577455919fb602cb8ecc0da991dcf",
    "KenneyInterface/ui_back_02.ogg": "61581c58194e3f19f531072edabbc344204c7e0a2887b8ededce4357bcf09195",
    "KenneyInterface/ui_confirm_01.ogg": "063564703b6094d70718a3e787a55cc9141611e4ecd6b6637f8828f79b4a8c3a",
    "KenneyInterface/ui_confirm_02.ogg": "33b17a9a9a2397c62b285c52c33a907fdffb476909c99e42dde603f6a7a8b12c",
}
root = Path("unity/Assets/PenaltyShootout/Audio/Stage9")
for relative, digest in expected.items():
    path = root / relative
    if not path.is_file():
        raise SystemExit(f"Missing licensed audio asset: {path}")
    actual = sha256(path.read_bytes()).hexdigest()
    if actual != digest:
        raise SystemExit(f"Audio hash mismatch: {path}: {actual}")
print(f"Validated {len(expected)} CC0 audio files")
PY

echo "Stage 9: Python contracts"
.venv/bin/python -m pytest -q

echo "Stage 9: Stage 8 web artifact"
npm --prefix web/stage8-analysis test
npm --prefix web/stage8-analysis run build

echo "Stage 9: preparing isolated final assets"
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod PenaltyShootout.Stage0.Editor.Stage9ProjectBuilder.PrepareProject \
  -logFile "$RESULTS/prepare.log"

echo "Stage 9: EditMode presentation and geometry contracts"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage9FinalPresentationTests \
  -testResults "$RESULTS/stage9-editmode-results.xml" \
  -logFile "$RESULTS/stage9-editmode.log"

echo "Stage 9: muted PlayMode native inference and five-shot set"
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode \
  -testFilter PenaltyShootout.Kernel.Tests.Stage9FinalPlayModeTests \
  -testResults "$RESULTS/stage9-playmode-results.xml" \
  -logFile "$RESULTS/stage9-playmode.log"

echo "Stage 9 preparation passed. No standalone was launched."
