from __future__ import annotations

import argparse

import pytest

from mosim_rl.camera_preview import jpeg_quality, positive_float


@pytest.mark.parametrize("value", ["0", "-1", "nan", "inf"])
def test_positive_float_rejects_non_positive_or_non_finite(value: str) -> None:
    with pytest.raises(argparse.ArgumentTypeError):
        positive_float(value)


@pytest.mark.parametrize("value", ["1", "85", "95"])
def test_jpeg_quality_accepts_protocol_range(value: str) -> None:
    assert jpeg_quality(value) == int(value)


@pytest.mark.parametrize("value", ["0", "96"])
def test_jpeg_quality_rejects_out_of_range_values(value: str) -> None:
    with pytest.raises(argparse.ArgumentTypeError):
        jpeg_quality(value)
