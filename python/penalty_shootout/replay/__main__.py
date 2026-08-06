from __future__ import annotations

import argparse

from .v1 import load_replay


def main() -> None:
    parser = argparse.ArgumentParser(description="Validate a penalty-replay-v1 file.")
    parser.add_argument("path")
    args = parser.parse_args()
    replay = load_replay(args.path)
    score = replay["Score"]
    print(
        f"Valid penalty-replay-v1: {score['Goals']} goals, "
        f"{score['Saves']} saves, {score['Misses']} misses"
    )


if __name__ == "__main__":
    main()
