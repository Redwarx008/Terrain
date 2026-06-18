#!/usr/bin/env python3
"""Compare river surface sample pixels against captured expected RGB values."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from PIL import Image


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Compare image RGB values at river surface sample points."
    )
    parser.add_argument("--samples", type=Path, required=True)
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--expected", choices=("current", "refraction"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def rounded_rgb(pixel: tuple[int, ...]) -> list[float]:
    return [round(channel / 255.0, 6) for channel in pixel[:3]]


def max_abs_delta(actual_rgb: list[float], expected_rgb: list[float]) -> float:
    return round(
        max(abs(actual - expected) for actual, expected in zip(actual_rgb, expected_rgb)),
        6,
    )


def load_samples(samples_path: Path) -> list[dict[str, Any]]:
    with samples_path.open("r", encoding="utf-8") as samples_file:
        samples = json.load(samples_file)

    current_points = samples.get("current_points")
    if not isinstance(current_points, list):
        raise ValueError(f"{samples_path} must contain a current_points list")

    return current_points


def build_report(samples_path: Path, image_path: Path, expected: str) -> dict[str, Any]:
    expected_key = f"{expected}_rgb"
    points: list[dict[str, Any]] = []
    worst_max_abs_delta = 0.0

    current_points = load_samples(samples_path)
    with Image.open(image_path) as image:
        rgb_image = image.convert("RGBA")
        for point in current_points:
            x = int(point["x"])
            y = int(point["y"])
            actual_rgb = rounded_rgb(rgb_image.getpixel((x, y)))
            expected_rgb = [round(float(channel), 6) for channel in point[expected_key]]
            point_max_abs_delta = max_abs_delta(actual_rgb, expected_rgb)
            worst_max_abs_delta = max(worst_max_abs_delta, point_max_abs_delta)
            points.append(
                {
                    "name": point["name"],
                    "x": x,
                    "y": y,
                    "actual_rgb": actual_rgb,
                    "expected_rgb": expected_rgb,
                    "max_abs_delta": point_max_abs_delta,
                }
            )

    return {
        "image": str(image_path),
        "expected": expected,
        "worst_max_abs_delta": round(worst_max_abs_delta, 6),
        "points": points,
    }


def main() -> int:
    args = parse_args()
    report = build_report(args.samples, args.image, args.expected)
    report_json = json.dumps(report, indent=2)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report_json + "\n", encoding="utf-8")
    print(report_json)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
