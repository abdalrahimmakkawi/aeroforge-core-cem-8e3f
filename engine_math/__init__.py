"""Thermodynamics, contour, and regenerative-cooling estimates."""

from lazyimp import lazy

evaluate = (lazy)("engine_math.design", "evaluate")

__all__ = ["evaluate"]
